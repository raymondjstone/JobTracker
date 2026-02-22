using JobTracker.Models;
using System.Text.RegularExpressions;

namespace JobTracker.Services;

public class JobScoringService
{
    private readonly ILogger<JobScoringService> _logger;
    private readonly AppSettingsService _settingsService;

    public JobScoringService(ILogger<JobScoringService> logger, AppSettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Calculate a suitability score (0-100) for a job based on user preferences and past behavior
    /// </summary>
    public int CalculateScore(JobListing job, Guid userId, List<JobListing> allUserJobs)
    {
        var settings = _settingsService.GetSettings(userId);
        var scoringPrefs = settings.ScoringPreferences;

        if (!scoringPrefs.EnableScoring)
            return 0;

        double totalScore = 0;
        double maxPossibleScore = 0;

        // 1. Skills Match (25 points max)
        if (scoringPrefs.SkillsWeight > 0 && scoringPrefs.PreferredSkills.Any())
        {
            var skillScore = CalculateSkillsMatch(job, scoringPrefs.PreferredSkills) * scoringPrefs.SkillsWeight;
            totalScore += skillScore;
            maxPossibleScore += 25 * scoringPrefs.SkillsWeight;
        }

        // 2. Salary Match (20 points max)
        if (scoringPrefs.SalaryWeight > 0 && scoringPrefs.MinDesiredSalary > 0)
        {
            var salaryScore = CalculateSalaryMatch(job, scoringPrefs.MinDesiredSalary, scoringPrefs.MaxDesiredSalary) * scoringPrefs.SalaryWeight;
            totalScore += salaryScore;
            maxPossibleScore += 20 * scoringPrefs.SalaryWeight;
        }

        // 3. Remote Preference (15 points max)
        if (scoringPrefs.RemoteWeight > 0)
        {
            var remoteScore = CalculateRemoteMatch(job, scoringPrefs.PreferRemote) * scoringPrefs.RemoteWeight;
            totalScore += remoteScore;
            maxPossibleScore += 15 * scoringPrefs.RemoteWeight;
        }

        // 4. Location Preference (10 points max)
        if (scoringPrefs.LocationWeight > 0 && scoringPrefs.PreferredLocations.Any())
        {
            var locationScore = CalculateLocationMatch(job, scoringPrefs.PreferredLocations) * scoringPrefs.LocationWeight;
            totalScore += locationScore;
            maxPossibleScore += 10 * scoringPrefs.LocationWeight;
        }

        // 5. Keywords in Description (15 points max)
        if (scoringPrefs.KeywordWeight > 0 && scoringPrefs.MustHaveKeywords.Any())
        {
            var keywordScore = CalculateKeywordMatch(job, scoringPrefs.MustHaveKeywords, scoringPrefs.AvoidKeywords) * scoringPrefs.KeywordWeight;
            totalScore += keywordScore;
            maxPossibleScore += 15 * scoringPrefs.KeywordWeight;
        }

        // 6. Company Preference (10 points max)
        if (scoringPrefs.CompanyWeight > 0 && (scoringPrefs.PreferredCompanies.Any() || scoringPrefs.AvoidCompanies.Any()))
        {
            var companyScore = CalculateCompanyMatch(job, scoringPrefs.PreferredCompanies, scoringPrefs.AvoidCompanies) * scoringPrefs.CompanyWeight;
            totalScore += companyScore;
            maxPossibleScore += 10 * scoringPrefs.CompanyWeight;
        }

        // 7. Learning from Past Behavior (15 points max)
        if (scoringPrefs.LearningWeight > 0)
        {
            var behaviorScore = CalculateBehavioralScore(job, allUserJobs) * scoringPrefs.LearningWeight;
            totalScore += behaviorScore;
            maxPossibleScore += 15 * scoringPrefs.LearningWeight;
        }

        // Normalize to 0-100
        if (maxPossibleScore == 0)
            return 0;

        var normalizedScore = (int)Math.Round((totalScore / maxPossibleScore) * 100);
        return Math.Clamp(normalizedScore, 0, 100);
    }

    private double CalculateSkillsMatch(JobListing job, List<string> preferredSkills)
    {
        if (!preferredSkills.Any())
            return 0;

        var jobSkillsLower = job.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var descriptionLower = job.Description?.ToLowerInvariant() ?? "";
        var titleLower = job.Title.ToLowerInvariant();

        int matchCount = 0;
        foreach (var skill in preferredSkills)
        {
            var skillLower = skill.ToLowerInvariant();
            // Check if skill is in job skills list or mentioned in title/description
            if (jobSkillsLower.Contains(skillLower) || 
                titleLower.Contains(skillLower) || 
                descriptionLower.Contains(skillLower))
            {
                matchCount++;
            }
        }

        return (double)matchCount / preferredSkills.Count * 25;
    }

    private double CalculateSalaryMatch(JobListing job, decimal minDesired, decimal maxDesired)
    {
        // Try to extract salary from job
        decimal? jobSalary = job.SalaryMin ?? job.SalaryMax;
        
        if (!jobSalary.HasValue && !string.IsNullOrEmpty(job.Salary))
        {
            // Try to parse from salary string
            jobSalary = ExtractSalaryFromString(job.Salary);
        }

        if (!jobSalary.HasValue)
            return 10; // Neutral score if no salary info

        if (jobSalary >= minDesired)
        {
            if (maxDesired > 0 && jobSalary <= maxDesired)
                return 20; // Perfect match
            else if (jobSalary >= minDesired)
                return 15; // Above minimum
        }

        // Below minimum - penalize
        var ratio = (double)jobSalary.Value / (double)minDesired;
        return Math.Max(0, ratio * 10);
    }

    private double CalculateRemoteMatch(JobListing job, bool preferRemote)
    {
        if (preferRemote && job.IsRemote)
            return 15;
        if (!preferRemote && !job.IsRemote)
            return 15;
        if (preferRemote && !job.IsRemote)
            return 3; // Penalty for non-remote when preferred
        return 10; // Neutral
    }

    private double CalculateLocationMatch(JobListing job, List<string> preferredLocations)
    {
        if (!preferredLocations.Any() || string.IsNullOrEmpty(job.Location))
            return 5; // Neutral

        var locationLower = job.Location.ToLowerInvariant();
        foreach (var preferred in preferredLocations)
        {
            if (locationLower.Contains(preferred.ToLowerInvariant()))
                return 10;
        }

        return 0; // No match
    }

    private double CalculateKeywordMatch(JobListing job, List<string> mustHave, List<string> avoid)
    {
        var textLower = $"{job.Title} {job.Description}".ToLowerInvariant();
        double score = 0;

        // Must-have keywords
        if (mustHave.Any())
        {
            int foundCount = 0;
            foreach (var keyword in mustHave)
            {
                if (textLower.Contains(keyword.ToLowerInvariant()))
                    foundCount++;
            }
            score += (double)foundCount / mustHave.Count * 12;
        }
        else
        {
            score += 6; // Neutral if no must-have keywords
        }

        // Avoid keywords - penalty
        if (avoid.Any())
        {
            bool hasAnyAvoid = false;
            foreach (var keyword in avoid)
            {
                if (textLower.Contains(keyword.ToLowerInvariant()))
                {
                    hasAnyAvoid = true;
                    break;
                }
            }
            score += hasAnyAvoid ? -5 : 3; // Penalty or bonus
        }
        else
        {
            score += 3; // Neutral if no avoid keywords
        }

        return Math.Max(0, score);
    }

    private double CalculateCompanyMatch(JobListing job, List<string> preferred, List<string> avoid)
    {
        var companyLower = job.Company.ToLowerInvariant();

        // Check avoid list first
        foreach (var avoidCompany in avoid)
        {
            if (companyLower.Contains(avoidCompany.ToLowerInvariant()))
                return -5; // Strong penalty
        }

        // Check preferred list
        foreach (var preferredCompany in preferred)
        {
            if (companyLower.Contains(preferredCompany.ToLowerInvariant()))
                return 10;
        }

        return 5; // Neutral
    }

    private double CalculateBehavioralScore(JobListing job, List<JobListing> allUserJobs)
    {
        // Learn from past behavior
        var interestedJobs = allUserJobs.Where(j => j.Interest == InterestStatus.Interested || j.HasApplied).ToList();
        
        if (!interestedJobs.Any())
            return 7.5; // Neutral if no history

        double score = 0;

        // Similar titles
        var titleWords = ExtractKeywords(job.Title);
        var interestedTitleWords = interestedJobs
            .SelectMany(j => ExtractKeywords(j.Title))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        var titleMatch = titleWords.Intersect(interestedTitleWords).Count();
        score += Math.Min(5, titleMatch * 1.5);

        // Similar companies or job types
        if (interestedJobs.Any(j => j.Company.Equals(job.Company, StringComparison.OrdinalIgnoreCase)))
            score += 3;

        if (interestedJobs.Any(j => j.JobType == job.JobType))
            score += 2;

        // Similar sources
        if (interestedJobs.Any(j => j.Source.Equals(job.Source, StringComparison.OrdinalIgnoreCase)))
            score += 1;

        // Remote preference pattern
        var remoteInterestCount = interestedJobs.Count(j => j.IsRemote);
        if (remoteInterestCount > interestedJobs.Count / 2 && job.IsRemote)
            score += 3;

        return Math.Min(15, score);
    }

    private List<string> ExtractKeywords(string text)
    {
        var words = Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length > 3) // Only words longer than 3 chars
            .Where(w => !CommonStopWords.Contains(w))
            .ToList();
        return words;
    }

    private decimal? ExtractSalaryFromString(string salary)
    {
        // Try to extract numeric salary from various formats
        var numbers = Regex.Matches(salary, @"\d{1,3}(?:,\d{3})*(?:\.\d{2})?|\d+");
        if (numbers.Count > 0)
        {
            var firstNumber = numbers[0].Value.Replace(",", "");
            if (decimal.TryParse(firstNumber, out var amount))
            {
                // If it's a small number, might be in thousands (e.g., "50" = "50k")
                if (amount < 1000 && salary.ToLowerInvariant().Contains("k"))
                    return amount * 1000;
                return amount;
            }
        }
        return null;
    }

    private static readonly HashSet<string> CommonStopWords = new()
    {
        "the", "and", "for", "with", "from", "this", "that", "will", "your", "our",
        "you", "are", "have", "has", "been", "who", "what", "where", "when", "which"
    };
}
