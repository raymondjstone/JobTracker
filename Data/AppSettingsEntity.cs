namespace JobTracker.Data;

public class AppSettingsEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }  // Foreign key to User - replaces singleton Id=1 pattern
    public string LinkedInUrl { get; set; } = "https://www.linkedin.com/jobs/";
    public string S1JobsUrl { get; set; } = "https://www.s1jobs.com/jobs/";
    public string IndeedUrl { get; set; } = "https://uk.indeed.com/jobs";
    public string WTTJUrl { get; set; } = "https://www.welcometothejungle.com/en/jobs";
    public bool EnableAutoRules { get; set; } = true;
    public bool StopOnFirstMatch { get; set; } = false;
    public int NoReplyDays { get; set; } = 3;
    public int GhostedDays { get; set; } = 3;
    public int StaleDays { get; set; } = 14;
    public string CoverLetterTemplatesJson { get; set; } = "[]";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string SmtpFromEmail { get; set; } = "";
    public string SmtpFromName { get; set; } = "Job Tracker";
    public bool EmailNotificationsEnabled { get; set; }
    public bool EmailOnStaleApplications { get; set; } = true;
    public bool EmailOnFollowUpDue { get; set; } = true;

    // IMAP settings
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;
    public string ImapUsername { get; set; } = "";
    public string ImapPassword { get; set; } = "";
    public string ImapFolder { get; set; } = "INBOX";
    public bool EmailCheckEnabled { get; set; }
    public bool EmailCheckAutoUpdateStage { get; set; } = true;
    public bool EmailCheckParseJobAlerts { get; set; } = true;

    public bool AutoArchiveEnabled { get; set; }
    public int AutoArchiveDays { get; set; } = 30;
    public bool DarkMode { get; set; }
    public string CrawlPagesJson { get; set; } = "[]";
}
