namespace JobTracker.Models;

public class AppSettings
{
    public JobSiteUrls JobSiteUrls { get; set; } = new();
    public JobRulesSettings JobRules { get; set; } = new();
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
