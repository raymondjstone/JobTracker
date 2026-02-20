using JobTracker.Models;
using System.Text.RegularExpressions;

namespace JobTracker.Services;

public class JobListingService
{
    private readonly List<JobListing> _jobListings = new();
    private readonly object _lock = new();
    private readonly ILogger<JobListingService> _logger;
    private readonly IStorageBackend _storage;
    private readonly CurrentUserService _currentUser;
    private readonly Lazy<JobRulesService> _rulesService;
    private readonly Lazy<JobHistoryService> _historyService;

    public event Action? OnChange;

    public JobListingService(
        IStorageBackend storage,
        ILogger<JobListingService> logger,
        CurrentUserService currentUser,
        Lazy<JobRulesService> rulesService,
        Lazy<JobHistoryService> historyService)
    {
        _logger = logger;
        _storage = storage;
        _currentUser = currentUser;
        _rulesService = rulesService;
        _historyService = historyService;

        // Note: We can't load jobs here as we don't have user context yet
        // Jobs are loaded lazily when GetAllJobListings is called
    }

    private Guid CurrentUserId => _currentUser.GetCurrentUserId();

    private Guid _loadedForUser = Guid.Empty;

    private void EnsureJobsLoaded(Guid? forUserId = null)
    {
        lock (_lock)
        {
            var userId = forUserId ?? CurrentUserId;
            if (userId == Guid.Empty) return;

            // Only reload if:
            // 1. List is empty
            // 2. User has changed
            // This prevents constant reloading which causes duplicates from race conditions
            var needsReload = !_jobListings.Any() || 
                             (_jobListings.Any() && _jobListings.First().UserId != userId) ||
                             _loadedForUser != userId;

            if (needsReload)
            {
                _jobListings.Clear();
                var jobs = _storage.LoadJobs(userId);
                _jobListings.AddRange(jobs);
                _loadedForUser = userId;
                _logger.LogDebug("Loaded {Count} jobs for user {UserId}", jobs.Count, userId);

                // Backfill parsed salary data for existing jobs
                BackfillSalaryData(userId);
            }
        }
    }

    /// <summary>
    /// Backfills SalaryMin/SalaryMax for jobs that have a Salary string but no parsed values.
    /// Called once when jobs are loaded.
    /// </summary>
    private void BackfillSalaryData(Guid userId)
    {
        var jobsToBackfill = _jobListings
            .Where(j => j.UserId == userId && !string.IsNullOrWhiteSpace(j.Salary) && !j.SalaryMin.HasValue && !j.SalaryMax.HasValue)
            .ToList();

        if (jobsToBackfill.Count == 0) return;

        foreach (var job in jobsToBackfill)
        {
            var (min, max) = SalaryParser.Parse(job.Salary);
            job.SalaryMin = min;
            job.SalaryMax = max;
        }

        _storage.SaveJobs(_jobListings.Where(j => j.UserId == userId).ToList(), userId);
        _logger.LogInformation("Backfilled salary data for {Count} jobs", jobsToBackfill.Count);
    }

    /// <summary>
    /// Forces a reload from storage, bypassing the cache
    /// Used by UI refresh timers to pick up changes from other instances
    /// </summary>
    public void ForceReload(Guid? forUserId = null)
    {
        lock (_lock)
        {
            var userId = forUserId ?? CurrentUserId;
            if (userId == Guid.Empty) return;

            _jobListings.RemoveAll(j => j.UserId == userId);
            var jobs = _storage.LoadJobs(userId);
            _jobListings.AddRange(jobs.Where(j => j.UserId == userId));
            _loadedForUser = userId;
            _logger.LogDebug("Force reloaded {Count} jobs for user {UserId}", jobs.Count, userId);
        }
    }

