using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JobTracker.Tests;

public class JobScoringServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<AppSettingsService> _settingsMock;
    private readonly JobScoringService _service;
    private readonly ScoringPreferences _prefs;
    private readonly AppSettings _appSettings;

    public JobScoringServiceTests()
    {
        _prefs = new ScoringPreferences
        {
            EnableScoring = true,
            SkillsWeight = 1.0,
            SalaryWeight = 1.0,
            RemoteWeight = 1.0,
            LocationWeight = 1.0,
            KeywordWeight = 1.0,
            CompanyWeight = 1.0,
            LearningWeight = 0 // Disable behavioral by default to keep tests deterministic
        };

        _appSettings = new AppSettings { ScoringPreferences = _prefs };

        _settingsMock = new Mock<AppSettingsService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _settingsMock.Setup(s => s.GetSettings(It.IsAny<Guid?>())).Returns(_appSettings);

        var logger = new Mock<ILogger<JobScoringService>>();
        _service = new JobScoringService(logger.Object, _settingsMock.Object);
    }

    private JobListing MakeJob(string title = "Developer", string company = "Acme",
        string description = "", bool isRemote = false, string location = "",
        decimal? salaryMin = null, decimal? salaryMax = null, string salary = "") => new()
    {
        Id = Guid.NewGuid(),
        UserId = _userId,
        Title = title,
        Company = company,
        Description = description,
        IsRemote = isRemote,
        Location = location,
        SalaryMin = salaryMin,
        SalaryMax = salaryMax,
        Salary = salary,
        Skills = new List<string>()
    };

    [Fact]
    public void CalculateScore_ScoringDisabled_ReturnsZero()
    {
        _prefs.EnableScoring = false;
        var score = _service.CalculateScore(MakeJob(), _userId, new List<JobListing>());
        Assert.Equal(0, score);
    }

    [Fact]
    public void CalculateScore_NoPreferences_ReturnsBaselineScore()
    {
        // All weights on but no preferences set — only remote/learning contribute neutral scores
        _prefs.PreferredSkills.Clear();
        _prefs.MinDesiredSalary = 0;
        _prefs.PreferredLocations.Clear();
        _prefs.MustHaveKeywords.Clear();
        _prefs.AvoidKeywords.Clear();
        _prefs.PreferredCompanies.Clear();
        _prefs.AvoidCompanies.Clear();
        _prefs.LearningWeight = 1.0;

        var score = _service.CalculateScore(MakeJob(), _userId, new List<JobListing>());

        // Should be a positive baseline (neutral scores from remote + learning)
        Assert.InRange(score, 1, 99);
    }

    [Fact]
    public void CalculateScore_PreferredSkillsMatch_HigherScore()
    {
        _prefs.PreferredSkills.AddRange(new[] { "C#", "Blazor" });
        // Disable other weights to isolate skills
        _prefs.SalaryWeight = 0;
        _prefs.RemoteWeight = 0;
        _prefs.LocationWeight = 0;
        _prefs.KeywordWeight = 0;
        _prefs.CompanyWeight = 0;

        var jobWithSkills = MakeJob(title: "C# Blazor Developer");
        var jobWithoutSkills = MakeJob(title: "Python Developer");

        var scoreWith = _service.CalculateScore(jobWithSkills, _userId, new List<JobListing>());
        var scoreWithout = _service.CalculateScore(jobWithoutSkills, _userId, new List<JobListing>());

        Assert.True(scoreWith > scoreWithout, $"Skills match ({scoreWith}) should be higher than no match ({scoreWithout})");
    }

    [Fact]
    public void CalculateScore_SalaryInRange_HigherScore()
    {
        _prefs.MinDesiredSalary = 40000m;
        _prefs.MaxDesiredSalary = 60000m;
        // Isolate salary scoring
        _prefs.SkillsWeight = 0;
        _prefs.RemoteWeight = 0;
        _prefs.LocationWeight = 0;
        _prefs.KeywordWeight = 0;
        _prefs.CompanyWeight = 0;

        var jobInRange = MakeJob(salaryMin: 50000m);
        var jobBelowRange = MakeJob(salaryMin: 20000m);

        var scoreInRange = _service.CalculateScore(jobInRange, _userId, new List<JobListing>());
        var scoreBelowRange = _service.CalculateScore(jobBelowRange, _userId, new List<JobListing>());

        Assert.True(scoreInRange > scoreBelowRange,
            $"In-range salary ({scoreInRange}) should score higher than below-range ({scoreBelowRange})");
    }

    [Fact]
    public void CalculateScore_RemotePreference_HigherScore()
    {
        _prefs.PreferRemote = true;
        // Isolate remote scoring
        _prefs.SkillsWeight = 0;
        _prefs.SalaryWeight = 0;
        _prefs.LocationWeight = 0;
        _prefs.KeywordWeight = 0;
        _prefs.CompanyWeight = 0;

        var remoteJob = MakeJob(isRemote: true);
        var onsiteJob = MakeJob(isRemote: false);

        var scoreRemote = _service.CalculateScore(remoteJob, _userId, new List<JobListing>());
        var scoreOnsite = _service.CalculateScore(onsiteJob, _userId, new List<JobListing>());

        Assert.True(scoreRemote > scoreOnsite,
            $"Remote job ({scoreRemote}) should score higher than onsite ({scoreOnsite})");
    }

    [Fact]
    public void CalculateScore_AvoidKeyword_LowerScore()
    {
        _prefs.MustHaveKeywords.Add("cloud");
        _prefs.AvoidKeywords.Add("legacy");
        // Isolate keyword scoring
        _prefs.SkillsWeight = 0;
        _prefs.SalaryWeight = 0;
        _prefs.RemoteWeight = 0;
        _prefs.LocationWeight = 0;
        _prefs.CompanyWeight = 0;

        var goodJob = MakeJob(description: "cloud infrastructure role");
        var badJob = MakeJob(description: "legacy maintenance role");

        var scoreGood = _service.CalculateScore(goodJob, _userId, new List<JobListing>());
        var scoreBad = _service.CalculateScore(badJob, _userId, new List<JobListing>());

        Assert.True(scoreGood > scoreBad,
            $"Good keywords ({scoreGood}) should score higher than avoid keywords ({scoreBad})");
    }

    [Fact]
    public void CalculateScore_ResultClampedTo0_100()
    {
        _prefs.PreferredSkills.Add("C#");
        _prefs.MinDesiredSalary = 50000m;
        _prefs.MaxDesiredSalary = 80000m;
        _prefs.PreferRemote = true;
        _prefs.PreferredLocations.Add("London");
        _prefs.MustHaveKeywords.Add("cloud");
        _prefs.PreferredCompanies.Add("Acme");

        var job = MakeJob(
            title: "C# Developer",
            company: "Acme",
            description: "cloud C# role",
            isRemote: true,
            location: "London",
            salaryMin: 60000m);

        var score = _service.CalculateScore(job, _userId, new List<JobListing>());

        Assert.InRange(score, 0, 100);
    }

    [Fact]
    public void CalculateScore_AvoidCompany_LowerScore()
    {
        _prefs.AvoidCompanies.Add("BadCorp");
        // Isolate company scoring
        _prefs.SkillsWeight = 0;
        _prefs.SalaryWeight = 0;
        _prefs.RemoteWeight = 0;
        _prefs.LocationWeight = 0;
        _prefs.KeywordWeight = 0;

        var goodJob = MakeJob(company: "GoodCorp");
        var badJob = MakeJob(company: "BadCorp");

        var scoreGood = _service.CalculateScore(goodJob, _userId, new List<JobListing>());
        var scoreBad = _service.CalculateScore(badJob, _userId, new List<JobListing>());

        Assert.True(scoreGood > scoreBad,
            $"Good company ({scoreGood}) should score higher than avoided company ({scoreBad})");
    }
}
