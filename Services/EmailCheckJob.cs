using JobTracker.Models;

namespace JobTracker.Services;

public class EmailCheckJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailCheckJob> _logger;

    public EmailCheckJob(IServiceScopeFactory scopeFactory, ILogger<EmailCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("[Background] Starting email check (IMAP)");

        using var scope = _scopeFactory.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var historyService = scope.ServiceProvider.GetRequiredService<JobHistoryService>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBackend>();
        var inboxService = scope.ServiceProvider.GetRequiredService<EmailInboxService>();
        var processingService = scope.ServiceProvider.GetRequiredService<EmailProcessingService>();

        var users = authService.GetAllUsers();

        foreach (var user in users)
        {
            try
            {
                var settings = settingsService.GetSettings(user.Id);

                if (!settings.EmailCheckEnabled || string.IsNullOrWhiteSpace(settings.ImapHost))
                    continue;

                _logger.LogInformation("[EmailCheck] Processing emails for user {Email}", user.Email);

                // Load processed email IDs
                var processedEmails = storage.LoadProcessedEmails(user.Id);
                var processedIds = new HashSet<string>(processedEmails.Select(e => e.MessageId));

                // Fetch new emails
                List<IncomingEmail> newEmails;
                try
                {
                    newEmails = await inboxService.FetchNewEmailsAsync(settings, processedIds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[EmailCheck] Failed to fetch emails for user {Email}", user.Email);
                    continue;
                }

                if (newEmails.Count == 0)
                {
                    _logger.LogInformation("[EmailCheck] No new emails for user {Email}", user.Email);
                    continue;
                }

                // Load user's jobs and contacts
                var userJobs = jobService.GetAllJobListings(user.Id).ToList();
                var contacts = storage.LoadContacts(user.Id);

                // Process
                var result = processingService.ProcessEmails(newEmails, userJobs, contacts, settings);

                // Apply stage updates
                foreach (var (jobId, newStage, reason) in result.StageUpdateDetails)
                {
                    var job = userJobs.FirstOrDefault(j => j.Id == jobId);
                    if (job == null) continue;

                    var oldStage = job.ApplicationStage;
                    job.ApplicationStage = newStage;
                    job.StageHistory ??= new List<ApplicationStageChange>();
                    job.StageHistory.Add(new ApplicationStageChange
                    {
                        Stage = newStage,
                        DateChanged = DateTime.Now,
                        Notes = $"Auto-updated from email: {reason}"
                    });
                    job.LastUpdated = DateTime.Now;

                    storage.SaveJob(job);

                    // Record history
                    storage.AddHistoryEntry(new JobHistoryEntry
                    {
                        UserId = user.Id,
                        JobId = job.Id,
                        JobTitle = job.Title,
                        Company = job.Company,
                        JobUrl = job.Url,
                        ActionType = HistoryActionType.ApplicationStageChanged,
                        ChangeSource = HistoryChangeSource.Email,
                        FieldName = "ApplicationStage",
                        OldValue = oldStage.ToString(),
                        NewValue = newStage.ToString(),
                        Details = reason
                    });

                    _logger.LogInformation("[EmailCheck] Updated job '{Title}' stage: {Old} -> {New}",
                        job.Title, oldStage, newStage);
                }

                // Add new jobs from alerts
                foreach (var (url, source) in result.NewJobDetails)
                {
                    var newJob = new JobListing
                    {
                        UserId = user.Id,
                        Url = url,
                        Source = source,
                        Title = $"(From {source} alert)",
                        Company = "",
                        Description = "",
                        DateAdded = DateTime.Now,
                        LastChecked = null, // Leave null so availability check fetches details immediately
                        Suitability = SuitabilityStatus.NotChecked
                    };

                    jobService.AddJobListing(newJob, user.Id);

                    storage.AddHistoryEntry(new JobHistoryEntry
                    {
                        UserId = user.Id,
                        JobId = newJob.Id,
                        JobTitle = newJob.Title,
                        Company = newJob.Company,
                        JobUrl = newJob.Url,
                        ActionType = HistoryActionType.JobAdded,
                        ChangeSource = HistoryChangeSource.Email,
                        Details = $"Added from {source} job alert email"
                    });

                    _logger.LogInformation("[EmailCheck] Added job from {Source} alert: {Url}", source, url);
                }

                // Save processed emails
                processedEmails.AddRange(result.ProcessedEmails);
                storage.SaveProcessedEmails(processedEmails, user.Id);

                _logger.LogInformation(
                    "[EmailCheck] User {Email}: {Total} processed, {Updates} stage updates, {Added} jobs added, {Ignored} ignored",
                    user.Email, result.TotalProcessed, result.StageUpdates, result.JobsAdded, result.Ignored);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailCheck] Error processing emails for user {UserId}", user.Id);
            }
        }

        _logger.LogInformation("[Background] Email check complete");
    }
}
