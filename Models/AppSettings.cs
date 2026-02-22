namespace JobTracker.Models;

public class AppSettings
{
    public JobSiteUrls JobSiteUrls { get; set; } = new();
    public JobRulesSettings JobRules { get; set; } = new();
    public List<SavedFilterPreset> FilterPresets { get; set; } = new();
    public List<string> HighlightKeywords { get; set; } = new();
    public PipelineSettings Pipeline { get; set; } = new();
    public List<CoverLetterTemplate> CoverLetterTemplates { get; set; } = new();
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string SmtpFromEmail { get; set; } = "";
    public string SmtpFromName { get; set; } = "Job Tracker";
    public bool EmailNotificationsEnabled { get; set; }
    public bool EmailOnStaleApplications { get; set; } = true;
    public bool EmailOnFollowUpDue { get; set; } = true;
    public bool AutoArchiveEnabled { get; set; }
    public int AutoArchiveDays { get; set; } = 30;
    public bool DarkMode { get; set; }
}

public class CoverLetterTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}

public class PipelineSettings
{
    public int NoReplyDays { get; set; } = 3;
    public int GhostedDays { get; set; } = 3;
    public int StaleDays { get; set; } = 14;
}

public class SavedFilterPreset
{
    public string Name { get; set; } = string.Empty;
    public string? SearchTerm { get; set; }
    public string? TitleOnlySearchTerm { get; set; }
    public string? Location { get; set; }
    public string? JobType { get; set; }
    public string? IsRemote { get; set; }
    public string? Interest { get; set; }
    public string? HasSalary { get; set; }
    public string? SalarySearch { get; set; }
    public string? SalaryTarget { get; set; }
    public string? Source { get; set; }
    public string? SkillSearch { get; set; }
    public string? ApplicationStage { get; set; }
    public string? SortBy { get; set; }
}

public class JobSiteUrls
{
    public string LinkedIn { get; set; } = "https://www.linkedin.com/jobs/";
    public string S1Jobs { get; set; } = "https://www.s1jobs.com/jobs/";
    public string Indeed { get; set; } = "https://uk.indeed.com/jobs";
    public string WTTJ { get; set; } = "https://www.welcometothejungle.com/en/jobs";
    public string EnergyJobSearch { get; set; } = "https://energyjobsearch.com/jobs?title=.net";

    public bool CrawlLinkedIn { get; set; } = true;
    public bool CrawlS1Jobs { get; set; } = true;
    public bool CrawlWTTJ { get; set; } = true;
    public bool CrawlEnergyJobSearch { get; set; } = true;
}

public class JobRulesSettings
{
    public bool EnableAutoRules { get; set; } = true;
    public bool StopOnFirstMatch { get; set; } = false;
    public List<JobRule> Rules { get; set; } = new();
}
