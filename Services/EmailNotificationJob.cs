using JobTracker.Models;

namespace JobTracker.Services;

public class EmailNotificationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailNotificationJob> _logger;

    public EmailNotificationJob(IServiceScopeFactory scopeFactory, ILogger<EmailNotificationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("[Background] Starting email notification check");

        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

        var users = authService.GetAllUsers();

        foreach (var user in users)
        {
            var settings = settingsService.GetSettings(user.Id);
            if (!settings.EmailNotificationsEnabled) continue;
            if (string.IsNullOrWhiteSpace(settings.SmtpHost)) continue;

            var allJobs = jobService.GetAllJobListings(user.Id);
            var appliedJobs = allJobs.Where(j => j.HasApplied && !j.IsArchived).ToList();
            var now = DateTime.Now;
            var sections = new List<string>();

            // Follow-ups due today
            if (settings.EmailOnFollowUpDue)
            {
                var followUps = appliedJobs
                    .Where(j => j.FollowUpDate.HasValue && j.FollowUpDate.Value.Date <= now.Date)
                    .ToList();

                if (followUps.Any())
                {
                    var items = string.Join("", followUps.Select(j =>
                        $"<li><strong>{Escape(j.Title)}</strong> at {Escape(j.Company)} — due {j.FollowUpDate!.Value:MMM dd}</li>"));
                    sections.Add($"<h3>Follow-ups Due ({followUps.Count})</h3><ul>{items}</ul>");
                }
            }

            // Stale applications
            if (settings.EmailOnStaleApplications)
            {
                var staleDays = settings.Pipeline.StaleDays;
                var staleCutoff = now.AddDays(-staleDays);
                var activeStages = new[] { ApplicationStage.Applied, ApplicationStage.Pending, ApplicationStage.TechTest, ApplicationStage.Interview };

                var staleJobs = appliedJobs
                    .Where(j => activeStages.Contains(j.ApplicationStage))
                    .Where(j =>
                    {
                        var lastChange = j.StageHistory?.Any() == true
                            ? j.StageHistory.Max(s => s.DateChanged)
                            : j.DateApplied ?? j.DateAdded;
                        return lastChange < staleCutoff;
                    })
                    .ToList();

                if (staleJobs.Any())
                {
                    var items = string.Join("", staleJobs.Select(j =>
                        $"<li><strong>{Escape(j.Title)}</strong> at {Escape(j.Company)} — {j.ApplicationStage}</li>"));
                    sections.Add($"<h3>Stale Applications ({staleJobs.Count})</h3><ul>{items}</ul>");
                }
            }

            if (!sections.Any()) continue;

            var body = $@"
<html>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333;"">
    <div style=""max-width: 600px; margin: 0 auto; padding: 20px;"">
        <h2 style=""color: #0066cc;"">Job Tracker Daily Digest</h2>
        {string.Join("<hr />", sections)}
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"" />
        <p style=""color: #999; font-size: 12px;"">
            This is an automated digest from Job Tracker. Manage notifications in Settings.
        </p>
    </div>
</body>
</html>";

            var sent = await emailService.SendEmailAsync(user.Email, "Job Tracker — Daily Digest", body,
                settings.SmtpHost, settings.SmtpPort, settings.SmtpUsername, settings.SmtpPassword,
                settings.SmtpFromEmail, settings.SmtpFromName);
            if (sent)
                _logger.LogInformation("[Background] Sent email digest to {Email}", user.Email);
            else
                _logger.LogWarning("[Background] Failed to send email digest to {Email}", user.Email);
        }

        _logger.LogInformation("[Background] Email notification check complete");
    }

    private static string Escape(string text) =>
        System.Net.WebUtility.HtmlEncode(text ?? "");
}
