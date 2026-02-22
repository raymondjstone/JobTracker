using JobTracker.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JobTracker.Services;

public class AIJobAssistantService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettingsService _settingsService;
    private readonly ILogger<AIJobAssistantService> _logger;
    private readonly IConfiguration _configuration;

    public AIJobAssistantService(
        HttpClient httpClient,
        AppSettingsService settingsService,
        ILogger<AIJobAssistantService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Analyze a job description and extract key information
    /// </summary>
    public async Task<JobAnalysis?> AnalyzeJobAsync(JobListing job, Guid userId)
    {
        var settings = _settingsService.GetSettings(userId);
        
        if (!settings.AIAssistant.Enabled || string.IsNullOrEmpty(settings.AIAssistant.ApiKey))
        {
            _logger.LogDebug("AI Assistant is disabled or no API key configured");
            return null;
        }

        var prompt = $@"Analyze this job posting and extract key information in JSON format:

Job Title: {job.Title}
Company: {job.Company}
Location: {job.Location}
Description: {job.Description}

Please provide:
1. A brief summary (2-3 sentences)
2. Key responsibilities (list of 3-5 main duties)
3. Required skills (list of technical and soft skills)
4. Qualifications (education, experience requirements)
5. Nice-to-have skills (optional skills that would be beneficial)

Respond ONLY with valid JSON in this format:
{{
  ""summary"": ""Brief 2-3 sentence summary"",
  ""responsibilities"": [""responsibility 1"", ""responsibility 2"", ...],
  ""requiredSkills"": [""skill 1"", ""skill 2"", ...],
  ""qualifications"": [""qualification 1"", ""qualification 2"", ...],
  ""niceToHaveSkills"": [""skill 1"", ""skill 2"", ...]
}}";

        try
        {
            var response = await CallAIAsync(prompt, settings.AIAssistant.ApiKey, settings.AIAssistant.Model, settings.AIAssistant.Provider);
            if (string.IsNullOrEmpty(response))
                return null;

            var analysis = JsonSerializer.Deserialize<JobAnalysis>(response, new JsonSerializerOptions
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze job: {Title}", job.Title);
            return null;
        }
    }

    /// <summary>
    /// Generate personalized cover letter suggestions based on job and user profile
    /// </summary>
    public async Task<CoverLetterSuggestions?> GenerateCoverLetterSuggestionsAsync(
        JobListing job, 
        Guid userId,
        List<string> userSkills,
        string? userExperience = null)
    {
        var settings = _settingsService.GetSettings(userId);
        
        if (!settings.AIAssistant.Enabled || string.IsNullOrEmpty(settings.AIAssistant.ApiKey))
        {
            return null;
        }

        var skillsText = userSkills.Any() ? string.Join(", ", userSkills) : "Not specified";
        var experienceText = !string.IsNullOrEmpty(userExperience) ? userExperience : "Not specified";

        var prompt = $@"Generate personalized cover letter suggestions for this job application:

Job Title: {job.Title}
Company: {job.Company}
Job Description: {job.Description?.Substring(0, Math.Min(1000, job.Description?.Length ?? 0))}

Applicant Skills: {skillsText}
Applicant Experience: {experienceText}

Please provide:
1. An opening paragraph that grabs attention (2-3 sentences)
2. 2-3 key selling points that match the job requirements with the applicant's skills
3. A strong closing paragraph expressing enthusiasm and call-to-action

Respond ONLY with valid JSON in this format:
{{
  ""openingParagraph"": ""Your attention-grabbing opening"",
  ""sellingPoints"": [""point 1 connecting your skills to their needs"", ""point 2"", ""point 3""],
  ""closingParagraph"": ""Your enthusiastic closing with call-to-action""
}}";

        try
        {
            var response = await CallAIAsync(prompt, settings.AIAssistant.ApiKey, settings.AIAssistant.Model, settings.AIAssistant.Provider);
            if (string.IsNullOrEmpty(response))
                return null;

            var suggestions = JsonSerializer.Deserialize<CoverLetterSuggestions>(response, new JsonSerializerOptions
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate cover letter suggestions for: {Title}", job.Title);
            return null;
        }
    }

    /// <summary>
    /// Analyze skill gaps between job requirements and user's skills
    /// </summary>
    public SkillGapAnalysis AnalyzeSkillGaps(
        List<string> jobRequiredSkills,
        List<string> jobNiceToHaveSkills,
        List<string> userSkills)
    {
        var userSkillsLower = userSkills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var requiredLower = jobRequiredSkills.Select(s => s.ToLowerInvariant()).ToList();
        var niceToHaveLower = jobNiceToHaveSkills.Select(s => s.ToLowerInvariant()).ToList();

        var matchedSkills = requiredLower
            .Where(r => userSkillsLower.Any(u => u.Contains(r) || r.Contains(u)))
            .ToList();

        var missingRequired = jobRequiredSkills
            .Where(r => !userSkillsLower.Any(u => 
                u.Contains(r.ToLowerInvariant()) || 
                r.ToLowerInvariant().Contains(u)))
            .ToList();

        var matchedNiceToHave = jobNiceToHaveSkills
            .Where(n => userSkillsLower.Any(u => 
                u.Contains(n.ToLowerInvariant()) || 
                n.ToLowerInvariant().Contains(u)))
            .ToList();

        var missingNiceToHave = jobNiceToHaveSkills
            .Where(n => !userSkillsLower.Any(u => 
                u.Contains(n.ToLowerInvariant()) || 
                n.ToLowerInvariant().Contains(u)))
            .ToList();

        var matchPercentage = requiredLower.Any() 
            ? (int)((double)matchedSkills.Count / requiredLower.Count * 100)
            : 100;

        return new SkillGapAnalysis
        {
            MatchedSkills = matchedSkills,
            MissingRequiredSkills = missingRequired,
            MatchedNiceToHaveSkills = matchedNiceToHave,
            MissingNiceToHaveSkills = missingNiceToHave,
            MatchPercentage = matchPercentage
        };
    }

    /// <summary>
    /// Find similar jobs based on skills, company, and interests
    /// </summary>
    public List<JobRecommendation> GetSimilarJobs(
        JobListing sourceJob,
        List<JobListing> allJobs,
        int maxResults = 5)
    {
        var recommendations = new List<JobRecommendation>();

        foreach (var job in allJobs.Where(j => j.Id != sourceJob.Id && !j.IsArchived))
        {
            var score = CalculateSimilarityScore(sourceJob, job);
            if (score > 0)
            {
                recommendations.Add(new JobRecommendation
                {
                    Job = job,
                    SimilarityScore = score,
                    Reasons = GetSimilarityReasons(sourceJob, job)
                });
            }
        }

        return recommendations
            .OrderByDescending(r => r.SimilarityScore)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Generate interview questions based on job requirements
    /// </summary>
    public async Task<InterviewQuestions?> GenerateInterviewQuestionsAsync(
        JobListing job,
        Guid userId)
    {
        var settings = _settingsService.GetSettings(userId);

        if (!settings.AIAssistant.Enabled || string.IsNullOrEmpty(settings.AIAssistant.ApiKey))
            return null;

        var prompt = $@"Generate interview preparation questions for this job:

Job Title: {job.Title}
Company: {job.Company}
Job Description: {job.Description?.Substring(0, Math.Min(1500, job.Description?.Length ?? 0))}

Please provide:
1. 5 technical questions related to the role's requirements
2. 5 behavioral/situational questions
3. 3 company-specific questions to ask the interviewer
4. 3 key preparation tips

Respond ONLY with valid JSON in this format:
{{
  ""technicalQuestions"": [""question 1"", ""question 2"", ...],
  ""behavioralQuestions"": [""question 1"", ""question 2"", ...],
  ""companySpecificQuestions"": [""question 1"", ""question 2"", ...],
  ""preparationTips"": [""tip 1"", ""tip 2"", ...]
}}";

        try
        {
            var response = await CallAIAsync(prompt, settings.AIAssistant.ApiKey, settings.AIAssistant.Model, settings.AIAssistant.Provider);
            if (string.IsNullOrEmpty(response))
                return null;

            var questions = JsonSerializer.Deserialize<InterviewQuestions>(response, new JsonSerializerOptions
            { 
                PropertyNameCaseInsensitive = true 
            });

            return questions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate interview questions for: {Title}", job.Title);
            return null;
        }
    }

    /// <summary>
    /// Generate salary negotiation tips and strategies
    /// </summary>
    public async Task<SalaryNegotiationTips?> GenerateSalaryNegotiationTipsAsync(
        JobListing job,
        Guid userId,
        string currentSalary = "")
    {
        var settings = _settingsService.GetSettings(userId);

        if (!settings.AIAssistant.Enabled || string.IsNullOrEmpty(settings.AIAssistant.ApiKey))
            return null;

        var currentSalaryText = !string.IsNullOrEmpty(currentSalary) ? currentSalary : "Not specified";
        var jobSalary = !string.IsNullOrEmpty(job.Salary) ? job.Salary : "Not specified";

        var prompt = $@"Provide salary negotiation guidance for this job offer:

Job Title: {job.Title}
Company: {job.Company}
Posted Salary Range: {jobSalary}
Applicant's Current Salary: {currentSalaryText}
Location: {job.Location}

Please provide:
1. Market rate estimate for this role
2. 4-5 negotiation strategies
3. 3-4 value points to emphasize
4. An opening statement for salary discussion
5. A counter-offer script

Respond ONLY with valid JSON in this format:
{{
  ""marketRateEstimate"": ""estimated range with reasoning"",
  ""negotiationStrategies"": [""strategy 1"", ""strategy 2"", ...],
  ""valuePoints"": [""point 1"", ""point 2"", ...],
  ""openingStatement"": ""professional opening for salary discussion"",
  ""counterOfferScript"": ""script for counter-offer""
}}";

        try
        {
            var response = await CallAIAsync(prompt, settings.AIAssistant.ApiKey, settings.AIAssistant.Model, settings.AIAssistant.Provider);
            if (string.IsNullOrEmpty(response))
                return null;

            var tips = JsonSerializer.Deserialize<SalaryNegotiationTips>(response, new JsonSerializerOptions
            { 
                PropertyNameCaseInsensitive = true 
            });

            return tips;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate salary negotiation tips for: {Title}", job.Title);
            return null;
        }
    }

    /// <summary>
    /// Generate resume optimization suggestions for a specific job
    /// </summary>
    public async Task<ResumeOptimization?> GenerateResumeOptimizationAsync(
        JobListing job,
        Guid userId)
    {
        var settings = _settingsService.GetSettings(userId);

        if (!settings.AIAssistant.Enabled || string.IsNullOrEmpty(settings.AIAssistant.ApiKey))
            return null;

        var userSkills = string.Join(", ", settings.AIAssistant.UserSkills);
        var userExperience = settings.AIAssistant.UserExperience;

        var prompt = $@"Provide resume optimization advice for this job application:

Job Title: {job.Title}
Company: {job.Company}
Job Requirements: {job.Description?.Substring(0, Math.Min(1500, job.Description?.Length ?? 0))}

Applicant Profile:
Skills: {userSkills}
Experience: {userExperience}

Please provide:
1. Top 5 skills to highlight on resume
2. 5-7 keywords to include (for ATS systems)
3. 3-4 experience points to emphasize
4. 3-4 specific improvements to make
5. A tailored professional summary (2-3 sentences)

Respond ONLY with valid JSON in this format:
{{
  ""skillsToHighlight"": [""skill 1"", ""skill 2"", ...],
  ""keywordsToInclude"": [""keyword 1"", ""keyword 2"", ...],
  ""experienceToEmphasize"": [""experience 1"", ""experience 2"", ...],
  ""suggestedImprovements"": [""improvement 1"", ""improvement 2"", ...],
  ""tailoredSummary"": ""professional summary tailored to this job""
}}";

        try
        {
            var response = await CallAIAsync(prompt, settings.AIAssistant.ApiKey, settings.AIAssistant.Model, settings.AIAssistant.Provider);
            if (string.IsNullOrEmpty(response))
                return null;

            var optimization = JsonSerializer.Deserialize<ResumeOptimization>(response, new JsonSerializerOptions
            { 
                PropertyNameCaseInsensitive = true 
            });

            return optimization;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate resume optimization for: {Title}", job.Title);
            return null;
        }
    }

    /// <summary>
    /// Predict application success probability based on profile match
    /// </summary>
    public async Task<ApplicationPrediction?> PredictApplicationSuccessAsync(
        JobListing job,
        Guid userId,
        List<JobListing> applicationHistory)
    {
        var settings = _settingsService.GetSettings(userId);

        if (!settings.AIAssistant.Enabled || string.IsNullOrEmpty(settings.AIAssistant.ApiKey))
            return null;

        var userSkills = string.Join(", ", settings.AIAssistant.UserSkills);
        var userExperience = settings.AIAssistant.UserExperience;

        var successfulApps = applicationHistory
            .Where(j => j.ApplicationStage == ApplicationStage.Interview || 
                       j.ApplicationStage == ApplicationStage.Offer)
            .Take(5)
            .Select(j => $"{j.Title} at {j.Company}")
            .ToList();

        var rejectedApps = applicationHistory
            .Where(j => j.ApplicationStage == ApplicationStage.Rejected || 
                       j.ApplicationStage == ApplicationStage.Ghosted)
            .Take(5)
            .Select(j => $"{j.Title} at {j.Company}")
            .ToList();

        var prompt = $@"Predict the success probability for this job application:

Job Title: {job.Title}
Company: {job.Company}
Job Requirements: {job.Description?.Substring(0, Math.Min(1000, job.Description?.Length ?? 0))}

Applicant Profile:
Skills: {userSkills}
Experience: {userExperience}

Application History:
Successful Applications: {string.Join("; ", successfulApps)}
Rejected Applications: {string.Join("; ", rejectedApps)}

Provide:
1. Success probability (0-100)
2. Brief explanation of prediction
3. 3-4 strength factors
4. 3-4 risk factors
5. 3-4 improvement suggestions
6. Recommended action (Apply Now, Improve Profile First, etc.)

Respond ONLY with valid JSON in this format:
{{
  ""successProbability"": 75,
  ""predictionReason"": ""explanation of the prediction"",
  ""strengthFactors"": [""strength 1"", ""strength 2"", ...],
  ""riskFactors"": [""risk 1"", ""risk 2"", ...],
  ""improvementSuggestions"": [""suggestion 1"", ""suggestion 2"", ...],
  ""recommendedAction"": ""action recommendation""
}}";

        try
        {
            var response = await CallAIAsync(prompt, settings.AIAssistant.ApiKey, settings.AIAssistant.Model, settings.AIAssistant.Provider);
            if (string.IsNullOrEmpty(response))
                return null;

            var prediction = JsonSerializer.Deserialize<ApplicationPrediction>(response, new JsonSerializerOptions
            { 
                PropertyNameCaseInsensitive = true 
            });

            return prediction;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to predict application success for: {Title}", job.Title);
            return null;
        }
    }

    private double CalculateSimilarityScore(JobListing source, JobListing target)
    {
        double score = 0;

        // Skills overlap (40 points max)
        var sourceSkillsLower = source.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var targetSkillsLower = target.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var commonSkills = sourceSkillsLower.Intersect(targetSkillsLower).Count();
        var totalSkills = sourceSkillsLower.Union(targetSkillsLower).Count();
        if (totalSkills > 0)
            score += (double)commonSkills / totalSkills * 40;

        // Same company (15 points)
        if (source.Company.Equals(target.Company, StringComparison.OrdinalIgnoreCase))
            score += 15;

        // Same job type (10 points)
        if (source.JobType == target.JobType)
            score += 10;

        // Same source (5 points)
        if (source.Source.Equals(target.Source, StringComparison.OrdinalIgnoreCase))
            score += 5;

        // Similar title keywords (20 points max)
        var sourceTitleWords = ExtractKeywords(source.Title);
        var targetTitleWords = ExtractKeywords(target.Title);
        var commonTitleWords = sourceTitleWords.Intersect(targetTitleWords).Count();
        var totalTitleWords = sourceTitleWords.Union(targetTitleWords).Count();
        if (totalTitleWords > 0)
            score += (double)commonTitleWords / totalTitleWords * 20;

        // Remote preference match (10 points)
        if (source.IsRemote == target.IsRemote)
            score += 10;

        return score;
    }

    private List<string> GetSimilarityReasons(JobListing source, JobListing target)
    {
        var reasons = new List<string>();

        var sourceSkillsLower = source.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var targetSkillsLower = target.Skills.Select(s => s.ToLowerInvariant()).ToHashSet();
        var commonSkills = sourceSkillsLower.Intersect(targetSkillsLower).Count();
        
        if (commonSkills > 0)
            reasons.Add($"{commonSkills} shared skill{(commonSkills > 1 ? "s" : "")}");

        if (source.Company.Equals(target.Company, StringComparison.OrdinalIgnoreCase))
            reasons.Add("Same company");

        if (source.JobType == target.JobType)
            reasons.Add($"Both {source.JobType}");

        if (source.IsRemote && target.IsRemote)
            reasons.Add("Both remote");

        var sourceTitleWords = ExtractKeywords(source.Title);
        var targetTitleWords = ExtractKeywords(target.Title);
        var commonTitleWords = sourceTitleWords.Intersect(targetTitleWords).ToList();
        if (commonTitleWords.Count > 0)
            reasons.Add($"Similar role: {string.Join(", ", commonTitleWords.Take(3))}");

        return reasons;
    }

    private List<string> ExtractKeywords(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length > 3)
            .Where(w => !CommonStopWords.Contains(w))
            .ToList();
        return words;
    }

    private async Task<string?> CallAIAsync(string prompt, string apiKey, string model, string provider)
    {
        if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
        {
            return await CallClaudeAsync(prompt, apiKey, model);
        }
        else // Default to OpenAI
        {
            return await CallOpenAIAsync(prompt, apiKey, model);
        }
    }

    private async Task<string?> CallOpenAIAsync(string prompt, string apiKey, string model)
    {
        var requestBody = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant that analyzes job descriptions and helps with job applications. Always respond with valid JSON." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 1500
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var messageContent = result
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return messageContent;
    }

    private async Task<string?> CallClaudeAsync(string prompt, string apiKey, string model)
    {
        var requestBody = new
        {
            model = model,
            max_tokens = 1500,
            temperature = 0.7,
            system = "You are a helpful assistant that analyzes job descriptions and helps with job applications. Always respond with valid JSON.",
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Claude API error: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var messageContent = result
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return messageContent;
    }

    private static readonly HashSet<string> CommonStopWords = new()
    {
        "the", "and", "for", "with", "from", "this", "that", "will", "your", "our",
        "you", "are", "have", "has", "been", "who", "what", "where", "when", "which",
        "senior", "junior", "lead", "principal"
    };
}

// Models for AI responses
public class JobAnalysis
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Responsibilities { get; set; } = new();
    public List<string> RequiredSkills { get; set; } = new();
    public List<string> Qualifications { get; set; } = new();
    public List<string> NiceToHaveSkills { get; set; } = new();
}

public class CoverLetterSuggestions
{
    public string OpeningParagraph { get; set; } = string.Empty;
    public List<string> SellingPoints { get; set; } = new();
    public string ClosingParagraph { get; set; } = string.Empty;
}

public class SkillGapAnalysis
{
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingRequiredSkills { get; set; } = new();
    public List<string> MatchedNiceToHaveSkills { get; set; } = new();
    public List<string> MissingNiceToHaveSkills { get; set; } = new();
    public int MatchPercentage { get; set; }
}

public class JobRecommendation
{
    public JobListing Job { get; set; } = null!;
    public double SimilarityScore { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class InterviewQuestions
{
    public List<string> TechnicalQuestions { get; set; } = new();
    public List<string> BehavioralQuestions { get; set; } = new();
    public List<string> CompanySpecificQuestions { get; set; } = new();
    public List<string> PreparationTips { get; set; } = new();
}

public class SalaryNegotiationTips
{
    public string MarketRateEstimate { get; set; } = string.Empty;
    public List<string> NegotiationStrategies { get; set; } = new();
    public List<string> ValuePoints { get; set; } = new();
    public string OpeningStatement { get; set; } = string.Empty;
    public string CounterOfferScript { get; set; } = string.Empty;
}

public class ResumeOptimization
{
    public List<string> SkillsToHighlight { get; set; } = new();
    public List<string> KeywordsToInclude { get; set; } = new();
    public List<string> ExperienceToEmphasize { get; set; } = new();
    public List<string> SuggestedImprovements { get; set; } = new();
    public string TailoredSummary { get; set; } = string.Empty;
}

public class ApplicationPrediction
{
    public int SuccessProbability { get; set; }
    public string PredictionReason { get; set; } = string.Empty;
    public List<string> StrengthFactors { get; set; } = new();
    public List<string> RiskFactors { get; set; } = new();
    public List<string> ImprovementSuggestions { get; set; } = new();
    public string RecommendedAction { get; set; } = string.Empty;
}
