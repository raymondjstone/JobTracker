namespace JobTracker.Models;

public class JobListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }  // Foreign key to User
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JobType JobType { get; set; } = JobType.FullTime;
    public string Url { get; set; } = string.Empty;
    public DateTime DatePosted { get; set; } = DateTime.Now;
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public string Salary { get; set; } = string.Empty;
    public bool IsRemote { get; set; }
    public List<string> Skills { get; set; } = new();
    public InterestStatus Interest { get; set; } = InterestStatus.NotRated;
    public bool HasApplied { get; set; } = false;
    public DateTime? DateApplied { get; set; }
    public ApplicationStage ApplicationStage { get; set; } = ApplicationStage.None;
    public List<ApplicationStageChange> StageHistory { get; set; } = new();
    public SuitabilityStatus Suitability { get; set; } = SuitabilityStatus.NotChecked;
    public bool SharedToWhatsApp { get; set; } = false;
    public DateTime? DateSharedToWhatsApp { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime? LastChecked { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string CoverLetter { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public class ApplicationStageChange
{
    public ApplicationStage Stage { get; set; }
    public DateTime DateChanged { get; set; }
    public string? Notes { get; set; }
}

public enum JobType
{
    FullTime,
    PartTime,
    Contract,
    Temporary,
    Internship,
    Volunteer,
    Other
}

public enum InterestStatus
{
    NotRated,
    Interested,
    NotInterested
}

public enum ApplicationStage
{
    None,
    Applied,
    NoReply,
    Pending,
    Ghosted,
    Rejected,
    TechTest,
    Interview,
    Offer
}

public enum SuitabilityStatus
{
    NotChecked,
    Possible,
    Unsuitable
}
