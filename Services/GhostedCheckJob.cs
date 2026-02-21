using JobTracker.Models;

namespace JobTracker.Services;

public class GhostedCheckJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GhostedCheckJob> _logger;

    public GhostedCheckJob(IServiceScopeFactory scopeFactory, ILogger<GhostedCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("[Hangfire] Starting ghosted check for applied jobs");

        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();

        var users = authService.GetAllUsers();
        int totalGhosted = 0;

        foreach (var user in users)
        {
            var settings = settingsService.GetSettings(user.Id);
            var ghostedDays = settings.Pipeline.GhostedDays;
            var cutoff = DateTime.Now.AddDays(-ghostedDays);

            var allJobs = jobService.GetAllJobListings(user.Id);
            var appliedJobs = allJobs
                .Where(j => j.HasApplied && j.ApplicationStage == ApplicationStage.NoReply)
                .Where(j => j.DateApplied.HasValue && j.DateApplied.Value < cutoff)
                .ToList();

            foreach (var job in appliedJobs)
            {
                jobService.SetApplicationStage(job.Id, ApplicationStage.Ghosted, HistoryChangeSource.System);
                totalGhosted++;
                _logger.LogInformation("[Hangfire] Marked job as ghosted: {Title} (applied {Date}, threshold {Days} days)",
                    job.Title, job.DateApplied?.ToString("yyyy-MM-dd"), ghostedDays);
            }

            if (appliedJobs.Count > 0)
            {
                _logger.LogInformation("[Hangfire] Marked {Count} jobs as ghosted for user {User}",
                    appliedJobs.Count, user.Email);
            }
        }

        _logger.LogInformation("[Hangfire] Ghosted check complete: {Total} jobs marked as ghosted", totalGhosted);
    }
}
