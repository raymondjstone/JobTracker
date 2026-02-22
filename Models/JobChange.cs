namespace JobTracker.Models;

public class JobChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public ChangeType ChangeType { get; set; }
    public string Description { get; set; } = string.Empty;
    public ChangeImpact Impact { get; set; }
}

public enum ChangeType
{
    Added,
    Modified,
    Removed
}

public enum ChangeImpact
{
    Minor,      // e.g., description formatting change
    Moderate,   // e.g., skills added/removed
    Major       // e.g., salary changed, job type changed
}

public class JobChangeTimeline
{
    public JobListing Job { get; set; } = null!;
    public List<JobChange> Changes { get; set; } = new();
    public DateTime FirstSeen { get; set; }
    public DateTime LastUpdated { get; set; }
    public int TotalChanges { get; set; }
    public bool HasRecentChanges { get; set; }
}
