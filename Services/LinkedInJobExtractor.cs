using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobTracker.Models;

namespace JobTracker.Services;

public partial class LinkedInJobExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LinkedInJobExtractor> _logger;

    public LinkedInJobExtractor(HttpClient httpClient, ILogger<LinkedInJobExtractor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to extract job information from a LinkedIn job URL.
    /// Note: This fetches the public job posting page and parses available metadata.
    /// </summary>
    public async Task<JobExtractionResult> ExtractFromUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return JobExtractionResult.Failure("URL cannot be empty.");
        }

        if (!IsValidLinkedInJobUrl(url))
        {
            return JobExtractionResult.Failure("Please provide a valid LinkedIn job URL (e.g., https://www.linkedin.com/jobs/view/...)");
        }

        try
        {
            // Set up request with browser-like headers
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.5");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return JobExtractionResult.Failure($"Failed to fetch the job page. Status: {response.StatusCode}. LinkedIn may be blocking automated requests.");
            }

            var html = await response.Content.ReadAsStringAsync();
            var job = ParseJobFromHtml(html, url);

            if (job != null)
            {
                return JobExtractionResult.Success(job);
            }

            return JobExtractionResult.Failure("Could not extract job details from the page. The page structure may have changed or access may be restricted.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching LinkedIn job");
            return JobExtractionResult.Failure($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return JobExtractionResult.Failure("Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting LinkedIn job");
            return JobExtractionResult.Failure($"An error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses job details from text copied from LinkedIn job posting.
    /// </summary>
    public JobExtractionResult ParseFromClipboardText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return JobExtractionResult.Failure("No text provided to parse.");
        }

        try
        {
            var job = new JobListing();
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrWhiteSpace(l))
                           .ToList();

            if (lines.Count == 0)
            {
                return JobExtractionResult.Failure("No content found in the provided text.");
            }

            // First non-empty line is usually the job title
            job.Title = lines.FirstOrDefault() ?? "Unknown Position";

            // Try to find company name (usually second line or contains common patterns)
            var companyLine = lines.Skip(1).FirstOrDefault();
            if (companyLine != null)
            {
                job.Company = CleanCompanyName(companyLine);
            }

            // Look for location patterns
            var locationLine = lines.FirstOrDefault(l =>
                LocationPatternRegex().IsMatch(l) ||
                l.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Hybrid", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("On-site", StringComparison.OrdinalIgnoreCase));

            if (locationLine != null)
            {
                job.Location = locationLine;
                job.IsRemote = locationLine.Contains("Remote", StringComparison.OrdinalIgnoreCase);
            }

            // Look for job type
            job.JobType = DetectJobType(text);

            // Look for salary information
            var salaryMatch = SalaryPatternRegex().Match(text);
            if (salaryMatch.Success)
            {
                job.Salary = salaryMatch.Value;
            }

            // Look for date posted
            var dateMatch = DatePostedPatternRegex().Match(text);
            if (dateMatch.Success)
            {
                job.DatePosted = ParseRelativeDate(dateMatch.Value);
            }

            // Rest of the text is likely description
            var descriptionStartIndex = Math.Min(3, lines.Count);
            if (lines.Count > descriptionStartIndex)
            {
                job.Description = string.Join("\n", lines.Skip(descriptionStartIndex));
            }

            // Extract skills from description
            job.Skills = ExtractSkills(text);

            return JobExtractionResult.Success(job, "Job parsed from clipboard. Please review and adjust the details as needed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing clipboard text");
            return JobExtractionResult.Failure($"Error parsing text: {ex.Message}");
        }
    }

    private JobListing? ParseJobFromHtml(string html, string url)
    {
        return TryParseJobFromHtml(html, url, _logger);
    }

    /// <summary>
    /// Parses job details from LinkedIn HTML. Can be called without an instance.
    /// </summary>
    public static JobListing? TryParseJobFromHtml(string html, string url, ILogger? logger = null)
    {
        var job = new JobListing { Url = url };

        // Try to extract from JSON-LD structured data (most reliable)
        var jsonLdMatch = JsonLdPatternRegex().Match(html);
        if (jsonLdMatch.Success)
        {
            try
            {
                var jsonContent = WebUtility.HtmlDecode(jsonLdMatch.Groups[1].Value);
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("title", out var title))
                    job.Title = title.GetString() ?? "";

                if (root.TryGetProperty("hiringOrganization", out var org))
                {
                    if (org.TryGetProperty("name", out var orgName))
                        job.Company = orgName.GetString() ?? "";
                }

                // If company still empty, try extracting from page title
                if (string.IsNullOrWhiteSpace(job.Company))
                    ParseCompanyFromPageTitle(html, job);

                // Prefer company name from "Direct message the job poster from {Company}"
                // as it's more reliable than hiringOrganization for recruiter-posted jobs
                var posterCompany = ExtractPosterCompany(html);
                if (!string.IsNullOrWhiteSpace(posterCompany))
                    job.Company = posterCompany;

                if (root.TryGetProperty("jobLocation", out var location))
                {
                    if (location.TryGetProperty("address", out var address))
                    {
                        var parts = new List<string>();
                        if (address.TryGetProperty("addressLocality", out var city))
                            parts.Add(city.GetString() ?? "");
                        if (address.TryGetProperty("addressRegion", out var region))
                            parts.Add(region.GetString() ?? "");
                        if (address.TryGetProperty("addressCountry", out var country))
                            parts.Add(country.GetString() ?? "");
                        job.Location = string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
                    }
                }

                if (root.TryGetProperty("description", out var desc))
                    job.Description = StripHtml(desc.GetString() ?? "");

                // Try to get a richer description from the HTML body
                var htmlDescription = ExtractDescriptionFromHtml(html);
                if (!string.IsNullOrWhiteSpace(htmlDescription) &&
                    htmlDescription.Length > (job.Description?.Length ?? 0) + 50)
                {
                    job.Description = htmlDescription;
                }

                if (root.TryGetProperty("employmentType", out var empType))
                    job.JobType = ParseEmploymentType(empType.GetString() ?? "");

                if (root.TryGetProperty("datePosted", out var datePosted))
                {
                    if (DateTime.TryParse(datePosted.GetString(), out var parsedDate))
                        job.DatePosted = parsedDate;
                }

                if (root.TryGetProperty("baseSalary", out var salary))
                {
                    job.Salary = ExtractSalaryFromJson(salary);
                }

                job.Skills = ExtractSkills(job.Description);
                job.IsRemote = job.Location.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                              job.Description.Contains("remote", StringComparison.OrdinalIgnoreCase);

                return job;
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Failed to parse JSON-LD data");
            }
        }

        // Fallback: Try to extract from meta tags and HTML
        job.Title = ExtractMetaContent(html, "og:title") ??
                   ExtractMetaContent(html, "twitter:title") ??
                   ExtractFromPattern(html, TitlePatternRegex()) ?? "";

        // Try HTML body first (full description), fall back to meta tags (truncated)
        job.Description = ExtractDescriptionFromHtml(html) ??
                         ExtractMetaContent(html, "og:description") ??
                         ExtractMetaContent(html, "description") ?? "";

        if (!string.IsNullOrEmpty(job.Title))
        {
            ParseCompanyFromTitle(job);
        }

        // Prefer company name from "Direct message the job poster from {Company}"
        var fallbackPosterCompany = ExtractPosterCompany(html);
        if (!string.IsNullOrWhiteSpace(fallbackPosterCompany))
            job.Company = fallbackPosterCompany;

        job.Skills = ExtractSkills(job.Description);

        return string.IsNullOrEmpty(job.Title) ? null : job;
    }

    private static string? ExtractMetaContent(string html, string propertyName)
    {
        var patterns = new[]
        {
            $@"<meta\s+(?:property|name)=[""']{propertyName}[""']\s+content=[""']([^""']*)[""']",
            $@"<meta\s+content=[""']([^""']*)[""']\s+(?:property|name)=[""']{propertyName}[""']"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return WebUtility.HtmlDecode(match.Groups[1].Value);
            }
        }

        return null;
    }

    private static string? ExtractFromPattern(string html, Regex pattern)
    {
        var match = pattern.Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Extracts the job description from the HTML body (show-more-less-html section).
    /// This contains the full description, unlike JSON-LD which may be truncated.
    /// </summary>
    private static string? ExtractDescriptionFromHtml(string html)
    {
        // LinkedIn wraps the full description in show-more-less-html__markup
        var descMatch = Regex.Match(html,
            @"<div[^>]*class=""[^""]*show-more-less-html__markup[^""]*""[^>]*>(.*?)</div>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!descMatch.Success)
        {
            // Try jobs-description content area
            descMatch = Regex.Match(html,
                @"<div[^>]*class=""[^""]*jobs-description-content__text[^""]*""[^>]*>(.*?)</div>\s*</div>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        if (!descMatch.Success)
        {
            // Try the jobs-box HTML content
            descMatch = Regex.Match(html,
                @"<div[^>]*class=""[^""]*jobs-box__html-content[^""]*""[^>]*>(.*?)</div>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        if (!descMatch.Success)
            return null;

        var descHtml = descMatch.Groups[1].Value;
        var text = StripHtml(descHtml);

        // Only use if substantial (more than a short snippet)
        return text.Length > 100 ? text : null;
    }

    private static string? ExtractPosterCompany(string html)
    {
        // LinkedIn shows "Direct message the job poster from {Company}" on job pages
        var match = Regex.Match(html,
            @"(?:Direct message the job poster from|job poster from)\s+([^<""\n]+)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var company = match.Groups[1].Value.Trim();
            if (company.Length > 0 && company.Length < 200)
                return CleanCompanyName(company);
        }
        return null;
    }

    /// <summary>
    /// Extracts company and title from LinkedIn page title formats:
    /// "{Company} hiring {Title} in {Location} | LinkedIn" or "Title at Company"
    /// </summary>
    private static void ParseCompanyFromTitle(JobListing job)
    {
        var text = job.Title;

        // Remove "| LinkedIn" suffix
        var pipeIdx = text.LastIndexOf(" | ", StringComparison.Ordinal);
        if (pipeIdx > 0)
            text = text[..pipeIdx].Trim();

        // Format: "{Company} hiring {Title} in {Location}"
        var hiringMatch = Regex.Match(text, @"^(.+?)\s+hiring\s+(.+?)(?:\s+in\s+.+)?$", RegexOptions.IgnoreCase);
        if (hiringMatch.Success)
        {
            job.Company = CleanCompanyName(hiringMatch.Groups[1].Value.Trim());
            job.Title = hiringMatch.Groups[2].Value.Trim();
            return;
        }

        // Format: "Job Title at Company"
        var atIndex = text.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (atIndex > 0)
        {
            job.Company = CleanCompanyName(text[(atIndex + 4)..].Trim());
            job.Title = text[..atIndex].Trim();
        }
    }

    /// <summary>
    /// Extracts company from the HTML page title tag when not available from structured data.
    /// </summary>
    private static void ParseCompanyFromPageTitle(string html, JobListing job)
    {
        var titleText = ExtractFromPattern(html, TitlePatternRegex());
        if (string.IsNullOrWhiteSpace(titleText)) return;

        var temp = new JobListing { Title = titleText };
        ParseCompanyFromTitle(temp);
        if (!string.IsNullOrWhiteSpace(temp.Company))
            job.Company = temp.Company;
    }

    private static bool IsValidLinkedInJobUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Host.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase)) &&
               (url.Contains("/jobs/view/") || url.Contains("/jobs/") || url.Contains("/job/"));
    }

    private static string CleanCompanyName(string text)
    {
        // Remove common suffixes like follower counts, ratings
        var cleaned = FollowerCountRegex().Replace(text, "");
        cleaned = RatingRegex().Replace(cleaned, "");
        return cleaned.Trim();
    }

    private static JobType DetectJobType(string text)
    {
        var lowerText = text.ToLowerInvariant();

        if (lowerText.Contains("full-time") || lowerText.Contains("full time"))
            return JobType.FullTime;
        if (lowerText.Contains("part-time") || lowerText.Contains("part time"))
            return JobType.PartTime;
        if (lowerText.Contains("contract"))
            return JobType.Contract;
        if (lowerText.Contains("temporary") || lowerText.Contains("temp "))
            return JobType.Temporary;
        if (lowerText.Contains("internship") || lowerText.Contains("intern "))
            return JobType.Internship;
        if (lowerText.Contains("volunteer"))
            return JobType.Volunteer;

        return JobType.FullTime; // Default
    }

    private static JobType ParseEmploymentType(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "FULL_TIME" => JobType.FullTime,
            "PART_TIME" => JobType.PartTime,
            "CONTRACTOR" or "CONTRACT" => JobType.Contract,
            "TEMPORARY" => JobType.Temporary,
            "INTERN" or "INTERNSHIP" => JobType.Internship,
            "VOLUNTEER" => JobType.Volunteer,
            _ => JobType.FullTime
        };
    }

    private static DateTime ParseRelativeDate(string dateText)
    {
        var lowerText = dateText.ToLowerInvariant();
        var now = DateTime.Now;

        var numberMatch = Regex.Match(lowerText, @"(\d+)");
        var number = numberMatch.Success ? int.Parse(numberMatch.Groups[1].Value) : 1;

        if (lowerText.Contains("hour"))
            return now.AddHours(-number);
        if (lowerText.Contains("day"))
            return now.AddDays(-number);
        if (lowerText.Contains("week"))
            return now.AddDays(-number * 7);
        if (lowerText.Contains("month"))
            return now.AddMonths(-number);

        return now;
    }

    private static string ExtractSalaryFromJson(JsonElement salary)
    {
        try
        {
            var parts = new List<string>();

            if (salary.TryGetProperty("currency", out var currency))
                parts.Add(currency.GetString() ?? "");

            if (salary.TryGetProperty("value", out var value))
            {
                if (value.TryGetProperty("minValue", out var min))
                    parts.Add(min.GetDecimal().ToString("N0"));
                if (value.TryGetProperty("maxValue", out var max))
                    parts.Add("- " + max.GetDecimal().ToString("N0"));
            }

            return string.Join(" ", parts);
        }
        catch
        {
            return "";
        }
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = html;
        // Preserve block-level structure as newlines
        text = Regex.Replace(text, @"<br\s*/?>", "\n");
        text = Regex.Replace(text, @"</(?:p|div|h\d|tr)>", "\n");
        text = Regex.Replace(text, @"<li[^>]*>", "- ");
        text = Regex.Replace(text, @"</li>", "\n");
        // Strip remaining tags
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        // Normalize horizontal whitespace (preserve newlines)
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n ", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    /// <summary>
    /// Cleans LinkedIn boilerplate from job descriptions.
    /// Removes header, footer, navigation, language selector, "more jobs" section, etc.
    /// </summary>
    public static string CleanDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        var text = description;

        // Priority rule: if both "About the job" and "Set alert for similar jobs" are present,
        // extract exactly the text between them — this is the most reliable boundary pair.
        var aboutMatch = Regex.Match(text, @"About the job\s*\n", RegexOptions.IgnoreCase);
        var alertMatch = Regex.Match(text, @"\n\s*Set alert for similar jobs", RegexOptions.IgnoreCase);
        if (aboutMatch.Success && alertMatch.Success && alertMatch.Index > aboutMatch.Index + aboutMatch.Length)
        {
            text = text.Substring(aboutMatch.Index + aboutMatch.Length, alertMatch.Index - (aboutMatch.Index + aboutMatch.Length));
            // Skip directly to Step 3 cleanup since we have clean boundaries
            goto Cleanup;
        }

        // Step 1: Extract content starting from "About the job" if present
        // This removes all the header boilerplate (title, company info, people to contact, etc.)
        if (aboutMatch.Success)
        {
            text = text.Substring(aboutMatch.Index + aboutMatch.Length);
        }

        // Step 2: Find and truncate at LinkedIn UI/boilerplate markers
        // These markers indicate the end of actual job content
        var endMarkers = new[]
        {
            @"\n\s*Set alert for similar jobs",
            @"\n\s*See how you compare to other applicants",
            @"\n\s*Applicants for this job",
            @"\n\s*Exclusive Job Seeker Insights",
            @"\n\s*About the company\s*\n",
            @"\n\s*Interested in working with us",
            @"\n\s*More jobs\s*\n",
            @"\n\s*See more jobs like this",
            @"\n\s*Need to hire fast\?",
            @"\n\s*About\s*\n\s*Accessibility",
            @"\n\s*LinkedIn Corporation",
            @"\n\s*Select language\s*\n",
            @"\n\s*Questions\?\s*\n\s*Visit our Help Center",
            @"\n\s*Date Posted:\s*[A-Z][a-z]+ \d{1,2}, \d{4}",
        };

        int earliestEndIndex = text.Length;
        foreach (var marker in endMarkers)
        {
            var match = Regex.Match(text, marker, RegexOptions.IgnoreCase);
            if (match.Success && match.Index < earliestEndIndex)
            {
                earliestEndIndex = match.Index;
            }
        }

        if (earliestEndIndex < text.Length)
        {
            text = text.Substring(0, earliestEndIndex);
        }

        Cleanup:
        // Step 3: Clean up remaining artifacts
        // Remove "… more" links
        text = Regex.Replace(text, @"\s*…\s*more\s*", " ", RegexOptions.IgnoreCase);

        // Remove isolated UI button text that might remain
        text = Regex.Replace(text, @"\n\s*Easy Apply\s*\n", "\n");
        text = Regex.Replace(text, @"\n\s*Save\s*\n", "\n");
        text = Regex.Replace(text, @"\n\s*Message\s*\n", "\n");
        text = Regex.Replace(text, @"\n\s*Apply\s*\n", "\n");

        // Remove "This job is sourced from a job board. Learn More" if present at start
        text = Regex.Replace(text, @"^This job is sourced from a job board\.?\s*Learn More\.?\s*\n*", "", RegexOptions.IgnoreCase);

        // Normalize multiple newlines (more than 3) to double newlines
        text = Regex.Replace(text, @"\n{4,}", "\n\n\n");

        // Trim whitespace
        text = text.Trim();

        return text;
    }

    private static List<string> ExtractSkills(string text)
    {
        var foundSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Skills that need word boundary matching to avoid false positives
        // Format: (pattern, display name)
        var boundarySkills = new (string pattern, string name)[]
        {
            // Short/ambiguous language names
            (@"\bGo\b(?:lang)?", "Go"),
            (@"\bRust\b", "Rust"),
            (@"\bR\b(?:\s+programming|\s+language)?", "R"),
            (@"\bC\b(?![\+#\w])", "C"),  // Match standalone C only, not C++ or C#
            (@"\bC\+\+\b", "C++"),
            (@"\bC#\b|C\s*Sharp", "C#"),
            (@"\bF#\b", "F#"),
            (@"\bSwift\b", "Swift"),
            (@"\bDart\b", "Dart"),
            (@"\bScala\b", "Scala"),
            (@"\bPerl\b", "Perl"),
            (@"\bLua\b", "Lua"),
            (@"\bJulia\b", "Julia"),
            (@"\bAI\b", "AI"),
            (@"\bML\b", "Machine Learning"),
            (@"\bNLP\b", "NLP"),
            (@"\bETL\b", "ETL"),
            (@"\bOOP\b", "OOP"),
            (@"\bTDD\b", "TDD"),
            (@"\bBDD\b", "BDD"),
            (@"\bDDD\b", "DDD"),
            (@"\bSQL\b", "SQL"),
            (@"\bNoSQL\b", "NoSQL"),
            (@"\bGit\b(?!Hub)", "Git"),  // Git but not GitHub (handled separately)
            (@"\bGitHub\b", "GitHub"),
            (@"\bGitLab\b", "GitLab"),
            (@"\bJava\b(?!Script)", "Java"),  // Java but not JavaScript
            (@"\bVue(?:\.?js)?\b", "Vue.js"),
            (@"\bNode(?:\.?js|JS)?\b", "Node.js"),
            (@"\bNext(?:\.?js|JS)\b", "Next.js"),
            (@"\bNuxt(?:\.?js|JS)?\b", "Nuxt.js"),
            (@"\bExpress(?:\.?js|JS)?\b", "Express.js"),
            (@"\bNest(?:\.?js|JS)\b", "NestJS"),
            (@"\bDeno\b", "Deno"),
            (@"\bBun\b", "Bun"),
            (@"\.NET(?:\s*Core|\s*Framework|\s*\d+)?", ".NET"),
            (@"\bASP\.NET(?:\s*Core|\s*MVC|\s*Web\s*API)?", "ASP.NET"),
            (@"\bWPF\b", "WPF"),
            (@"\bWinForms\b|Windows\s+Forms", "WinForms"),
            (@"\bMAUI\b", "MAUI"),
            (@"\bXamarin\b", "Xamarin"),
            (@"\bAWS\b|Amazon\s+Web\s+Services", "AWS"),
            (@"\bGCP\b|Google\s+Cloud(?:\s+Platform)?", "GCP"),
            (@"\bAzure\b", "Azure"),
            (@"\bK8s\b|Kubernetes", "Kubernetes"),
            (@"\bEKS\b", "EKS"),
            (@"\bAKS\b", "AKS"),
            (@"\bGKE\b", "GKE"),
            (@"\bHelm\b", "Helm"),
            (@"\bIstio\b", "Istio"),
            (@"\bCI\s*/\s*CD\b|CI/CD", "CI/CD"),
            (@"\bREST\b(?:\s*API)?|RESTful", "REST API"),
            (@"\bSOAP\b", "SOAP"),
            (@"\bgRPC\b", "gRPC"),
            (@"\bAPI\b(?:\s+Gateway)?", "API"),
            (@"\bOAuth\b", "OAuth"),
            (@"\bJWT\b", "JWT"),
            (@"\bSSO\b", "SSO"),
            (@"\bSAML\b", "SAML"),
        };

        // Skills that are unambiguous and can use simple contains matching
        var simpleSkills = new[]
        {
            // Languages
            "JavaScript", "TypeScript", "Python", "Ruby", "PHP", "Kotlin", "Groovy",
            "Objective-C", "COBOL", "Fortran", "Haskell", "Erlang", "Elixir", "Clojure",

            // Frontend
            "React", "Angular", "Svelte", "jQuery", "Ember", "Backbone",
            "HTML", "CSS", "SASS", "SCSS", "LESS", "Tailwind", "Bootstrap",
            "Material UI", "Chakra UI", "Styled Components", "Webpack", "Vite", "Rollup",
            "Redux", "MobX", "Zustand", "Recoil",

            // Backend/Frameworks
            "Blazor", "Django", "Flask", "FastAPI", "Spring Boot", "Spring Framework",
            "Ruby on Rails", "Laravel", "Symfony", "CodeIgniter",
            "Entity Framework", "Dapper", "Hibernate", "Sequelize", "Prisma",
            "SignalR", "WebSockets",

            // Databases
            "PostgreSQL", "MySQL", "MariaDB", "Oracle", "SQL Server", "SQLite",
            "MongoDB", "DynamoDB", "Cassandra", "CouchDB", "Firebase",
            "Redis", "Memcached", "Elasticsearch", "OpenSearch", "Solr",
            "Neo4j", "GraphQL", "Hasura",

            // Cloud/DevOps
            "Docker", "Podman", "Terraform", "Ansible", "Puppet", "Chef",
            "Jenkins", "CircleCI", "Travis CI", "GitHub Actions", "Azure DevOps",
            "ArgoCD", "Tekton", "Spinnaker",
            "Prometheus", "Grafana", "Datadog", "New Relic", "Splunk", "ELK Stack",
            "CloudFormation", "Pulumi", "Serverless",
            "Lambda", "Azure Functions", "Cloud Functions",

            // Messaging/Streaming
            "Kafka", "RabbitMQ", "ActiveMQ", "Amazon SQS", "Azure Service Bus",
            "Apache Spark", "Apache Flink", "Apache Airflow", "Celery",

            // Mobile
            "React Native", "Flutter", "Ionic", "Cordova", "SwiftUI", "Jetpack Compose",
            "Android", "iOS",

            // Testing
            "Jest", "Mocha", "Cypress", "Playwright", "Selenium", "Puppeteer",
            "xUnit", "NUnit", "MSTest", "JUnit", "PyTest", "RSpec",
            "Postman", "SoapUI",

            // Data/ML
            "Machine Learning", "Deep Learning", "TensorFlow", "PyTorch", "Keras",
            "Scikit-learn", "Pandas", "NumPy", "Jupyter", "Apache Hadoop",
            "Power BI", "Tableau", "Looker", "dbt",

            // Architecture/Practices
            "Microservices", "Monolith", "Event-Driven", "CQRS", "Event Sourcing",
            "Domain-Driven Design", "Clean Architecture", "Hexagonal Architecture",
            "Agile", "Scrum", "Kanban", "SAFe", "DevOps", "SRE", "Platform Engineering",
            "LINQ",

            // OS/Infrastructure
            "Linux", "Ubuntu", "CentOS", "RHEL", "Debian", "Windows Server",
            "Nginx", "Apache", "IIS", "HAProxy", "Traefik",

            // Security
            "OWASP", "Penetration Testing", "Security Scanning", "Vault",

            // Version Control
            "Bitbucket", "Mercurial", "SVN",
        };

        // Check boundary-matched skills
        foreach (var (pattern, name) in boundarySkills)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                foundSkills.Add(name);
            }
        }

        // Check simple contains skills
        foreach (var skill in simpleSkills)
        {
            if (text.Contains(skill, StringComparison.OrdinalIgnoreCase))
            {
                foundSkills.Add(skill);
            }
        }

        // Special handling: If we found "C" alone but also found C++ or C#, remove standalone C
        if (foundSkills.Contains("C") && (foundSkills.Contains("C++") || foundSkills.Contains("C#")))
        {
            // Only keep C if it's explicitly mentioned as "C programming" or "C language"
            if (!Regex.IsMatch(text, @"\bC\b\s+(?:programming|language|developer)", RegexOptions.IgnoreCase))
            {
                foundSkills.Remove("C");
            }
        }

        return foundSkills.Take(15).ToList();
    }

    [GeneratedRegex(@"<script[^>]*type=[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdPatternRegex();

    [GeneratedRegex(@"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePatternRegex();

    [GeneratedRegex(@"\$[\d,]+(?:\s*-\s*\$[\d,]+)?(?:\s*(?:per|/)\s*(?:year|hour|month|yr|hr))?", RegexOptions.IgnoreCase)]
    private static partial Regex SalaryPatternRegex();

    [GeneratedRegex(@"(?:posted\s+)?(\d+\s+(?:hour|day|week|month)s?\s+ago|today|yesterday)", RegexOptions.IgnoreCase)]
    private static partial Regex DatePostedPatternRegex();

    [GeneratedRegex(@"[,\s]*\d+[\d,]*\s*followers?", RegexOptions.IgnoreCase)]
    private static partial Regex FollowerCountRegex();

    [GeneratedRegex(@"[,\s]*[\d.]+\s*(?:out of \d+\s*)?stars?", RegexOptions.IgnoreCase)]
    private static partial Regex RatingRegex();

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:,\s*[A-Z]{2})?\b|\b(?:Remote|Hybrid|On-site)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LocationPatternRegex();
}

public class JobExtractionResult
{
    public bool IsSuccess { get; private set; }
    public JobListing? Job { get; private set; }
    public string Message { get; private set; } = "";

    public static JobExtractionResult Success(JobListing job, string? message = null)
    {
        return new JobExtractionResult
        {
            IsSuccess = true,
            Job = job,
            Message = message ?? "Job extracted successfully."
        };
    }

    public static JobExtractionResult Failure(string message)
    {
        return new JobExtractionResult
        {
            IsSuccess = false,
            Message = message
        };
    }
}
