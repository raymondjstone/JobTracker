namespace JobTracker.Models;

public class JobHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }  // Foreign key to User
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // What job was affected
    public Guid JobId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? JobUrl { get; set; }

    // What happened
    public HistoryActionType ActionType { get; set; }
    public HistoryChangeSource ChangeSource { get; set; }

    // Details
    public string? FieldName { get; set; } // Which field changed
    public string? OldValue { get; set; }  
    public string? NewValue { get; set; }
    public string? Details { get; set; }
    public string? RuleName { get; set; } // If changed by a rule
}

public enum HistoryActionType
{
    JobAdded,
    JobDeleted,
    DescriptionUpdated,
    InterestChanged,
    SuitabilityChanged,
    AppliedStatusChanged,
    ApplicationStageChanged,
    TitleUpdated,
    CompanyUpdated,
    LocationUpdated,
    SalaryUpdated,
    NotesUpdated,
    SkillsUpdated,
    BulkImport,
    DuplicateRemoved,
    RemoteStatusChanged,
    Modified              // Generic field modification (for change timeline)
}

public enum HistoryChangeSource
{
    Manual,           // User clicked a button in the UI
    Rule,             // Changed by an automatic rule
    AutoFetch,        // Description fetched automatically
    BrowserExtension, // Added/updated via browser extension API
    Import,           // Bulk import from JSON
    System            // System cleanup, deduplication, etc.
}

public class JobHistoryFilter
{
    public HistoryActionType? ActionType { get; set; }
    public HistoryChangeSource? ChangeSource { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? JobId { get; set; }
    public string? SearchTerm { get; set; }
}

public class PagedHistoryResult
{
    public List<JobHistoryEntry> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
