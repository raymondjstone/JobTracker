using JobTracker.Models;
using System.Text.RegularExpressions;

namespace JobTracker.Services;

public class JobRulesService
{
    private readonly AppSettingsService _settingsService;
    private readonly CurrentUserService _currentUser;
    private readonly ILogger<JobRulesService> _logger;

    public event Action? OnChange;

    public JobRulesService(AppSettingsService settingsService, ILogger<JobRulesService> logger, CurrentUserService currentUser)
    {
        _settingsService = settingsService;
        _logger = logger;
        _currentUser = currentUser;
    }

    private Guid CurrentUserId => _currentUser.GetCurrentUserId();

    public JobRulesSettings GetRulesSettings(Guid? forUserId = null)
    {
        return _settingsService.GetSettings(forUserId).JobRules;
    }

    public List<JobRule> GetAllRules(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        return _settingsService.GetSettings(forUserId).JobRules.Rules
            .Where(r => r.UserId == userId || r.UserId == Guid.Empty) // Include user-specific and legacy rules
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToList();
    }

    public List<JobRule> GetEnabledRules(Guid? forUserId = null)
    {
        return GetAllRules(forUserId).Where(r => r.IsEnabled).ToList();
    }

    public JobRule? GetRule(Guid id)
    {
        return _settingsService.GetSettings().JobRules.Rules.FirstOrDefault(r => r.Id == id);
    }

    public void AddRule(JobRule rule)
    {
        var settings = _settingsService.GetSettings();
        rule.Id = Guid.NewGuid();
        rule.UserId = CurrentUserId;
        rule.DateCreated = DateTime.Now;
        settings.JobRules.Rules.Add(rule);
        SaveAndNotify();
        _logger.LogInformation("Added rule: {Name}", rule.Name);
    }

    public void UpdateRule(JobRule rule)
    {
        UpdateRuleInternal(rule);
        OnChange?.Invoke();
        _logger.LogInformation("Updated rule: {Name}", rule.Name);
    }

