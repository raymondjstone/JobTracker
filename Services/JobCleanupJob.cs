using JobTracker.Models;

namespace JobTracker.Services;

public class JobCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobCleanupJob> _logger;

    public JobCleanupJob(IServiceScopeFactory scopeFactory, ILogger<JobCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("[JobCleanup] Starting automatic job cleanup");

        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();

        var users = authService.GetAllUsers();
        int totalDeleted = 0;

        foreach (var user in users)
        {
            var settings = settingsService.GetSettings(user.Id);
            var allJobs = jobService.GetAllJobListings(user.Id);
            var jobsToDelete = new List<JobListing>();

            // Delete unsuitable jobs
            if (settings.DeleteUnsuitableAfterDays > 0)
            {
                var unsuitableCutoff = DateTime.Now.AddDays(-settings.DeleteUnsuitableAfterDays);
                var unsuitableJobs = allJobs
                    .Where(j => j.Suitability == SuitabilityStatus.Unsuitable)
                    .Where(j => j.DateAdded < unsuitableCutoff)
                    .ToList();

                jobsToDelete.AddRange(unsuitableJobs);
                _logger.LogInformation("[JobCleanup] Found {Count} unsuitable jobs to delete for user {User} (older than {Days} days)",
                    unsuitableJobs.Count, user.Email, settings.DeleteUnsuitableAfterDays);
            }

            // Delete rejected jobs
            if (settings.DeleteRejectedAfterDays > 0)
            {
                var rejectedCutoff = DateTime.Now.AddDays(-settings.DeleteRejectedAfterDays);
                var rejectedJobs = allJobs
                    .Where(j => j.HasApplied && j.ApplicationStage == ApplicationStage.Rejected)
                    .Where(j => j.DateApplied.HasValue && j.DateApplied.Value < rejectedCutoff)
                    .ToList();

                jobsToDelete.AddRange(rejectedJobs);
                _logger.LogInformation("[JobCleanup] Found {Count} rejected jobs to delete for user {User} (older than {Days} days)",
                    rejectedJobs.Count, user.Email, settings.DeleteRejectedAfterDays);
            }

            // Delete ghosted jobs
            if (settings.DeleteGhostedAfterDays > 0)
            {
                var ghostedCutoff = DateTime.Now.AddDays(-settings.DeleteGhostedAfterDays);
                var ghostedJobs = allJobs
                    .Where(j => j.HasApplied && j.ApplicationStage == ApplicationStage.Ghosted)
                    .Where(j => j.DateApplied.HasValue && j.DateApplied.Value < ghostedCutoff)
                    .ToList();

                jobsToDelete.AddRange(ghostedJobs);
                _logger.LogInformation("[JobCleanup] Found {Count} ghosted jobs to delete for user {User} (older than {Days} days)",
                    ghostedJobs.Count, user.Email, settings.DeleteGhostedAfterDays);
            }

            // Remove duplicates (in case a job matches multiple criteria)
            jobsToDelete = jobsToDelete.DistinctBy(j => j.Id).ToList();

            // Delete the jobs
            foreach (var job in jobsToDelete)
            {
                jobService.DeleteJobListing(job.Id);
                totalDeleted++;
                _logger.LogDebug("[JobCleanup] Deleted job: {Title} at {Company} (Status: {Status}, Added: {Date})",
                    job.Title, job.Company, 
                    job.Suitability == SuitabilityStatus.Unsuitable ? "Unsuitable" : job.ApplicationStage.ToString(),
                    job.DateAdded.ToString("yyyy-MM-dd"));
            }

            if (jobsToDelete.Count > 0)
            {
                _logger.LogInformation("[JobCleanup] Deleted {Count} jobs for user {User}",
                    jobsToDelete.Count, user.Email);
            }
        }

        _logger.LogInformation("[JobCleanup] Job cleanup complete: {Total} jobs deleted across all users", totalDeleted);
    }
}
