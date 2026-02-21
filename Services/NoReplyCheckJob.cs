using JobTracker.Models;

namespace JobTracker.Services;

public class NoReplyCheckJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoReplyCheckJob> _logger;

    public NoReplyCheckJob(IServiceScopeFactory scopeFactory, ILogger<NoReplyCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("[Hangfire] Starting no reply check for applied jobs");

        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();

        var users = authService.GetAllUsers();
        int totalNoReply = 0;

        foreach (var user in users)
        {
            var settings = settingsService.GetSettings(user.Id);
            var noReplyDays = settings.Pipeline.NoReplyDays;
            var cutoff = DateTime.Now.AddDays(-noReplyDays);

            var allJobs = jobService.GetAllJobListings(user.Id);
            var appliedJobs = allJobs
                .Where(j => j.HasApplied && j.ApplicationStage == ApplicationStage.Applied)
                .Where(j => j.DateApplied.HasValue && j.DateApplied.Value < cutoff)
                .ToList();

            foreach (var job in appliedJobs)
            {
                jobService.SetApplicationStage(job.Id, ApplicationStage.NoReply, HistoryChangeSource.System);
                totalNoReply++;
                _logger.LogInformation("[Hangfire] Marked job as no reply: {Title} (applied {Date}, threshold {Days} days)",
                    job.Title, job.DateApplied?.ToString("yyyy-MM-dd"), noReplyDays);
            }

            if (appliedJobs.Count > 0)
            {
                _logger.LogInformation("[Hangfire] Marked {Count} jobs as no reply for user {User}",
                    appliedJobs.Count, user.Email);
            }
        }

        _logger.LogInformation("[Hangfire] no reply check complete: {Total} jobs marked as no reply", totalNoReply);
    }
}
