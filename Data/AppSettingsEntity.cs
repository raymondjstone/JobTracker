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
}
