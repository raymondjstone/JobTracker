using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JobTracker.Tests;

public class JobRulesServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<AppSettingsService> _settingsMock;
    private readonly Mock<CurrentUserService> _currentUserMock;
    private readonly JobRulesService _service;
    private readonly AppSettings _appSettings;

    public JobRulesServiceTests()
    {
        _appSettings = new AppSettings
        {
            JobRules = new JobRulesSettings
            {
                EnableAutoRules = true,
                StopOnFirstMatch = false,
                Rules = new List<JobRule>()
            }
        };

        _settingsMock = new Mock<AppSettingsService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _settingsMock.Setup(s => s.GetSettings(It.IsAny<Guid?>())).Returns(_appSettings);
        _settingsMock.Setup(s => s.Save());

        _currentUserMock = new Mock<CurrentUserService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(_userId);

        var logger = new Mock<ILogger<JobRulesService>>();
        _service = new JobRulesService(_settingsMock.Object, logger.Object, _currentUserMock.Object);
    }

    private JobListing MakeJob(string title = "Developer", string company = "Acme",
        string description = "", string location = "", bool isRemote = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _userId,
        Title = title,
        Company = company,
        Description = description,
        Location = location,
        IsRemote = isRemote,
        Skills = new List<string>()
    };

    private void AddRule(JobRule rule)
    {
        rule.UserId = _userId;
        rule.IsEnabled = true;
        _appSettings.JobRules.Rules.Add(rule);
    }

    [Fact]
    public void EvaluateJob_NoRules_ReturnsEmptyResult()
    {
        var result = _service.EvaluateJob(MakeJob(), _userId);

        Assert.False(result.HasMatches);
        Assert.Null(result.Interest);
        Assert.Null(result.Suitability);
    }

    [Fact]
    public void EvaluateJob_ContainsRule_MatchesTitle()
    {
        AddRule(new JobRule
        {
            Name = "Senior check",
            Field = RuleField.Title,
            Operator = RuleOperator.Contains,
            Value = "Senior",
            SetInterest = InterestStatus.Interested
        });

        var result = _service.EvaluateJob(MakeJob(title: "Senior Developer"), _userId);

        Assert.True(result.HasMatches);
        Assert.Equal(InterestStatus.Interested, result.Interest);
    }

    [Fact]
    public void EvaluateJob_NotContainsRule_ExcludesNonMatching()
    {
        AddRule(new JobRule
        {
            Name = "No Junior",
            Field = RuleField.Title,
            Operator = RuleOperator.NotContains,
            Value = "Junior",
            SetSuitability = SuitabilityStatus.Possible
        });

        var result = _service.EvaluateJob(MakeJob(title: "Senior Developer"), _userId);

        Assert.True(result.HasMatches);
        Assert.Equal(SuitabilityStatus.Possible, result.Suitability);
    }

    [Fact]
    public void EvaluateJob_NotContainsRule_DoesNotMatchWhenValuePresent()
    {
        AddRule(new JobRule
        {
            Name = "No Junior",
            Field = RuleField.Title,
            Operator = RuleOperator.NotContains,
            Value = "Junior",
            SetSuitability = SuitabilityStatus.Possible
        });

        var result = _service.EvaluateJob(MakeJob(title: "Junior Developer"), _userId);

        Assert.False(result.HasMatches);
    }

    [Fact]
    public void EvaluateJob_RegexRule_MatchesPattern()
    {
        AddRule(new JobRule
        {
            Name = "Senior regex",
            Field = RuleField.Title,
            Operator = RuleOperator.Regex,
            Value = @"\b(Senior|Lead)\b",
            SetInterest = InterestStatus.Interested
        });

        var result = _service.EvaluateJob(MakeJob(title: "Lead Engineer"), _userId);

        Assert.True(result.HasMatches);
        Assert.Equal(InterestStatus.Interested, result.Interest);
    }

    [Fact]
    public void EvaluateJob_InvalidRegex_DoesNotThrow()
    {
        AddRule(new JobRule
        {
            Name = "Bad regex",
            Field = RuleField.Title,
            Operator = RuleOperator.Regex,
            Value = @"[invalid(",
            SetInterest = InterestStatus.Interested
        });

        var result = _service.EvaluateJob(MakeJob(title: "Developer"), _userId);

        Assert.False(result.HasMatches);
    }

    [Fact]
    public void EvaluateJob_CompoundAnd_AllConditionsMustMatch()
    {
        AddRule(new JobRule
        {
            Name = "Remote Senior",
            Logic = ConditionLogic.And,
            Conditions = new List<RuleCondition>
            {
                new() { Field = RuleField.Title, Operator = RuleOperator.Contains, Value = "Senior" },
                new() { Field = RuleField.IsRemote, Operator = RuleOperator.IsTrue }
            },
            SetInterest = InterestStatus.Interested
        });

        // Only title matches — should not fire
        var partialResult = _service.EvaluateJob(MakeJob(title: "Senior Developer", isRemote: false), _userId);
        Assert.False(partialResult.HasMatches);

        // Both match
        var fullResult = _service.EvaluateJob(MakeJob(title: "Senior Developer", isRemote: true), _userId);
        Assert.True(fullResult.HasMatches);
    }

    [Fact]
    public void EvaluateJob_CompoundOr_AnyConditionCanMatch()
    {
        AddRule(new JobRule
        {
            Name = "Remote or Senior",
            Logic = ConditionLogic.Or,
            Conditions = new List<RuleCondition>
            {
                new() { Field = RuleField.Title, Operator = RuleOperator.Contains, Value = "Senior" },
                new() { Field = RuleField.IsRemote, Operator = RuleOperator.IsTrue }
            },
            SetInterest = InterestStatus.Interested
        });

        var result = _service.EvaluateJob(MakeJob(title: "Junior Developer", isRemote: true), _userId);

        Assert.True(result.HasMatches);
        Assert.Equal(InterestStatus.Interested, result.Interest);
    }

    [Fact]
    public void EvaluateJob_StopOnFirstMatch_OnlyFirstRuleApplied()
    {
        _appSettings.JobRules.StopOnFirstMatch = true;

        AddRule(new JobRule
        {
            Name = "First rule",
            Priority = 10,
            Field = RuleField.Title,
            Operator = RuleOperator.Contains,
            Value = "Developer",
            SetInterest = InterestStatus.Interested
        });

        AddRule(new JobRule
        {
            Name = "Second rule",
            Priority = 5,
            Field = RuleField.Title,
            Operator = RuleOperator.Contains,
            Value = "Developer",
            SetSuitability = SuitabilityStatus.Unsuitable
        });

        var result = _service.EvaluateJob(MakeJob(title: "Developer"), _userId);

        Assert.Single(result.MatchedRules);
        Assert.Equal("First rule", result.MatchedRules[0].Name);
        Assert.Equal(InterestStatus.Interested, result.Interest);
        Assert.Null(result.Suitability);
    }

    [Fact]
    public void EvaluateJob_SetsInterestAndSuitability()
    {
        AddRule(new JobRule
        {
            Name = "Mark unsuitable",
            Field = RuleField.Company,
            Operator = RuleOperator.Contains,
            Value = "BadCorp",
            SetInterest = InterestStatus.NotInterested,
            SetSuitability = SuitabilityStatus.Unsuitable
        });

        var result = _service.EvaluateJob(MakeJob(company: "BadCorp Ltd"), _userId);

        Assert.Equal(InterestStatus.NotInterested, result.Interest);
        Assert.Equal(SuitabilityStatus.Unsuitable, result.Suitability);
    }

    [Fact]
    public void EvaluateJob_RulesDisabled_ReturnsEmptyResult()
    {
        _appSettings.JobRules.EnableAutoRules = false;

        AddRule(new JobRule
        {
            Name = "Should not fire",
            Field = RuleField.Title,
            Operator = RuleOperator.Contains,
            Value = "Developer",
            SetInterest = InterestStatus.Interested
        });

        var result = _service.EvaluateJob(MakeJob(title: "Developer"), _userId);

        Assert.False(result.HasMatches);
    }

    [Fact]
    public void EvaluateJob_SetIsRemote_SetsRemoteFlag()
    {
        AddRule(new JobRule
        {
            Name = "Flag remote",
            Field = RuleField.Description,
            Operator = RuleOperator.Contains,
            Value = "work from home",
            SetIsRemote = true
        });

        var result = _service.EvaluateJob(MakeJob(description: "Fully work from home position"), _userId);

        Assert.True(result.HasMatches);
        Assert.True(result.IsRemote);
    }
}