    public IReadOnlyList<JobListing> GetAllJobListings(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            return _jobListings.Where(j => j.UserId == userId).ToList().AsReadOnly();
        }
    }

    public JobListing? GetJobListingById(Guid id, Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            return _jobListings.FirstOrDefault(j => j.Id == id && j.UserId == userId);
        }
    }

    public JobListing? GetJobById(Guid id) => GetJobListingById(id);

    /// <summary>
    /// Adds a job listing if it doesn't already exist (based on URL which contains LinkedIn job ID)
    /// </summary>
    /// <returns>True if added, false if duplicate</returns>
    public bool AddJobListing(JobListing jobListing, Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        if (userId == Guid.Empty)
        {
            // If called from API, UserId should already be set on the job
            if (jobListing.UserId == Guid.Empty)
            {
                _logger.LogWarning("Cannot add job without user context");
                return false;
            }
            userId = jobListing.UserId;
        }
        
        EnsureJobsLoaded(userId);

        lock (_lock)
        {
            // Clean the job data
            CleanJobData(jobListing);

            // Primary check: duplicate by URL (contains LinkedIn job ID) - for this user only
            if (!string.IsNullOrWhiteSpace(jobListing.Url))
            {
                var normalizedUrl = NormalizeUrl(jobListing.Url);
                var existingByUrl = _jobListings.FirstOrDefault(j =>
                    j.UserId == userId && NormalizeUrl(j.Url) == normalizedUrl);

                if (existingByUrl != null)
                {
                    _logger.LogInformation("Duplicate job skipped (URL match): {Title} at {Company}",
                        jobListing.Title, jobListing.Company);
                    return false;
                }
            }

            // Secondary check: title + company combination (for jobs without URLs) - for this user only
            if (!string.IsNullOrWhiteSpace(jobListing.Title) && !string.IsNullOrWhiteSpace(jobListing.Company))
            {
                var existingByTitleCompany = _jobListings.FirstOrDefault(j =>
                    j.UserId == userId &&
                    string.Equals(j.Title?.Trim(), jobListing.Title?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(j.Company?.Trim(), jobListing.Company?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (existingByTitleCompany != null)
                {
                    _logger.LogInformation("Duplicate job skipped (Title+Company match): {Title} at {Company}",
                        jobListing.Title, jobListing.Company);
                    return false;
                }
            }

            jobListing.Id = Guid.NewGuid();
            jobListing.UserId = userId;
            jobListing.DateAdded = DateTime.Now;
            jobListing.LastChecked = DateTime.Now;
            if (jobListing.Interest == default)
                jobListing.Interest = InterestStatus.NotRated;

            // Auto-infer source from URL if not set
            if (string.IsNullOrWhiteSpace(jobListing.Source))
            {
                jobListing.Source = InferSourceFromUrl(jobListing.Url);
            }

            // Parse salary into min/max
            var (salaryMin, salaryMax) = SalaryParser.Parse(jobListing.Salary);
            jobListing.SalaryMin = salaryMin;
            jobListing.SalaryMax = salaryMax;

            // Apply job rules
            RuleEvaluationResult? ruleResult = null;
            {
                ruleResult = _rulesService.Value.EvaluateJob(jobListing);
                if (ruleResult.Interest.HasValue)
                {
                    jobListing.Interest = ruleResult.Interest.Value;
                    _logger.LogInformation("Rule '{Rule}' applied: Set interest to {Interest} for {Title}", ruleResult.InterestRuleName, ruleResult.Interest, jobListing.Title);
                }
                if (ruleResult.Suitability.HasValue)
                {
                    jobListing.Suitability = ruleResult.Suitability.Value;
                    _logger.LogInformation("Rule '{Rule}' applied: Set suitability to {Suitability} for {Title}", ruleResult.SuitabilityRuleName, ruleResult.Suitability, jobListing.Title);
                }
                if (ruleResult.IsRemote.HasValue)
                {
                    jobListing.IsRemote = ruleResult.IsRemote.Value;
                    _logger.LogInformation("Rule '{Rule}' applied: Set IsRemote to {IsRemote} for {Title}", ruleResult.IsRemoteRuleName, ruleResult.IsRemote, jobListing.Title);
                }
            }

            _jobListings.Add(jobListing);

            _storage.AddJob(jobListing);
            NotifyStateChanged();

            // Record history
            _historyService.Value.RecordJobAdded(jobListing, HistoryChangeSource.BrowserExtension);
            if (ruleResult != null && ruleResult.HasMatches)
            {
                if (jobListing.Interest != InterestStatus.NotRated)
                {
                    _historyService.Value.RecordInterestChanged(jobListing, InterestStatus.NotRated, jobListing.Interest,
                        HistoryChangeSource.Rule, ruleResult.InterestRuleName);
                }
                if (jobListing.Suitability != SuitabilityStatus.NotChecked)
                {
                    _historyService.Value.RecordSuitabilityChanged(jobListing, SuitabilityStatus.NotChecked, jobListing.Suitability,
                        HistoryChangeSource.Rule, ruleResult.SuitabilityRuleName);
                }
                if (ruleResult.IsRemote.HasValue)
                {
                    _historyService.Value.RecordRemoteStatusChanged(jobListing, false, jobListing.IsRemote,
                        HistoryChangeSource.Rule, ruleResult.IsRemoteRuleName);
                }
            }

            _logger.LogInformation("Job added: {Title} at {Company}", jobListing.Title, jobListing.Company);
            return true;
        }
    }

    /// <summary>
    /// Adds multiple job listings, skipping duplicates
    /// </summary>
    /// <returns>Number of jobs actually added</returns>
    public int AddJobListings(IEnumerable<JobListing> jobListings)
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        if (userId == Guid.Empty) return 0;

        int addedCount = 0;
        lock (_lock)
        {
            foreach (var job in jobListings)
            {
                CleanJobData(job);

                // Check for duplicate by URL for this user
                if (!string.IsNullOrWhiteSpace(job.Url))
                {
                    var normalizedUrl = NormalizeUrl(job.Url);
                    var existingByUrl = _jobListings.FirstOrDefault(j =>
                        j.UserId == userId && NormalizeUrl(j.Url) == normalizedUrl);

                    if (existingByUrl != null)
                    {
                        continue;
                    }
                }

                // Check by title + company for this user
                if (!string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(job.Company))
                {
                    var existingByTitleCompany = _jobListings.FirstOrDefault(j =>
                        j.UserId == userId &&
                        string.Equals(j.Title?.Trim(), job.Title?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(j.Company?.Trim(), job.Company?.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (existingByTitleCompany != null)
                    {
                        continue;
                    }
                }

                job.Id = Guid.NewGuid();
                job.UserId = userId;
                job.DateAdded = DateTime.Now;
                if (job.Interest == default)
                    job.Interest = InterestStatus.NotRated;

                // Auto-infer source from URL if not set
                if (string.IsNullOrWhiteSpace(job.Source))
                {
                    job.Source = InferSourceFromUrl(job.Url);
                }

                // Parse salary into min/max
                var (salMin, salMax) = SalaryParser.Parse(job.Salary);
                job.SalaryMin = salMin;
                job.SalaryMax = salMax;

                // Apply job rules
                {
                    var ruleResult = _rulesService.Value.EvaluateJob(job);
                    if (ruleResult.Interest.HasValue)
                    {
                        job.Interest = ruleResult.Interest.Value;
                    }
                    if (ruleResult.Suitability.HasValue)
                    {
                        job.Suitability = ruleResult.Suitability.Value;
                    }
                    if (ruleResult.IsRemote.HasValue)
                    {
                        job.IsRemote = ruleResult.IsRemote.Value;
                    }
                }

                _jobListings.Add(job);
                addedCount++;
            }

            if (addedCount > 0)
            {
                _storage.SaveJobs(_jobListings.Where(j => j.UserId == userId).ToList(), userId);
                NotifyStateChanged();
            }

            _logger.LogInformation("Added {Count} jobs (duplicates skipped)", addedCount);
        }
        return addedCount;
    }

    /// <summary>
    /// Checks if a job with the given URL already exists for the current user
    /// </summary>
    public bool JobExists(string url, Guid? forUserId = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            var normalizedUrl = NormalizeUrl(url);
            return _jobListings.Any(j => j.UserId == userId && NormalizeUrl(j.Url) == normalizedUrl);
        }
    }

    /// <summary>
    /// Updates the interest status of a job
    /// </summary>
    public void SetInterestStatus(Guid id, InterestStatus status)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                var oldStatus = job.Interest;
                job.Interest = status;
                _storage.SaveJob(job);
                NotifyStateChanged();
                _historyService.Value.RecordInterestChanged(job, oldStatus, status, HistoryChangeSource.Manual);
                _logger.LogInformation("Updated interest for {Title}: {Status}", job.Title, status);
            }
        }
    }

    /// <summary>
    /// Toggles the applied status of a job
    /// </summary>
    public void SetAppliedStatus(Guid id, bool hasApplied)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                job.HasApplied = hasApplied;
                job.DateApplied = hasApplied ? DateTime.Now : null;
                
                // Set initial stage when marking as applied
                if (hasApplied && job.ApplicationStage == ApplicationStage.None)
                {
                    job.ApplicationStage = ApplicationStage.Applied;
                    
                    // Record the initial stage in history
                    job.StageHistory ??= new List<ApplicationStageChange>();
                    job.StageHistory.Add(new ApplicationStageChange
                    {
                        Stage = ApplicationStage.Applied,
                        DateChanged = DateTime.Now
                    });
                }
                else if (!hasApplied)
                {
                    job.ApplicationStage = ApplicationStage.None;
                    // Optionally clear history when un-applying, or keep it for reference
                    // job.StageHistory?.Clear();
                }
                _storage.SaveJob(job);
                NotifyStateChanged();
                _historyService.Value.RecordAppliedStatusChanged(job, !hasApplied, hasApplied, HistoryChangeSource.Manual);
                _logger.LogInformation("Updated applied status for {Title}: {Applied}", job.Title, hasApplied);
            }
        }
    }

    /// <summary>
    /// Sets the application stage of a job
    /// </summary>
    public void SetApplicationStage(Guid id, ApplicationStage stage, HistoryChangeSource source = HistoryChangeSource.Manual)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                var previousStage = job.ApplicationStage;
                job.ApplicationStage = stage;

                // Record the stage change in history
                if (stage != previousStage)
                {
                    job.StageHistory ??= new List<ApplicationStageChange>();
                    job.StageHistory.Add(new ApplicationStageChange
                    {
                        Stage = stage,
                        DateChanged = DateTime.Now
                    });
                }

                // If setting a stage other than None, ensure HasApplied is true
                if (stage != ApplicationStage.None && !job.HasApplied)
                {
                    job.HasApplied = true;
                    job.DateApplied = DateTime.Now;
                }
                _storage.SaveJob(job);
                NotifyStateChanged();
                if (stage != previousStage)
                {
                    _historyService.Value.RecordApplicationStageChanged(job, previousStage.ToString(), stage.ToString(), source);
                }
                _logger.LogInformation("Updated application stage for {Title}: {Stage}", job.Title, stage);
            }
        }
    }

    public void SetNotes(Guid id, string notes)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                job.Notes = notes ?? string.Empty;
                _storage.SaveJob(job);
                NotifyStateChanged();
            }
        }
    }

    public void SetCoverLetter(Guid id, string coverLetter)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                job.CoverLetter = coverLetter ?? string.Empty;
                _storage.SaveJob(job);
                NotifyStateChanged();
            }
        }
    }

    /// <summary>
    /// Cycles to the next application stage
    /// </summary>
    public void CycleApplicationStage(Guid id)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null && job.HasApplied)
            {
                var stages = Enum.GetValues<ApplicationStage>().Where(s => s != ApplicationStage.None).ToArray();
                var currentIndex = Array.IndexOf(stages, job.ApplicationStage);
                var nextIndex = (currentIndex + 1) % stages.Length;
                var newStage = stages[nextIndex];
                
                // Record the stage change in history
                job.StageHistory ??= new List<ApplicationStageChange>();
                job.StageHistory.Add(new ApplicationStageChange
                {
                    Stage = newStage,
                    DateChanged = DateTime.Now
                });
                
                job.ApplicationStage = newStage;
                _storage.SaveJob(job);
                NotifyStateChanged();
                _logger.LogInformation("Cycled application stage for {Title}: {Stage}", job.Title, job.ApplicationStage);
            }
        }
    }

    /// <summary>
    /// Sets the suitability status of a job
    /// </summary>
    public void SetSuitabilityStatus(Guid id, SuitabilityStatus status)
    {
        SetSuitabilityStatus(id, status, HistoryChangeSource.Manual);
    }

    /// <summary>
    /// Sets the suitability status of a job with an explicit history source
    /// </summary>
    public void SetSuitabilityStatus(Guid id, SuitabilityStatus status, HistoryChangeSource source, string? reason = null, Guid? forUserId = null)
    {
        EnsureJobsLoaded(forUserId);
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                var oldStatus = job.Suitability;
                job.Suitability = status;
                _storage.SaveJob(job);
                NotifyStateChanged();
                _historyService.Value.RecordSuitabilityChanged(job, oldStatus, status, source, reason: reason);
                _logger.LogInformation("Updated suitability for {Title}: {Status} (source: {Source})", job.Title, status, source);
            }
        }
    }

    /// <summary>
    /// Marks a job as shared to WhatsApp
    /// </summary>
    public void SetSharedToWhatsApp(Guid id, bool shared)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                job.SharedToWhatsApp = shared;
                job.DateSharedToWhatsApp = shared ? DateTime.Now : null;
                _storage.SaveJob(job);
                NotifyStateChanged();
                _logger.LogInformation("Updated WhatsApp share status for {Title}: {Shared}", job.Title, shared);
            }
        }
    }

    /// <summary>
    /// Updates job fields if the parsed data is better than what's stored.
    /// "Better" means: non-empty where stored is empty, or longer description.
    /// </summary>
    public void UpdateJobIfBetter(Guid id, JobListing parsed)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job == null || parsed == null) return;

            var changed = false;

            if (!string.IsNullOrWhiteSpace(parsed.Description) &&
                (string.IsNullOrWhiteSpace(job.Description) || parsed.Description.Length > job.Description.Length + 50))
            {
                var cleaned = LinkedInJobExtractor.CleanDescription(parsed.Description);
                if (cleaned.Length > (job.Description?.Length ?? 0))
                {
                    job.Description = cleaned;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed.Salary) && string.IsNullOrWhiteSpace(job.Salary))
            {
                job.Salary = parsed.Salary;
                var (salMin, salMax) = SalaryParser.Parse(job.Salary);
                job.SalaryMin = salMin;
                job.SalaryMax = salMax;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(parsed.Company) && string.IsNullOrWhiteSpace(job.Company))
            {
                job.Company = parsed.Company;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(parsed.Location) && string.IsNullOrWhiteSpace(job.Location))
            {
                job.Location = parsed.Location;
                changed = true;
            }

            if (parsed.Skills != null && parsed.Skills.Count > 0 && (job.Skills == null || job.Skills.Count == 0))
            {
                job.Skills = parsed.Skills;
                changed = true;
            }

            if (parsed.IsRemote && !job.IsRemote)
            {
                job.IsRemote = true;
                changed = true;
            }

            if (changed)
            {
                job.LastChecked = DateTime.Now;
                _storage.SaveJob(job);
                NotifyStateChanged();
                _logger.LogInformation("Updated job details from availability check: {Title}", job.Title);
            }
        }
    }

    /// <summary>
    /// Updates the LastChecked timestamp for a job
    /// </summary>
    public void SetLastChecked(Guid id, Guid? forUserId = null)
    {
        EnsureJobsLoaded(forUserId);
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                job.LastChecked = DateTime.Now;
                _storage.SaveJob(job);
            }
        }
    }

    /// <summary>
    /// Finds a job by URL for a given user.
    /// </summary>
    public JobListing? FindJobByUrl(string url, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            var normalizedUrl = NormalizeUrl(url);
            return _jobListings.FirstOrDefault(j => j.UserId == userId && NormalizeUrl(j.Url) == normalizedUrl);
        }
    }

    /// <summary>
    /// Updates the description of an existing job by URL
    /// Also re-evaluates rules since description content may trigger new matches
    /// </summary>
    public bool UpdateJobDescription(string url, string description, Guid? forUserId = null, string? company = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            var normalizedUrl = NormalizeUrl(url);
            var job = _jobListings.FirstOrDefault(j => j.UserId == userId && NormalizeUrl(j.Url) == normalizedUrl);

            if (job == null) return false;

            // Update if: no existing description, OR new description is substantial (>100 chars) and different
            var hasNoDescription = string.IsNullOrWhiteSpace(job.Description) || job.Description.Length < 100;
            var newIsSubstantial = !string.IsNullOrWhiteSpace(description) && description.Length >= 100;
            var newIsDifferent = job.Description != description;

            if (hasNoDescription || (newIsSubstantial && newIsDifferent))
            {
                var oldLength = job.Description?.Length ?? 0;
                var oldInterest = job.Interest;
                var oldSuitability = job.Suitability;
                var oldIsRemote = job.IsRemote;

                // Clean LinkedIn boilerplate from the description
                var cleanedDescription = LinkedInJobExtractor.CleanDescription(description);
                job.Description = cleanedDescription;
                job.LastChecked = DateTime.Now;

                // Update company if missing and provided
                var oldCompany = job.Company;
                if (!string.IsNullOrWhiteSpace(company) && string.IsNullOrWhiteSpace(job.Company))
                {
                    job.Company = company.Trim();
                    _logger.LogInformation("Set company to '{Company}' for job: {Title}", job.Company, job.Title);
                    _historyService.Value.RecordCompanyUpdated(job, oldCompany, job.Company, HistoryChangeSource.AutoFetch);
                }

                // Re-evaluate rules now that we have description content
                // Only apply to unclassified fields (don't override user's manual choices)
                RuleEvaluationResult? ruleResult = null;
                {
                    ruleResult = _rulesService.Value.EvaluateJob(job);
                    if (ruleResult.Interest.HasValue && job.Interest == InterestStatus.NotRated)
                    {
                        job.Interest = ruleResult.Interest.Value;
                        _logger.LogInformation("Rule '{Rule}' applied after description update: Set interest to {Interest} for {Title}",
                            ruleResult.InterestRuleName, ruleResult.Interest, job.Title);
                    }
                    if (ruleResult.Suitability.HasValue && job.Suitability == SuitabilityStatus.NotChecked)
                    {
                        job.Suitability = ruleResult.Suitability.Value;
                        _logger.LogInformation("Rule '{Rule}' applied after description update: Set suitability to {Suitability} for {Title}",
                            ruleResult.SuitabilityRuleName, ruleResult.Suitability, job.Title);
                    }
                    if (ruleResult.IsRemote.HasValue && !job.IsRemote)
                    {
                        job.IsRemote = ruleResult.IsRemote.Value;
                        _logger.LogInformation("Rule '{Rule}' applied after description update: Set IsRemote to {IsRemote} for {Title}",
                            ruleResult.IsRemoteRuleName, ruleResult.IsRemote, job.Title);
                    }
                }

                _storage.SaveJob(job);
                NotifyStateChanged();

                // Record history
                _historyService.Value.RecordDescriptionUpdated(job, oldLength, cleanedDescription.Length, HistoryChangeSource.AutoFetch);
                if (ruleResult != null && ruleResult.HasMatches)
                {
                    if (job.Interest != oldInterest)
                    {
                        _historyService.Value.RecordInterestChanged(job, oldInterest, job.Interest,
                            HistoryChangeSource.Rule, ruleResult.InterestRuleName);
                    }
                    if (job.Suitability != oldSuitability)
                    {
                        _historyService.Value.RecordSuitabilityChanged(job, oldSuitability, job.Suitability,
                            HistoryChangeSource.Rule, ruleResult.SuitabilityRuleName);
                    }
                    if (job.IsRemote != oldIsRemote)
                    {
                        _historyService.Value.RecordRemoteStatusChanged(job, oldIsRemote, job.IsRemote,
                            HistoryChangeSource.Rule, ruleResult.IsRemoteRuleName);
                    }
                }

                _logger.LogInformation("Updated description for job: {Title} ({Chars} chars, cleaned from {OriginalChars})",
                    job.Title, cleanedDescription.Length, description.Length);
                return true;
            }

            // Even if description didn't change, update company if missing
            if (!string.IsNullOrWhiteSpace(company) && string.IsNullOrWhiteSpace(job.Company))
            {
                var oldCompany2 = job.Company;
                var oldInterest = job.Interest;
                var oldSuitability = job.Suitability;
                var oldIsRemote = job.IsRemote;

                job.Company = company.Trim();
                _historyService.Value.RecordCompanyUpdated(job, oldCompany2, job.Company, HistoryChangeSource.AutoFetch);

                // Re-evaluate rules now that we have company info
                var ruleResult = _rulesService.Value.EvaluateJob(job);
                if (ruleResult.Interest.HasValue && job.Interest == InterestStatus.NotRated)
                    job.Interest = ruleResult.Interest.Value;
                if (ruleResult.Suitability.HasValue && job.Suitability == SuitabilityStatus.NotChecked)
                    job.Suitability = ruleResult.Suitability.Value;
                if (ruleResult.IsRemote.HasValue && !job.IsRemote)
                    job.IsRemote = ruleResult.IsRemote.Value;

                _storage.SaveJob(job);
                NotifyStateChanged();
                _logger.LogInformation("Set company to '{Company}' for job: {Title}", job.Company, job.Title);

                if (ruleResult.HasMatches)
                {
                    if (job.Interest != oldInterest)
                        _historyService.Value.RecordInterestChanged(job, oldInterest, job.Interest, HistoryChangeSource.Rule, ruleResult.InterestRuleName);
                    if (job.Suitability != oldSuitability)
                        _historyService.Value.RecordSuitabilityChanged(job, oldSuitability, job.Suitability, HistoryChangeSource.Rule, ruleResult.SuitabilityRuleName);
                    if (job.IsRemote != oldIsRemote)
                        _historyService.Value.RecordRemoteStatusChanged(job, oldIsRemote, job.IsRemote, HistoryChangeSource.Rule, ruleResult.IsRemoteRuleName);
                }

                return true;
            }

            _logger.LogDebug("Skipped description update for {Title}: existing={ExistingLen}, new={NewLen}",
                job.Title, job.Description?.Length ?? 0, description.Length);
            return false;
        }
    }

    public void UpdateJobListing(JobListing jobListing)
    {
        lock (_lock)
        {
            // Parse salary into min/max
            var (salaryMin, salaryMax) = SalaryParser.Parse(jobListing.Salary);
            jobListing.SalaryMin = salaryMin;
            jobListing.SalaryMax = salaryMax;

            var existingJob = _jobListings.FirstOrDefault(j => j.Id == jobListing.Id);
            if (existingJob != null)
            {
                var index = _jobListings.IndexOf(existingJob);
                _jobListings[index] = jobListing;
                _storage.SaveJob(jobListing);
                NotifyStateChanged();
            }
        }
    }

    public void DeleteJobListing(Guid id)
    {
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                _jobListings.Remove(job);
                _storage.DeleteJob(id);
                NotifyStateChanged();
            }
        }
    }

    public void ClearAllJobListings(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        lock (_lock)
        {
            _jobListings.RemoveAll(j => j.UserId == userId);
            _storage.DeleteAllJobs(userId);
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Cleans up all existing job data (removes duplicated text, etc.)
    /// </summary>
    public int CleanupAllJobs(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            int cleanedCount = 0;
            foreach (var job in _jobListings.Where(j => j.UserId == userId))
            {
                var originalTitle = job.Title;
                var originalCompany = job.Company;
                
                CleanJobData(job);
                
                if (job.Title != originalTitle || job.Company != originalCompany)
                {
                    cleanedCount++;
                }
            }

            if (cleanedCount > 0)
            {
                _storage.SaveJobs(_jobListings.Where(j => j.UserId == userId).ToList(), userId);
                NotifyStateChanged();
                _logger.LogInformation("Cleaned up {Count} jobs", cleanedCount);
            }

            return cleanedCount;
        }
    }

    /// <summary>
    /// Cleans LinkedIn boilerplate from all existing job descriptions.
    /// </summary>
    public int CleanAllDescriptions(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            int cleanedCount = 0;
            foreach (var job in _jobListings.Where(j => j.UserId == userId))
            {
                if (string.IsNullOrWhiteSpace(job.Description))
                    continue;

                var originalLength = job.Description.Length;
                var cleanedDescription = LinkedInJobExtractor.CleanDescription(job.Description);

                if (cleanedDescription.Length < originalLength)
                {
                    job.Description = cleanedDescription;
                    cleanedCount++;
                    _logger.LogInformation("Cleaned description for {Title}: {OrigLen} -> {NewLen} chars",
                        job.Title, originalLength, cleanedDescription.Length);
                }
            }

            if (cleanedCount > 0)
            {
                _storage.SaveJobs(_jobListings.Where(j => j.UserId == userId).ToList(), userId);
                NotifyStateChanged();
                _logger.LogInformation("Cleaned {Count} job descriptions", cleanedCount);
            }

            return cleanedCount;
        }
    }

    public IEnumerable<JobListing> FilterJobListings(JobListingFilter filter)
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            var query = _jobListings.Where(j => j.UserId == userId).AsEnumerable();

            // Title-only search (takes precedence if set)
            if (!string.IsNullOrWhiteSpace(filter.TitleOnlySearchTerm))
            {
                var titleTerm = filter.TitleOnlySearchTerm.ToLowerInvariant();
                query = query.Where(j =>
                    j.Title.Contains(titleTerm, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var searchTerm = filter.SearchTerm.ToLowerInvariant();
                query = query.Where(j =>
                    j.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    j.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    j.Company.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filter.Location))
            {
                query = query.Where(j => j.Location.Contains(filter.Location, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.JobType.HasValue)
            {
                query = query.Where(j => j.JobType == filter.JobType.Value);
            }

            if (filter.IsRemote.HasValue)
            {
                query = query.Where(j => j.IsRemote == filter.IsRemote.Value);
            }

            if (filter.DateFrom.HasValue)
            {
                query = query.Where(j => j.DatePosted >= filter.DateFrom.Value);
            }

            if (filter.DateTo.HasValue)
            {
                query = query.Where(j => j.DatePosted <= filter.DateTo.Value);
            }

            // Interest filter
            if (filter.Interest.HasValue)
            {
                query = query.Where(j => j.Interest == filter.Interest.Value);
            }

            // Salary filter - check if job has salary info
            if (filter.HasSalary.HasValue)
            {
                if (filter.HasSalary.Value)
                query = query.Where(j => !string.IsNullOrWhiteSpace(j.Salary));
                else
                query = query.Where(j => string.IsNullOrWhiteSpace(j.Salary));
            }

            // Salary text search
            if (!string.IsNullOrWhiteSpace(filter.SalarySearch))
            query = query.Where(j => j.Salary.Contains(filter.SalarySearch, StringComparison.OrdinalIgnoreCase));

            // Salary target range filter
            if (filter.SalaryTarget.HasValue)
            {
                var target = filter.SalaryTarget.Value;
                query = query.Where(j =>
                    (j.SalaryMin.HasValue || j.SalaryMax.HasValue) &&
                    (!j.SalaryMin.HasValue || j.SalaryMin.Value <= target) &&
                    (!j.SalaryMax.HasValue || j.SalaryMax.Value >= target));
            }

            // Applied status filter
            if (filter.HasApplied.HasValue)
            {
                query = query.Where(j => j.HasApplied == filter.HasApplied.Value);
            }

            // Application stage filter
            if (filter.ApplicationStage.HasValue)
            {
                query = query.Where(j => j.ApplicationStage == filter.ApplicationStage.Value);
            }

            // Suitability filter
            if (filter.Suitability.HasValue)
            {
                query = query.Where(j => j.Suitability == filter.Suitability.Value);
            }

            // Source filter
            if (!string.IsNullOrWhiteSpace(filter.Source))
            query = query.Where(j => string.Equals(j.Source, filter.Source, StringComparison.OrdinalIgnoreCase));

            // Skill filter
            if (!string.IsNullOrWhiteSpace(filter.SkillSearch))
            {
                var skillTerm = filter.SkillSearch.Trim();
                query = query.Where(j => j.Skills != null && j.Skills.Any(s => s.Contains(skillTerm, StringComparison.OrdinalIgnoreCase)));
            }

            // Sort order:
            // 1. Prioritized job first (if specified)
            // 2. Applied jobs next
            // 3. Unsuitable jobs last
            // 4. Then by the selected sort option
            var sorted = query
                .OrderByDescending(j => filter.PrioritizeJobId.HasValue && j.Id == filter.PrioritizeJobId.Value)
                .ThenByDescending(j => j.HasApplied)
                .ThenBy(j => j.Suitability == SuitabilityStatus.Unsuitable);
            
            var result = filter.SortBy switch
            {
                SortOption.DatePostedDesc => sorted.ThenByDescending(j => j.DatePosted),
                SortOption.DatePostedAsc => sorted.ThenBy(j => j.DatePosted),
                SortOption.DateAddedDesc => sorted.ThenByDescending(j => j.DateAdded),
                SortOption.DateAddedAsc => sorted.ThenBy(j => j.DateAdded),
                SortOption.TitleAsc => sorted.ThenBy(j => j.Title),
                SortOption.TitleDesc => sorted.ThenByDescending(j => j.Title),
                SortOption.CompanyAsc => sorted.ThenBy(j => j.Company),
                SortOption.CompanyDesc => sorted.ThenByDescending(j => j.Company),
                SortOption.SalaryDesc => sorted.ThenByDescending(j => j.SalaryMax ?? 0),
                SortOption.SalaryAsc => sorted.ThenBy(j => j.SalaryMin ?? decimal.MaxValue),
                _ => sorted.ThenByDescending(j => j.DateAdded)
            };

            return result.ToList();
        }
    }

    public IEnumerable<string> GetDistinctLocations()
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _jobListings.Where(j => j.UserId == userId).Select(j => j.Location).Distinct()
                .Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l).ToList();
        }
    }

    public IEnumerable<string> GetDistinctCompanies()
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _jobListings.Where(j => j.UserId == userId).Select(j => j.Company).Distinct()
                .Where(c => !string.IsNullOrWhiteSpace(c)).OrderBy(c => c).ToList();
        }
    }

    public IEnumerable<string> GetDistinctSources()
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _jobListings.Where(j => j.UserId == userId).Select(j => j.Source).Distinct()
                .Where(s => !string.IsNullOrWhiteSpace(s)).OrderBy(s => s).ToList();
        }
    }

    public IEnumerable<string> GetDistinctSkills()
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _jobListings.Where(j => j.UserId == userId)
                .SelectMany(j => j.Skills ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
        }
    }

    /// <summary>
    /// Infers the source site from a job URL
    /// </summary>
    public static string InferSourceFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var urlLower = url.ToLowerInvariant();

        if (urlLower.Contains("linkedin.com")) return "LinkedIn";
        if (urlLower.Contains("indeed.com")) return "Indeed";
        if (urlLower.Contains("s1jobs.com")) return "S1Jobs";
        if (urlLower.Contains("welcometothejungle.com")) return "WTTJ";
        if (urlLower.Contains("energyjobsearch.com")) return "EnergyJobSearch";

        return string.Empty;
    }

    /// <summary>
    /// Fixes jobs with unknown/empty sources by inferring from their URLs
    /// </summary>
    /// <returns>Number of jobs fixed</returns>
    public int FixUnknownSources(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            int fixedCount = 0;

            foreach (var job in _jobListings.Where(j => j.UserId == userId))
            {
                if (string.IsNullOrWhiteSpace(job.Source))
                {
                    var inferredSource = InferSourceFromUrl(job.Url);
                    if (!string.IsNullOrWhiteSpace(inferredSource))
                    {
                        job.Source = inferredSource;
                        fixedCount++;
                        _logger.LogInformation("Fixed source for job {Title}: {Source}", job.Title, inferredSource);
                    }
                }
            }

            if (fixedCount > 0)
            {
                _storage.SaveJobs(_jobListings.Where(j => j.UserId == userId).ToList(), userId);
                NotifyStateChanged();
                _logger.LogInformation("Fixed source for {Count} jobs", fixedCount);
            }

            return fixedCount;
        }
    }

    public int GetTotalCount(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            return _jobListings.Count(j => j.UserId == userId);
        }
    }

    /// <summary>
    /// Gets statistics about the job listings
    /// </summary>
    public JobStats GetStats(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            var userJobs = _jobListings.Where(j => j.UserId == userId).ToList();

            // Jobs needing descriptions excludes unsuitable jobs (no point fetching for those)
            var jobsNeedingDescription = userJobs
                .Where(j => string.IsNullOrWhiteSpace(j.Description) || j.Description.Length < 100)
                .Where(j => j.Suitability != SuitabilityStatus.Unsuitable)
                .ToList();

            var needingBySource = jobsNeedingDescription
                .GroupBy(j => string.IsNullOrWhiteSpace(j.Source) ? "Unknown" : j.Source)
                .ToDictionary(g => g.Key, g => g.Count());

            return new JobStats
            {
                TotalJobs = userJobs.Count,
                InterestedCount = userJobs.Count(j => j.Interest == InterestStatus.Interested),
                NotInterestedCount = userJobs.Count(j => j.Interest == InterestStatus.NotInterested),
                NotRatedCount = userJobs.Count(j => j.Interest == InterestStatus.NotRated),
                UnsuitableCount = userJobs.Count(j => j.Suitability == SuitabilityStatus.Unsuitable),
                WithSalaryCount = userJobs.Count(j => !string.IsNullOrWhiteSpace(j.Salary)),
                RemoteCount = userJobs.Count(j => j.IsRemote),
                AppliedCount = userJobs.Count(j => j.HasApplied),
                NeedingDescriptionCount = jobsNeedingDescription.Count,
                NeedingDescriptionBySource = needingBySource
            };
        }
    }

    /// <summary>
    /// Gets jobs that need descriptions (have no description or very short one)
    /// Excludes jobs marked as Unsuitable (no point fetching descriptions for those)
    /// Prioritizes: 1) Newest jobs first, 2) Interested jobs, 3) Possible jobs
    /// </summary>
    public IReadOnlyList<JobListing> GetJobsNeedingDescriptions(int limit = int.MaxValue, Guid? forUserId = null)
    {
        EnsureJobsLoaded(forUserId);
        var userId = forUserId ?? CurrentUserId;
        
        _logger.LogInformation("GetJobsNeedingDescriptions called - User: {UserId}, ForUserId: {ForUserId}, CurrentUserId: {CurrentUserId}",
            userId, forUserId, CurrentUserId);
        
        lock (_lock)
        {
            var allUserJobs = _jobListings.Where(j => j.UserId == userId).ToList();
            _logger.LogInformation("Total jobs for user {UserId}: {Count}", userId, allUserJobs.Count);
            
            var jobsNeedingDesc = allUserJobs
                .Where(j => string.IsNullOrWhiteSpace(j.Description) || j.Description.Length < 100)
                .ToList();
            _logger.LogInformation("Jobs with missing/short descriptions: {Count}", jobsNeedingDesc.Count);
            
            var unsuitable = jobsNeedingDesc.Where(j => j.Suitability == SuitabilityStatus.Unsuitable).Count();
            _logger.LogInformation("Unsuitable jobs (will be excluded): {Count}", unsuitable);
            
            var result = _jobListings
                .Where(j => j.UserId == userId)
                .Where(j => string.IsNullOrWhiteSpace(j.Description) || j.Description.Length < 100)
                .Where(j => j.Suitability != SuitabilityStatus.Unsuitable) // Skip unsuitable jobs
                .OrderByDescending(j => j.DateAdded) // Newest jobs first
                .ThenByDescending(j => j.Interest == InterestStatus.Interested) // Then interested jobs
                .ThenByDescending(j => j.Suitability == SuitabilityStatus.Possible) // Then possible jobs
                .Take(limit)
                .ToList()
                .AsReadOnly();
            
            _logger.LogInformation("Returning {Count} jobs needing descriptions for user {UserId}", result.Count, userId);
            
            return result;
        }
    }

    /// <summary>
    /// Normalizes a URL for comparison.
    /// LinkedIn: job ID is in the path (e.g., /jobs/view/4331453808/)
    /// Indeed: job ID is in the query string (e.g., /viewjob?jk=abc123)
    /// S1Jobs: job ID is in the path (e.g., /job/12345678)
    /// WTTJ: job ID is in the path (e.g., /jobs/job-slug_12345)
    /// </summary>
    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        // For Indeed URLs, preserve the jk parameter as itcontains the job ID
        if (url.Contains("indeed.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(url, @"[?&]jk=([a-f0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return $"indeed.com/viewjob?jk={match.Groups[1].Value.ToLowerInvariant()}";
            }
        }

        // For WTTJ URLs, extract the job slug from the path
        if (url.Contains("welcometothejungle.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(url, @"/jobs?/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return $"welcometothejungle.com/jobs/{match.Groups[1].Value.ToLowerInvariant()}";
            }
        }

        // For LinkedIn and other URLs, remove query string and fragment
        var normalized = url.Split('?')[0].Split('#')[0];

        // Remove trailing slash
        normalized = normalized.TrimEnd('/');

        // Convert to lowercase
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Cleans job data - removes duplicated text, extra whitespace, etc.
    /// </summary>
    private static void CleanJobData(JobListing job)
    {
        job.Title = CleanText(job.Title);
        job.Company = CleanText(job.Company);
        job.Location = CleanText(job.Location);
    }

    /// <summary>
    /// Cleans text - removes duplicated text, "with verification" suffix, extra whitespace
    /// </summary>
    private static string CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Trim and normalize whitespace
        text = text.Trim();
        text = Regex.Replace(text, @"\s+", " ");

        // Remove "with verification" suffix
        text = Regex.Replace(text, @"\s*with verification\s*$", "", RegexOptions.IgnoreCase);

        // Check if text is exactly duplicated (e.g., "Senior DeveloperSenior Developer")
        var len = text.Length;
        if (len >= 6 && len % 2 == 0)
        {
            var half = len / 2;
            var firstHalf = text[..half];
            var secondHalf = text[half..];
            if (string.Equals(firstHalf, secondHalf, StringComparison.OrdinalIgnoreCase))
            {
                text = firstHalf.Trim();
            }
        }

        // Check if text is duplicated with a space (e.g., "Senior Developer Senior Developer")
        var words = text.Split(' ');
        if (words.Length >= 2 && words.Length % 2 == 0)
        {
            var half = words.Length / 2;
            var firstHalf = string.Join(" ", words.Take(half));
            var secondHalf = string.Join(" ", words.Skip(half));
            if (string.Equals(firstHalf, secondHalf, StringComparison.OrdinalIgnoreCase))
            {
                text = firstHalf.Trim();
            }
        }

        // Handle pattern: "Title \n    \n    \n\nTitle with verification"
        // Split on multiple whitespace/newlines and check for repeats
        var parts = Regex.Split(text, @"\s{2,}|\n+").Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (parts.Length >= 2)
        {
            var first = parts[0].Trim();
            var last = Regex.Replace(parts[^1].Trim(), @"\s*with verification\s*$", "", RegexOptions.IgnoreCase);
            
            if (string.Equals(first, last, StringComparison.OrdinalIgnoreCase) ||
                first.StartsWith(last, StringComparison.OrdinalIgnoreCase) ||
                last.StartsWith(first, StringComparison.OrdinalIgnoreCase))
            {
                text = first.Length >= last.Length ? first : last;
            }
        }

        return text.Trim();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    /// <summary>
    /// Applies all enabled rules to existing unclassified jobs.
    /// Only affects jobs with NotRated interest and NotChecked suitability.
    /// </summary>
    /// <returns>Number of jobs updated</returns>
    public int ApplyRulesToExistingJobs()
    {
        EnsureJobsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            int updatedCount = 0;

            foreach (var job in _jobListings.Where(j => j.UserId == userId))
            {
                // Skip jobs that are fully classified (interest, suitability, and remote all set)
                var hasUnsetInterest = job.Interest == InterestStatus.NotRated;
                var hasUnsetSuitability = job.Suitability == SuitabilityStatus.NotChecked;
                var couldChangeRemote = !job.IsRemote; // Remote flag defaults to false; rules could set it
                if (!hasUnsetInterest && !hasUnsetSuitability && !couldChangeRemote)
                {
                    continue;
                }

                var ruleResult = _rulesService.Value.EvaluateJob(job);
                bool modified = false;

                if (ruleResult.Interest.HasValue && job.Interest == InterestStatus.NotRated)
                {
                    job.Interest = ruleResult.Interest.Value;
                    modified = true;
                }
                if (ruleResult.Suitability.HasValue && job.Suitability == SuitabilityStatus.NotChecked)
                {
                    job.Suitability = ruleResult.Suitability.Value;
                    modified = true;
                }
                if (ruleResult.IsRemote.HasValue && job.IsRemote != ruleResult.IsRemote.Value)
                {
                    job.IsRemote = ruleResult.IsRemote.Value;
                    modified = true;
                }

                if (modified)
                {
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                _storage.SaveJobs(_jobListings.Where(j => j.UserId == userId).ToList(), userId);
                NotifyStateChanged();
                _logger.LogInformation("Applied rules to {Count} existing jobs", updatedCount);
            }

            return updatedCount;
        }
    }

    /// <summary>
    /// Applies rules to a single job by ID. Used after fetching/updating a job's data.
    /// Unlike the bulk method, this allows rules to override previously rule-set values
    /// since the job's data (description, company) may have changed.
    /// </summary>
    public bool ApplyRulesToExistingJobs(Guid jobId)
    {
        EnsureJobsLoaded();
        lock (_lock)
        {
            var job = _jobListings.FirstOrDefault(j => j.Id == jobId);
            if (job == null) return false;

            var oldInterest = job.Interest;
            var oldSuitability = job.Suitability;
            var oldIsRemote = job.IsRemote;

            var ruleResult = _rulesService.Value.EvaluateJob(job);
            bool modified = false;

            if (ruleResult.Interest.HasValue && ruleResult.Interest.Value != job.Interest)
            {
                job.Interest = ruleResult.Interest.Value;
                modified = true;
            }
            if (ruleResult.Suitability.HasValue && ruleResult.Suitability.Value != job.Suitability)
            {
                job.Suitability = ruleResult.Suitability.Value;
                modified = true;
            }
            if (ruleResult.IsRemote.HasValue && ruleResult.IsRemote.Value != job.IsRemote)
            {
                job.IsRemote = ruleResult.IsRemote.Value;
                modified = true;
            }

            if (modified)
            {
                _storage.SaveJob(job);
                NotifyStateChanged();
                _logger.LogInformation("Applied rules to job: {Title}", job.Title);

                if (job.Interest != oldInterest)
                    _historyService.Value.RecordInterestChanged(job, oldInterest, job.Interest, HistoryChangeSource.Rule, ruleResult.InterestRuleName);
                if (job.Suitability != oldSuitability)
                    _historyService.Value.RecordSuitabilityChanged(job, oldSuitability, job.Suitability, HistoryChangeSource.Rule, ruleResult.SuitabilityRuleName);
                if (job.IsRemote != oldIsRemote)
                    _historyService.Value.RecordRemoteStatusChanged(job, oldIsRemote, job.IsRemote, HistoryChangeSource.Rule, ruleResult.IsRemoteRuleName);
            }

            return modified;
        }
    }

    /// <summary>
    /// Removes duplicate jobs from the job list (by URL or Title+Company), keeping only the first occurrence.
    /// Returns the number of jobs removed.
    /// </summary>
    public int RemoveDuplicateJobs(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        EnsureJobsLoaded(userId);
        lock (_lock)
        {
            var seenUrls = new HashSet<string>();
            var seenTitleCompany = new HashSet<string>();
            var uniqueJobs = new List<JobListing>();
            int removed = 0;

            foreach (var job in _jobListings.Where(j => j.UserId == userId))
            {
                string urlKey = NormalizeUrl(job.Url);
                string titleCompanyKey = (job.Title?.Trim().ToLowerInvariant() ?? "") + "|" + (job.Company?.Trim().ToLowerInvariant() ?? "");

                if (!string.IsNullOrWhiteSpace(urlKey))
                {
                    if (seenUrls.Contains(urlKey))
                    {
                        removed++;
                        continue;
                    }
                    seenUrls.Add(urlKey);
                }
                else if (!string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(job.Company))
                {
                    if (seenTitleCompany.Contains(titleCompanyKey))
                    {
                        removed++;
                        continue;
                    }
                    seenTitleCompany.Add(titleCompanyKey);
                }
                uniqueJobs.Add(job);
            }

            if (removed > 0)
            {
                // Remove user's jobs and add back unique ones
                _jobListings.RemoveAll(j => j.UserId == userId);
                _jobListings.AddRange(uniqueJobs);
                _storage.SaveJobs(uniqueJobs, userId);
                NotifyStateChanged();
            }
            return removed;
        }
    }
}

public class JobListingFilter
{
    public string? SearchTerm { get; set; }
    public string? TitleOnlySearchTerm { get; set; } // NEW: Only search in Title
    public string? Location { get; set; }
    public JobType? JobType { get; set; }
    public bool? IsRemote { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public SortOption SortBy { get; set; } = SortOption.DateAddedDesc;
    public InterestStatus? Interest { get; set; }
    public bool? HasSalary { get; set; }
    public string? SalarySearch { get; set; }
    public decimal? SalaryTarget { get; set; }
    public bool? HasApplied { get; set; }
    public ApplicationStage? ApplicationStage { get; set; }
    public SuitabilityStatus? Suitability { get; set; }
    public Guid? PrioritizeJobId { get; set; }
    public string? Source { get; set; }
    public string? SkillSearch { get; set; }
}

public enum SortOption
{
    DatePostedDesc,
    DatePostedAsc,
    DateAddedDesc,
    DateAddedAsc,
    TitleAsc,
    TitleDesc,
    CompanyAsc,
    CompanyDesc,
    SalaryDesc,
    SalaryAsc
}

public class JobStats
{
    public int TotalJobs { get; set; }
    public int InterestedCount { get; set; }
    public int NotInterestedCount { get; set; }
    public int NotRatedCount { get; set; }
    public int UnsuitableCount { get; set; }
    public int WithSalaryCount { get; set; }
    public int RemoteCount { get; set; }
    public int AppliedCount { get; set; }
    public int NeedingDescriptionCount { get; set; }
    public Dictionary<string, int> NeedingDescriptionBySource { get; set; } = new();
}
