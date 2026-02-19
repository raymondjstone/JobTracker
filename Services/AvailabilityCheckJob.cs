using JobTracker.Models;

namespace JobTracker.Services;

public class AvailabilityCheckJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvailabilityCheckJob> _logger;

    public AvailabilityCheckJob(IServiceScopeFactory scopeFactory, ILogger<AvailabilityCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(SuitabilityStatus targetStatus)
    {
        _logger.LogInformation("[Hangfire] Starting availability check for {Status} jobs", targetStatus);

        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var availabilityService = scope.ServiceProvider.GetRequiredService<JobAvailabilityService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();

        var users = authService.GetAllUsers();
        var cutoff = DateTime.Now.AddHours(-4);

        foreach (var user in users)
        {
            var allJobs = jobService.GetAllJobListings(user.Id);
            var jobsToCheck = allJobs
                .Where(j => !j.HasApplied && j.Suitability == targetStatus)
                .Where(j => !string.IsNullOrWhiteSpace(j.Url))
                .Where(j => !j.LastChecked.HasValue || j.LastChecked.Value < cutoff)
                .OrderBy(j => j.LastChecked.HasValue ? 1 : 0)
                .ThenBy(j => j.LastChecked ?? DateTime.MinValue)
                .ToList();

            if (jobsToCheck.Count == 0)
            {
                _logger.LogInformation("[Hangfire] No {Status} jobs to check for user {User}", targetStatus, user.Email);
                continue;
            }

            _logger.LogInformation("[Hangfire] Checking {Count} {Status} jobs for user {User}",
                jobsToCheck.Count, targetStatus, user.Email);

            int markedUnavailable = 0;
            int errors = 0;
            int skipped = 0;

            await availabilityService.ScanJobsAsync(
                jobsToCheck,
                markUnsuitableAction: (jobId, reason) =>
                {
                    jobService.SetSuitabilityStatus(jobId, SuitabilityStatus.Unsuitable, HistoryChangeSource.System, reason);
                    Interlocked.Increment(ref markedUnavailable);
                },
                markCheckedAction: (jobId) =>
                {
                    jobService.SetLastChecked(jobId);
                },
                updateJobAction: (jobId, parsed) =>
                {
                    jobService.UpdateJobIfBetter(jobId, parsed);
                },
                onProgress: (progress) =>
                {
                    errors = progress.ErrorCount;
                    skipped = progress.SkippedCount;
                }
            );

            _logger.LogInformation(
                "[Hangfire] Availability check complete for user {User}: {Total} jobs, {Unavailable} unavailable, {Errors} errors, {Skipped} skipped",
                user.Email, jobsToCheck.Count, markedUnavailable, errors, skipped);
        }
    }
}