    private void UpdateRuleInternal(JobRule rule)
    {
        var settings = _settingsService.GetSettings();
        var index = settings.JobRules.Rules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0)
        {
            settings.JobRules.Rules[index] = rule;
            SaveSettings();
        }
    }

    private void SaveSettings()
    {
        _settingsService.Save();
    }

    public void DeleteRule(Guid id)
    {
        var settings = _settingsService.GetSettings();
        var rule = settings.JobRules.Rules.FirstOrDefault(r => r.Id == id);
        if (rule != null)
        {
            settings.JobRules.Rules.Remove(rule);
            SaveAndNotify();
            _logger.LogInformation("Deleted rule: {Name}", rule.Name);
        }
    }

    public void ToggleRule(Guid id)
    {
        var rule = GetRule(id);
        if (rule != null)
        {
            rule.IsEnabled = !rule.IsEnabled;
            UpdateRule(rule);
        }
    }

    public void UpdateGlobalSettings(bool enableAutoRules, bool stopOnFirstMatch)
    {
        var settings = _settingsService.GetSettings();
        settings.JobRules.EnableAutoRules = enableAutoRules;
        settings.JobRules.StopOnFirstMatch = stopOnFirstMatch;
        SaveAndNotify();
    }

    public RuleEvaluationResult EvaluateJob(JobListing job, Guid? forUserId = null)
    {
        var result = new RuleEvaluationResult();
        var userId = forUserId ?? job.UserId; // Use job's UserId if not specified
        if (userId == Guid.Empty) userId = CurrentUserId;

        Console.WriteLine($"[RULES] EvaluateJob called for '{job.Title}' - forUserId: {forUserId}, job.UserId: {job.UserId}, resolved userId: {userId}");

        var rulesSettings = GetRulesSettings(userId);

        Console.WriteLine($"[RULES] Settings loaded - EnableAutoRules: {rulesSettings.EnableAutoRules}, StopOnFirstMatch: {rulesSettings.StopOnFirstMatch}");

        if (!rulesSettings.EnableAutoRules)
        {
            Console.WriteLine($"[RULES] Rules are DISABLED - skipping evaluation for job: {job.Title}");
            _logger.LogDebug("Rules are disabled - skipping evaluation for job: {Title}", job.Title);
            return result;
        }

        var enabledRules = GetEnabledRules(userId);
        Console.WriteLine($"[RULES] Found {enabledRules.Count} enabled rules for user {userId}");
        foreach (var rule in enabledRules.Take(5))
        {
            Console.WriteLine($"[RULES]   - Rule: '{rule.Name}' (Field: {rule.Field}, Op: {rule.Operator}, Value: '{rule.Value}')");
        }

        _logger.LogDebug("Evaluating {Count} enabled rules for job: {Title} (user: {UserId})", enabledRules.Count, job.Title, userId);

        if (enabledRules.Count == 0)
        {
            Console.WriteLine($"[RULES] WARNING: No enabled rules found for user {userId}");
            _logger.LogWarning("No enabled rules found for user {UserId}", userId);
        }

        foreach (var rule in enabledRules)
        {
            if (EvaluateRule(rule, job))
            {
                _logger.LogInformation("Rule '{RuleName}' matched job: {Title}", rule.Name, job.Title);
                result.MatchedRules.Add(rule);

                if (rule.SetInterest.HasValue && !result.Interest.HasValue)
                {
                    result.Interest = rule.SetInterest;
                    result.InterestRuleName = rule.Name;
                    _logger.LogInformation("Rule '{RuleName}' set Interest to {Interest}", rule.Name, result.Interest);
                }

                if (rule.SetSuitability.HasValue && !result.Suitability.HasValue)
                {
                    result.Suitability = rule.SetSuitability;
                    result.SuitabilityRuleName = rule.Name;
                    _logger.LogInformation("Rule '{RuleName}' set Suitability to {Suitability}", rule.Name, result.Suitability);
                }

                if (rule.SetIsRemote.HasValue && !result.IsRemote.HasValue)
                {
                    result.IsRemote = rule.SetIsRemote;
                    result.IsRemoteRuleName = rule.Name;
                    _logger.LogInformation("Rule '{RuleName}' set IsRemote to {IsRemote}", rule.Name, result.IsRemote);
                }

                // Update rule stats (without notifying UI to avoid thread issues)
                rule.TimesTriggered++;
                rule.LastTriggered = DateTime.Now;
                UpdateRuleInternal(rule);

                if (rulesSettings.StopOnFirstMatch)
                {
                    _logger.LogDebug("StopOnFirstMatch is enabled - stopping rule evaluation");
                    break;
                }
            }
        }

        if (result.MatchedRules.Count == 0)
        {
            _logger.LogDebug("No rules matched for job: {Title}", job.Title);
        }

        return result;
    }

    private bool EvaluateRule(JobRule rule, JobListing job)
    {
        // Use compound conditions if available
        if (rule.HasCompoundConditions)
        {
            return EvaluateCompoundConditions(rule, job);
        }

        // Simple single-condition evaluation
        return EvaluateSingleCondition(rule.Field, rule.Operator, rule.Value, rule.CaseSensitive, job);
    }

    private bool EvaluateCompoundConditions(JobRule rule, JobListing job)
    {
        if (rule.Conditions.Count == 0)
        {
            return false;
        }

        if (rule.Logic == ConditionLogic.And)
        {
            // All conditions must match
            return rule.Conditions.All(c => EvaluateSingleCondition(c.Field, c.Operator, c.Value, c.CaseSensitive, job));
        }
        else
        {
            // Any condition must match
            return rule.Conditions.Any(c => EvaluateSingleCondition(c.Field, c.Operator, c.Value, c.CaseSensitive, job));
        }
    }

    private bool EvaluateSingleCondition(RuleField field, RuleOperator op, string value, bool caseSensitive, JobListing job)
    {
        if (field == RuleField.IsRemote)
        {
            // Special case for boolean field
            return op switch
            {
                RuleOperator.IsTrue => job.IsRemote,
                RuleOperator.IsFalse => !job.IsRemote,
                _ => false
            };
        }

        if (field == RuleField.SuitabilityScore)
        {
            // Special case for numeric score comparison
            if (!int.TryParse(value, out var threshold))
            {
                _logger.LogWarning("Invalid score threshold value: {Value}", value);
                return false;
            }

            return op switch
            {
                RuleOperator.GreaterThan => job.SuitabilityScore > threshold,
                RuleOperator.GreaterThanOrEqual => job.SuitabilityScore >= threshold,
                RuleOperator.LessThan => job.SuitabilityScore < threshold,
                RuleOperator.LessThanOrEqual => job.SuitabilityScore <= threshold,
                RuleOperator.Equals => job.SuitabilityScore == threshold,
                RuleOperator.NotEquals => job.SuitabilityScore != threshold,
                _ => false
            };
        }

        if (field == RuleField.Any)
        {
            // Search across all text fields
            var allFields = new[] { RuleField.Title, RuleField.Description, RuleField.Company, RuleField.Location, RuleField.Salary, RuleField.Source };
            return allFields.Any(f => EvaluateCondition(GetFieldValue(f, job), op, value, caseSensitive));
        }

        if (field == RuleField.Skills)
        {
            // Special handling for skills array
            var skillsText = string.Join(" ", job.Skills);
            return EvaluateCondition(skillsText, op, value, caseSensitive);
        }

        var fieldValue = GetFieldValue(field, job);
        return EvaluateCondition(fieldValue, op, value, caseSensitive);
    }

    private string GetFieldValue(RuleField field, JobListing job)
    {
        return field switch
        {
            RuleField.Title => job.Title ?? string.Empty,
            RuleField.Description => job.Description ?? string.Empty,
            RuleField.Company => job.Company ?? string.Empty,
            RuleField.Location => job.Location ?? string.Empty,
            RuleField.Salary => job.Salary ?? string.Empty,
            RuleField.Source => job.Source ?? string.Empty,
            RuleField.Skills => string.Join(" ", job.Skills ?? new List<string>()),
            RuleField.SuitabilityScore => job.SuitabilityScore.ToString(),
            _ => string.Empty
        };
    }

    private bool EvaluateCondition(string fieldValue, RuleOperator op, string ruleValue, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return op switch
        {
            RuleOperator.Contains => fieldValue.Contains(ruleValue, comparison),
            RuleOperator.NotContains => !fieldValue.Contains(ruleValue, comparison),
            RuleOperator.Equals => fieldValue.Equals(ruleValue, comparison),
            RuleOperator.NotEquals => !fieldValue.Equals(ruleValue, comparison),
            RuleOperator.StartsWith => fieldValue.StartsWith(ruleValue, comparison),
            RuleOperator.EndsWith => fieldValue.EndsWith(ruleValue, comparison),
            RuleOperator.Regex => TryRegexMatch(fieldValue, ruleValue, caseSensitive),
            _ => false
        };
    }

    private bool TryRegexMatch(string input, string pattern, bool caseSensitive)
    {
        try
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(input, pattern, options, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Invalid regex pattern '{Pattern}': {Error}", pattern, ex.Message);
            return false;
        }
    }

    public List<JobRule> GetPresetRules()
    {
        return new List<JobRule>
        {
            new JobRule
            {
                Name = "Ignore agency jobs (Noir)",
                Field = RuleField.Description,
                Operator = RuleOperator.Contains,
                Value = "Noir",
                SetSuitability = SuitabilityStatus.Unsuitable
            },
            new JobRule
            {
                Name = "Ignore security clearance required",
                Field = RuleField.Description,
                Operator = RuleOperator.Contains,
                Value = "security clearance",
                SetSuitability = SuitabilityStatus.Unsuitable
            },
            new JobRule
            {
                Name = "Mark remote jobs as interesting",
                Field = RuleField.IsRemote,
                Operator = RuleOperator.IsTrue,
                Value = "",
                SetInterest = InterestStatus.Interested
            },
            new JobRule
            {
                Name = "Ignore junior roles",
                Field = RuleField.Title,
                Operator = RuleOperator.Contains,
                Value = "Junior",
                SetSuitability = SuitabilityStatus.Unsuitable
            },
            new JobRule
            {
                Name = "Ignore graduate roles",
                Field = RuleField.Title,
                Operator = RuleOperator.Contains,
                Value = "Graduate",
                SetSuitability = SuitabilityStatus.Unsuitable
            },
            new JobRule
            {
                Name = "Ignore unpaid internships",
                Field = RuleField.Salary,
                Operator = RuleOperator.Contains,
                Value = "unpaid",
                SetSuitability = SuitabilityStatus.Unsuitable
            },
            new JobRule
            {
                Name = "Highlight senior roles",
                Field = RuleField.Title,
                Operator = RuleOperator.Regex,
                Value = @"\b(Senior|Lead|Principal|Staff)\b",
                SetInterest = InterestStatus.Interested
            },
            new JobRule
            {
                Name = "Remote senior .NET roles",
                Logic = ConditionLogic.And,
                Conditions = new List<RuleCondition>
                {
                    new RuleCondition { Field = RuleField.IsRemote, Operator = RuleOperator.IsTrue },
                    new RuleCondition { Field = RuleField.Title, Operator = RuleOperator.Contains, Value = "Senior" },
                    new RuleCondition { Field = RuleField.Any, Operator = RuleOperator.Regex, Value = @"\.NET|C#|Blazor" }
                },
                SetInterest = InterestStatus.Interested,
                SetSuitability = SuitabilityStatus.Possible
            },
            new JobRule
            {
                Name = "Flag remote jobs from description",
                Logic = ConditionLogic.Or,
                Conditions = new List<RuleCondition>
                {
                    new RuleCondition { Field = RuleField.Description, Operator = RuleOperator.Regex, Value = @"\bremote\b" },
                    new RuleCondition { Field = RuleField.Description, Operator = RuleOperator.Contains, Value = "work from home" }
                },
                SetIsRemote = true
            },
            new JobRule
            {
                Name = "Ignore short contracts or agencies",
                Logic = ConditionLogic.Or,
                Conditions = new List<RuleCondition>
                {
                    new RuleCondition { Field = RuleField.Description, Operator = RuleOperator.Contains, Value = "3 month contract" },
                    new RuleCondition { Field = RuleField.Description, Operator = RuleOperator.Contains, Value = "6 month contract" },
                    new RuleCondition { Field = RuleField.Company, Operator = RuleOperator.Contains, Value = "Recruitment" }
                },
                SetSuitability = SuitabilityStatus.Unsuitable
            }
        };
    }

    private void SaveAndNotify()
    {
        SaveSettings();
        OnChange?.Invoke();
    }
}

public class RuleEvaluationResult
{
    public List<JobRule> MatchedRules { get; set; } = new();
    public InterestStatus? Interest { get; set; }
    public SuitabilityStatus? Suitability { get; set; }
    public bool? IsRemote { get; set; }
    public string? InterestRuleName { get; set; }
    public string? SuitabilityRuleName { get; set; }
    public string? IsRemoteRuleName { get; set; }

    public bool HasMatches => MatchedRules.Count > 0;
}
