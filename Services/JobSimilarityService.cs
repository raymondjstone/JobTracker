using JobTracker.Models;

namespace JobTracker.Services;

public class JobSimilarityService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "for", "in", "at", "of", "to", "is", "it",
        "on", "by", "with", "as", "be", "this", "that", "are", "was", "were", "been",
        "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
        "may", "might", "shall", "can", "not", "no", "but", "if", "from", "we", "you",
        "they", "their", "our", "your", "its", "all", "any", "each", "every", "both",
        "few", "more", "most", "other", "some", "such", "only", "own", "same", "so",
        "than", "too", "very", "just", "about", "above", "after", "again", "also",
        "am", "an", "because", "before", "between", "during", "into", "through",
        "under", "until", "up", "what", "when", "where", "which", "while", "who",
        "|", "-", "/", "&", "role", "job", "work", "working", "looking", "based",
        "experience", "team", "company", "opportunity", "position", "required", "using"
    };

    public List<SimilarJob> FindSimilarJobs(JobListing target, List<JobListing> allJobs, int maxResults = 5)
    {
        var targetTitleTokens = Tokenize(target.Title);
        var targetSkills = new HashSet<string>(target.Skills.Select(s => s.ToLowerInvariant()));
        var targetCompany = NormalizeCompany(target.Company);
        var targetLocation = target.Location.ToLowerInvariant().Trim();
        var targetDescTokens = ExtractTopTokens(target.Description, 20);

        var results = new List<SimilarJob>();

        foreach (var job in allJobs)
        {
            if (job.Id == target.Id || job.IsArchived)
                continue;

            // Title similarity (40%)
            var jobTitleTokens = Tokenize(job.Title);
            double titleScore = Jaccard(targetTitleTokens, jobTitleTokens);

            // Skills overlap (30%)
            var jobSkills = new HashSet<string>(job.Skills.Select(s => s.ToLowerInvariant()));
            double skillsScore = Jaccard(targetSkills, jobSkills);

            // Company match (10%)
            double companyScore = NormalizeCompany(job.Company) == targetCompany ? 1.0 : 0.0;

            // Location match (10%)
            var jobLocation = job.Location.ToLowerInvariant().Trim();
            double locationScore = 0.0;
            if (!string.IsNullOrEmpty(targetLocation) && !string.IsNullOrEmpty(jobLocation))
            {
                if (targetLocation == jobLocation)
                    locationScore = 1.0;
                else if (targetLocation.Contains(jobLocation) || jobLocation.Contains(targetLocation))
                    locationScore = 0.7;
            }

            // Description keyword overlap (10%)
            var jobDescTokens = ExtractTopTokens(job.Description, 20);
            double descScore = Jaccard(targetDescTokens, jobDescTokens);

            double totalScore = titleScore * 0.4 + skillsScore * 0.3 + companyScore * 0.1
                              + locationScore * 0.1 + descScore * 0.1;

            if (totalScore > 0.08)
            {
                results.Add(new SimilarJob
                {
                    JobId = job.Id,
                    Title = job.Title,
                    Company = job.Company,
                    Score = Math.Round(totalScore * 100, 1)
                });
            }
        }

        return results.OrderByDescending(r => r.Score).Take(maxResults).ToList();
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>();
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '(', ')', '[', ']', '/', '|', '-', ':', ';' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !StopWords.Contains(w));
        return new HashSet<string>(words);
    }

    private static HashSet<string> ExtractTopTokens(string text, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>();
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '(', ')', '[', ']', '/', '|', '-', ':', ';', '!', '?' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w));

        return new HashSet<string>(
            words.GroupBy(w => w)
                 .OrderByDescending(g => g.Count())
                 .Take(count)
                 .Select(g => g.Key));
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0;
        if (a.Count == 0 || b.Count == 0) return 0;
        int intersection = a.Count(x => b.Contains(x));
        int union = a.Count + b.Count - intersection;
        return union > 0 ? (double)intersection / union : 0;
    }

    private static string NormalizeCompany(string company)
    {
        return JobListingService.NormalizeCompanyName(company).ToLowerInvariant();
    }
}

public class SimilarJob
{
    public Guid JobId { get; set; }
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public double Score { get; set; }
}
