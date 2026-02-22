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
    public int DeleteUnsuitableAfterDays { get; set; } = 90;
    public int DeleteRejectedAfterDays { get; set; } = 60;
    public int DeleteGhostedAfterDays { get; set; } = 60;
    public ScoringPreferences ScoringPreferences { get; set; } = new();
    public AIAssistantSettings AIAssistant { get; set; } = new();
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
    public string? ScoreBand { get; set; }
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

public class ScoringPreferences
{
    public bool EnableScoring { get; set; } = true;

    // Weights (0-1, where 1 = full weight, 0 = disabled)
    public double SkillsWeight { get; set; } = 1.0;
    public double SalaryWeight { get; set; } = 1.0;
    public double RemoteWeight { get; set; } = 1.0;
    public double LocationWeight { get; set; } = 0.8;
    public double KeywordWeight { get; set; } = 0.9;
    public double CompanyWeight { get; set; } = 0.7;
    public double LearningWeight { get; set; } = 1.0;

    // Preferences
    public List<string> PreferredSkills { get; set; } = new();
    public decimal MinDesiredSalary { get; set; } = 0;
    public decimal MaxDesiredSalary { get; set; } = 0;
    public bool PreferRemote { get; set; } = true;
    public List<string> PreferredLocations { get; set; } = new();
    public List<string> MustHaveKeywords { get; set; } = new();
    public List<string> AvoidKeywords { get; set; } = new();
    public List<string> PreferredCompanies { get; set; } = new();
    public List<string> AvoidCompanies { get; set; } = new();

    // Auto-score threshold
    public int MinScoreToShow { get; set; } = 0; // Hide jobs below this score
}

public class AIAssistantSettings
{
    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "OpenAI"; // "OpenAI" or "Claude"
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-3.5-turbo"; // OpenAI: gpt-3.5-turbo, gpt-4 | Claude: claude-3-5-sonnet-20241022, claude-3-opus-20240229
    public bool AutoAnalyzeNewJobs { get; set; } = false;
    public bool AutoGenerateCoverLetter { get; set; } = false;
    public bool ShowSkillGaps { get; set; } = true;
    public bool ShowSimilarJobs { get; set; } = true;
    public List<string> UserSkills { get; set; } = new(); // User's skill profile
    public string UserExperience { get; set; } = string.Empty; // Brief experience summary
}
