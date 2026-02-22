using JobTracker.Models;

namespace JobTracker.Services;

public class AutoArchiveJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoArchiveJob> _logger;

    public AutoArchiveJob(IServiceScopeFactory scopeFactory, ILogger<AutoArchiveJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("[Background] Starting auto-archive check");

        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();

        var users = authService.GetAllUsers();
        int totalArchived = 0;

        foreach (var user in users)
        {
            var settings = settingsService.GetSettings(user.Id);
            if (!settings.AutoArchiveEnabled) continue;

            var cutoff = DateTime.Now.AddDays(-settings.AutoArchiveDays);
            var allJobs = jobService.GetAllJobListings(user.Id);

            var jobsToArchive = allJobs
                .Where(j => !j.IsArchived)
                .Where(j => j.ApplicationStage == ApplicationStage.Rejected || j.ApplicationStage == ApplicationStage.Ghosted)
                .Where(j =>
                {
                    var lastChange = j.StageHistory?.Any() == true
                        ? j.StageHistory.Max(s => s.DateChanged)
                        : j.DateApplied ?? j.DateAdded;
                    return lastChange < cutoff;
                })
                .ToList();

            foreach (var job in jobsToArchive)
            {
                jobService.SetArchived(job.Id, true);
                totalArchived++;
                _logger.LogInformation("[Background] Auto-archived job: {Title} at {Company}", job.Title, job.Company);
            }

            if (jobsToArchive.Count > 0)
            {
                _logger.LogInformation("[Background] Auto-archived {Count} jobs for user {User}",
                    jobsToArchive.Count, user.Email);
            }
        }

        _logger.LogInformation("[Background] Auto-archive check complete: {Total} jobs archived", totalArchived);
    }
}
