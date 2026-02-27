using JobTracker.Models;

namespace JobTracker.Services;

public class EmailProcessingResult
{
    public int TotalProcessed { get; set; }
    public int StageUpdates { get; set; }
    public int JobsAdded { get; set; }
    public int Ignored { get; set; }
    public List<string> Errors { get; set; } = new();

    public List<ProcessedEmail> ProcessedEmails { get; set; } = new();
    public List<(Guid JobId, ApplicationStage NewStage, string Reason)> StageUpdateDetails { get; set; } = new();
    public List<(string Url, string Source)> NewJobDetails { get; set; } = new();
}

public class EmailProcessingService
{
    private readonly EmailReplyMatcher _replyMatcher;
    private readonly EmailJobAlertParser _alertParser;
    private readonly ILogger<EmailProcessingService> _logger;

    public EmailProcessingService(
        EmailReplyMatcher replyMatcher,
        EmailJobAlertParser alertParser,
        ILogger<EmailProcessingService> logger)
    {
        _replyMatcher = replyMatcher;
        _alertParser = alertParser;
        _logger = logger;
    }

    public EmailProcessingResult ProcessEmails(
        List<IncomingEmail> emails,
        List<JobListing> userJobs,
        List<Contact> userContacts,
        AppSettings settings)
    {
        var result = new EmailProcessingResult();

        foreach (var email in emails)
        {
            try
            {
                result.TotalProcessed++;

                // Try reply matching first
                if (settings.EmailCheckAutoUpdateStage)
                {
                    var replyMatch = _replyMatcher.Match(email, userJobs, userContacts);
                    if (replyMatch.Matched)
                    {
                        result.StageUpdates++;
                        result.StageUpdateDetails.Add((replyMatch.JobId, replyMatch.SuggestedStage, replyMatch.MatchReason));
                        result.ProcessedEmails.Add(new ProcessedEmail
                        {
                            MessageId = email.MessageId,
                            Action = "StageUpdate",
                            RelatedJobId = replyMatch.JobId
                        });
                        continue;
                    }
                }

                // Try job alert parsing
                if (settings.EmailCheckParseJobAlerts)
                {
                    var alertResult = _alertParser.Parse(email);
                    if (alertResult.IsJobAlert && alertResult.JobUrls.Count > 0)
                    {
                        foreach (var url in alertResult.JobUrls)
                        {
                            // Skip URLs that already exist in the user's jobs
                            if (userJobs.Any(j => !string.IsNullOrWhiteSpace(j.Url) &&
                                j.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            result.JobsAdded++;
                            result.NewJobDetails.Add((url, alertResult.Source));
                        }

                        result.ProcessedEmails.Add(new ProcessedEmail
                        {
                            MessageId = email.MessageId,
                            Action = alertResult.JobUrls.Count > 0 ? "JobAdded" : "Ignored"
                        });
                        continue;
                    }
                }

                // No match
                result.Ignored++;
                result.ProcessedEmails.Add(new ProcessedEmail
                {
                    MessageId = email.MessageId,
                    Action = "Ignored"
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error processing email '{email.Subject}': {ex.Message}");
                _logger.LogError(ex, "[EmailProcessing] Error processing email {MessageId}", email.MessageId);
                result.ProcessedEmails.Add(new ProcessedEmail
                {
                    MessageId = email.MessageId,
                    Action = "Ignored"
                });
            }
        }

        return result;
    }
}
