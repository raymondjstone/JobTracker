using JobTracker.Models;

namespace JobTracker.Services;

public class JsaActivityModeJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JsaActivityModeJob> _logger;

    /// <summary>Maximum duration the job will run before stopping.</summary>
    private static readonly TimeSpan MaxRunDuration = TimeSpan.FromHours(4);

    public JsaActivityModeJob(IServiceScopeFactory scopeFactory, ILogger<JsaActivityModeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Only run on weekdays between 08:00 and 18:00
        var now = DateTime.Now;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            || now.Hour < 8 || now.Hour >= 18)
        {
            _logger.LogInformation("[JsaActivityMode] Outside active hours ({DayOfWeek} {Time:HH:mm}) — skipping",
                now.DayOfWeek, now);
            return;
        }

        _logger.LogInformation("[JsaActivityMode] Starting — will run for up to {Hours} hours", MaxRunDuration.TotalHours);

        var deadline = DateTime.Now.Add(MaxRunDuration);
        var random = new Random();
        int totalMarked = 0;

        while (DateTime.Now < deadline && !ct.IsCancellationRequested)
        {
            // Wait a random period between 30 secs and 2.5 minutes
            var delayMinutes = 0.5 + (random.NextDouble() * 2);
            var delay = TimeSpan.FromMinutes(delayMinutes);
            _logger.LogDebug("[JsaActivityMode] Waiting {Delay:F1} minutes before next action", delay.TotalMinutes);

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (ct.IsCancellationRequested || DateTime.Now >= deadline)
                break;

            using var scope = _scopeFactory.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
            var authService = scope.ServiceProvider.GetRequiredService<AuthService>();

            var users = authService.GetAllUsers();

            foreach (var user in users)
            {
                var allJobs = jobService.GetAllJobListings(user.Id);

                // "Browse" tab = not applied, not possible, not unsuitable, not archived
                var browseJobs = allJobs
                    .Where(j => !j.HasApplied
                                && j.Suitability == SuitabilityStatus.NotChecked
                                && !j.IsArchived)
                    .ToList();

                if (browseJobs.Count == 0)
                {
                    _logger.LogInformation("[JsaActivityMode] No browse jobs left for user {User}, skipping", user.Email);
                    continue;
                }

                var pick = browseJobs[random.Next(browseJobs.Count)];
                jobService.SetSuitabilityStatus(pick.Id, SuitabilityStatus.Unsuitable, HistoryChangeSource.Manual,
                    forUserId: user.Id);
                totalMarked++;

                _logger.LogInformation("[JsaActivityMode] Marked \"{Title}\" at {Company} as unsuitable ({Remaining} browse jobs remaining)",
                    pick.Title, pick.Company, browseJobs.Count - 1);
            }
        }

        _logger.LogInformation("[JsaActivityMode] Finished — marked {Total} jobs as unsuitable over the session", totalMarked);
    }
}
