namespace JobTracker.Models;

public class ProcessedEmail
{
    public string MessageId { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.Now;
    public string Action { get; set; } = "Ignored"; // "StageUpdate", "JobAdded", "Ignored"
    public Guid? RelatedJobId { get; set; }
}
