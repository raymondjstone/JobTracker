using System.Net;
using JobTracker.Models;

namespace JobTracker.Services;

public enum JobAvailabilityResult
{
    Available,
    Unavailable,
    Error,
    Skipped
}

public class JobAvailabilityCheckResult
{
    public JobAvailabilityResult Result { get; set; }
    public string? Reason { get; set; }
    public JobListing? ParsedJob { get; set; }
}

public class JobAvailabilityProgress
{
    public int TotalJobs { get; set; }
    public int CheckedCount { get; set; }
    public int MarkedUnsuitableCount { get; set; }
    public int ErrorCount { get; set; }
    public int SkippedCount { get; set; }
    public string CurrentJobTitle { get; set; } = "";
    public string? LastError { get; set; }
    public bool IsComplete { get; set; }
    public bool WasCancelled { get; set; }
}

public class JobAvailabilityService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JobAvailabilityService> _logger;

    public JobAvailabilityService(HttpClient httpClient, ILogger<JobAvailabilityService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<JobAvailabilityCheckResult> CheckJobAvailabilityAsync(
        JobListing job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.Url))
            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Skipped };

        var source = (job.Source ?? "").ToLowerInvariant();

        // Indeed blocks all automated requests via Cloudflare (403 for both active and removed jobs)
        // so we cannot determine availability - skip entirely
        if (source == "indeed")
            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Skipped };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.5");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == HttpStatusCode.Gone)
            {
                var reason = $"Job page not found (HTTP {(int)response.StatusCode})";
                _logger.LogInformation("Job unavailable (HTTP {Status}): {Title} - {Url}",
                    (int)response.StatusCode, job.Title, job.Url);
                return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Unavailable, Reason = reason };
            }

            // LinkedIn returns 999 or 403 for automated requests but still includes page content.
            // Always read the body for LinkedIn to check for "No longer accepting applications".
            if (source == "linkedin")
            {
                // LinkedIn redirects expired jobs to search results with ?trk=expired_jd_redirect
                // or to a URL that no longer contains the original job ID.
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                if (finalUrl.Contains("expired_jd_redirect", StringComparison.OrdinalIgnoreCase) ||
                    (finalUrl != job.Url && !finalUrl.Contains("/jobs/view/", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("LinkedIn job redirected to {FinalUrl} (expired): {Title} - {Url}",
                        finalUrl, job.Title, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Unavailable,
                        Reason = "LinkedIn: Job expired (redirected away from listing)"
                    };
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("LinkedIn fetch for {Title}: HTTP {Status}, HTML length {Length}, URL: {Url}",
                    job.Title, (int)response.StatusCode, html.Length, job.Url);

                // Check text indicators for unavailability
                var unavailableIndicators = new[]
                {
                    "No longer accepting applications",
                    "This job is no longer available",
                    "This job has been closed",
                    "Job has expired",
                    "This position has been filled",
                    "No longer available"
                };
                foreach (var indicator in unavailableIndicators)
                {
                    if (html.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("LinkedIn job unavailable ({Indicator}): {Title} - {Url}",
                            indicator, job.Title, job.Url);
                        return new JobAvailabilityCheckResult
                        {
                            Result = JobAvailabilityResult.Unavailable,
                            Reason = $"LinkedIn: {indicator}"
                        };
                    }
                }

                // Check JSON-LD for expired job (validThrough date in the past, or jobLocationType closed)
                var jsonLdExpired = CheckJsonLdForExpiredJob(html);
                if (jsonLdExpired != null)
                {
                    _logger.LogInformation("LinkedIn job expired via JSON-LD ({Reason}): {Title} - {Url}",
                        jsonLdExpired, job.Title, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Unavailable,
                        Reason = $"LinkedIn: {jsonLdExpired}"
                    };
                }

                // If the page has job content (description section) but no Apply button,
                // the job is no longer accepting applications. LinkedIn's public page
                // removes the Apply/Easy Apply button when a job closes.
                var hasJobContent = html.Contains("show-more-less-html", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("jobs-description", StringComparison.OrdinalIgnoreCase);
                var hasApplyButton = html.Contains("applyUrl", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("Easy Apply", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("apply-button", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("jobs-apply-button", StringComparison.OrdinalIgnoreCase);
                if (hasJobContent && !hasApplyButton)
                {
                    _logger.LogInformation("LinkedIn job has content but no Apply button (closed): {Title} - {Url}",
                        job.Title, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Unavailable,
                        Reason = "LinkedIn: No Apply button found (job closed)"
                    };
                }

                // If we got content back (even with non-200), the job page exists
                // Try to parse job details to update stored data if better
                if (html.Length > 0)
                {
                    var parsed = LinkedInJobExtractor.TryParseJobFromHtml(html, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Available,
                        ParsedJob = parsed
                    };
                }
            }

            // WTTJ shows various messages on expired/closed listings
            if (source == "wttj")
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var wttjIndicators = new[]
                {
                    "No longer accepting applications",
                    "This job is no longer available",
                    "This job has been closed",
                    "Job has expired",
                    "This position has been filled",
                    "No longer available",
                    "Job no longer available"
                };
                foreach (var indicator in wttjIndicators)
                {
                    if (html.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("WTTJ job unavailable ({Indicator}): {Title} - {Url}",
                            indicator, job.Title, job.Url);
                        return new JobAvailabilityCheckResult
                        {
                            Result = JobAvailabilityResult.Unavailable,
                            Reason = $"WTTJ: {indicator}"
                        };
                    }
                }
            }

            // S1Jobs expired/closed listings
            if (source == "s1jobs")
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var s1Indicators = new[]
                {
                    "No longer accepting applications",
                    "This job is no longer available",
                    "This job has been closed",
                    "Job has expired",
                    "This position has been filled",
                    "No longer available",
                    "This vacancy has expired"
                };
                foreach (var indicator in s1Indicators)
                {
                    if (html.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("S1Jobs job unavailable ({Indicator}): {Title} - {Url}",
                            indicator, job.Title, job.Url);
                        return new JobAvailabilityCheckResult
                        {
                            Result = JobAvailabilityResult.Unavailable,
                            Reason = $"S1Jobs: {indicator}"
                        };
                    }
                }

                // Parse job details from S1Jobs page HTML
                if (html.Length > 0)
                {
                    var parsed = TryParseS1JobsFromHtml(html, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Available,
                        ParsedJob = parsed
                    };
                }
            }

            // EnergyJobSearch expired/closed listings
            if (source == "energyjobsearch")
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var ejsIndicators = new[]
                {
                    "This job is no longer available",
                    "Job has expired",
                    "No longer accepting applications",
                    "This position has been filled",
                    "No longer available"
                    // Note: "Page not found" excluded - React SPA bundles this string in i18n messages on every page
                };
                foreach (var indicator in ejsIndicators)
                {
                    if (html.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("EnergyJobSearch job unavailable ({Indicator}): {Title} - {Url}",
                            indicator, job.Title, job.Url);
                        return new JobAvailabilityCheckResult
                        {
                            Result = JobAvailabilityResult.Unavailable,
                            Reason = $"EnergyJobSearch: {indicator}"
                        };
                    }
                }

                // Check JSON-LD for expiry (validThrough)
                var jsonLdExpired = CheckJsonLdForExpiredJob(html);
                if (jsonLdExpired != null)
                {
                    _logger.LogInformation("EnergyJobSearch job expired via JSON-LD ({Reason}): {Title} - {Url}",
                        jsonLdExpired, job.Title, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Unavailable,
                        Reason = $"EnergyJobSearch: {jsonLdExpired}"
                    };
                }

                // Parse job details if available
                if (html.Length > 0)
                {
                    var parsed = TryParseEnergyJobSearchFromHtml(html, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Available,
                        ParsedJob = parsed
                    };
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Unexpected HTTP status {Status} for job: {Title} - {Url}",
                    (int)response.StatusCode, job.Title, job.Url);
                return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Error };
            }

            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Available };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error checking job availability: {Title}", job.Title);
            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Error };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout checking job availability: {Title}", job.Title);
            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Error };
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking job availability: {Title}", job.Title);
            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Error };
        }
    }

    public async Task ScanJobsAsync(
        IReadOnlyList<JobListing> jobs,
        Action<Guid, string> markUnsuitableAction,
        Action<Guid>? markCheckedAction = null,
        Action<Guid, JobListing>? updateJobAction = null,
        Action<JobAvailabilityProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var progress = new JobAvailabilityProgress
        {
            TotalJobs = jobs.Count
        };

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress.CurrentJobTitle = $"{job.Title} ({job.Company})";
            onProgress?.Invoke(progress);

            var check = await CheckJobAvailabilityAsync(job, cancellationToken);

            switch (check.Result)
            {
                case JobAvailabilityResult.Unavailable:
                    markUnsuitableAction(job.Id, check.Reason ?? "Job no longer available");
                    progress.MarkedUnsuitableCount++;
                    break;
                case JobAvailabilityResult.Available:
                    if (check.ParsedJob != null)
                    {
                        updateJobAction?.Invoke(job.Id, check.ParsedJob);
                    }
                    markCheckedAction?.Invoke(job.Id);
                    break;
                case JobAvailabilityResult.Error:
                    progress.ErrorCount++;
                    progress.LastError = $"Error checking: {job.Title}";
                    break;
                case JobAvailabilityResult.Skipped:
                    progress.SkippedCount++;
                    break;
            }

            progress.CheckedCount++;
            onProgress?.Invoke(progress);

            if (check.Result != JobAvailabilityResult.Skipped)
            {
                var source = (job.Source ?? "").ToLowerInvariant();
                var delayMs = source == "linkedin" ? 5000 : 1000;
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        progress.IsComplete = true;
        progress.CurrentJobTitle = "";
        onProgress?.Invoke(progress);
    }

    /// <summary>
    /// Checks JSON-LD structured data in LinkedIn HTML for signs the job has expired.
    /// Returns a reason string if expired, null if still active.
    /// </summary>
    private string? CheckJsonLdForExpiredJob(string html)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(html,
                @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (!match.Success) return null;

            var json = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check validThrough date - if in the past, job has expired
            if (root.TryGetProperty("validThrough", out var validThrough))
            {
                if (DateTime.TryParse(validThrough.GetString(), out var expiryDate))
                {
                    if (expiryDate < DateTime.UtcNow)
                    {
                        return $"Job expired on {expiryDate:yyyy-MM-dd}";
                    }
                }
            }

            // Check for jobPostingStatus or status fields
            if (root.TryGetProperty("status", out var status))
            {
                var statusStr = status.GetString() ?? "";
                if (statusStr.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("expired", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Job status: {statusStr}";
                }
            }

            // Check if description is empty/missing - sign of removed listing
            if (root.TryGetProperty("description", out var desc))
            {
                var descStr = desc.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(descStr) || descStr.Length < 20)
                {
                    return "Job listing has no description (likely removed)";
                }
            }
            else
            {
                // No description property at all in JSON-LD
                return "Job listing has no description (likely removed)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing JSON-LD for expiry check");
        }

        return null;
    }

    /// <summary>
    /// Parses job details from an S1Jobs job detail page HTML.
    /// </summary>
    public static JobListing? TryParseS1JobsFromHtml(string html, string url)
    {
        var job = new JobListing { Url = url, Source = "S1Jobs" };

        // Title: <h1 class="jobDetails__title job-title">...</h1>
        var titleMatch = System.Text.RegularExpressions.Regex.Match(html,
            @"class=""[^""]*job-title[^""]*""[^>]*>([^<]+)<",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (titleMatch.Success)
            job.Title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();

        // Company + Location: <p class="jobDetails__location job-location-company">Company, <span>Location</span></p>
        var compLocMatch = System.Text.RegularExpressions.Regex.Match(html,
            @"class=""jobDetails__location\s+job-location-company""[^>]*>(.*?)</p>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (compLocMatch.Success)
        {
            var raw = compLocMatch.Groups[1].Value;

            // Company is the text before the <span>
            var companyMatch = System.Text.RegularExpressions.Regex.Match(raw, @"^([^<]+)");
            if (companyMatch.Success)
                job.Company = System.Net.WebUtility.HtmlDecode(companyMatch.Groups[1].Value).Trim().TrimEnd(',').Trim();

            // Location is inside the <span>
            var locationMatch = System.Text.RegularExpressions.Regex.Match(raw,
                @"<span[^>]*>([^<]*(?:<[^>]*>[^<]*)*)</span>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (locationMatch.Success)
            {
                var loc = System.Text.RegularExpressions.Regex.Replace(locationMatch.Groups[1].Value, @"<[^>]+>", "");
                job.Location = System.Net.WebUtility.HtmlDecode(loc).Trim();
            }
        }

        // Salary: <p class="jobDetails__customSalaryInfo job-salary">£80000</p>
        var salaryMatch = System.Text.RegularExpressions.Regex.Match(html,
            @"class=""[^""]*job-salary[^""]*""[^>]*>(.*?)</p>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (salaryMatch.Success)
        {
            var salary = System.Text.RegularExpressions.Regex.Replace(salaryMatch.Groups[1].Value, @"<[^>]+>", "");
            salary = System.Net.WebUtility.HtmlDecode(salary).Trim();
            salary = System.Text.RegularExpressions.Regex.Replace(salary, @"\s+", " ");
            if (!string.IsNullOrWhiteSpace(salary))
                job.Salary = salary;
        }

        // Description: <div class="jobDescription">...</div> (greedy to end of section)
        var descMatch = System.Text.RegularExpressions.Regex.Match(html,
            @"<div\s+class=""jobDescription"">(.*?)</div>\s*(?:<div\s+class=""(?:mt|youtube|companyInfo|content-wrapper)|$)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (descMatch.Success)
        {
            var desc = descMatch.Groups[1].Value;
            // Strip HTML tags but preserve newlines for block elements
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"<br\s*/?>", "\n");
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"</(?:p|div|li|h\d)>", "\n");
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"<li[^>]*>", "- ");
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"<[^>]+>", "");
            desc = System.Net.WebUtility.HtmlDecode(desc);
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"[ \t]+", " ");
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"\n{3,}", "\n\n");
            desc = desc.Trim();
            if (!string.IsNullOrWhiteSpace(desc))
                job.Description = desc;
        }

        // Only return if we got at least a title or description
        if (string.IsNullOrWhiteSpace(job.Title) && string.IsNullOrWhiteSpace(job.Description))
            return null;

        return job;
    }

    /// <summary>
    /// Parses job details from an EnergyJobSearch job detail page HTML using JSON-LD structured data.
    /// </summary>
    public static JobListing? TryParseEnergyJobSearchFromHtml(string html, string url)
    {
        var job = new JobListing { Url = url, Source = "EnergyJobSearch" };

        // Try JSON-LD first (most reliable for this site)
        var jsonLdMatch = System.Text.RegularExpressions.Regex.Match(html,
            @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (jsonLdMatch.Success)
        {
            try
            {
                var jsonText = System.Net.WebUtility.HtmlDecode(jsonLdMatch.Groups[1].Value);
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                if (root.TryGetProperty("@type", out var typeEl) &&
                    typeEl.GetString() == "JobPosting")
                {
                    if (root.TryGetProperty("title", out var titleEl))
                        job.Title = titleEl.GetString() ?? "";

                    if (root.TryGetProperty("hiringOrganization", out var org) &&
                        org.TryGetProperty("name", out var orgName))
                        job.Company = orgName.GetString() ?? "";

                    if (root.TryGetProperty("jobLocation", out var loc) &&
                        loc.TryGetProperty("address", out var addr))
                    {
                        if (addr.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            var parts = new List<string>();
                            if (addr.TryGetProperty("addressLocality", out var locality))
                                parts.Add(locality.GetString() ?? "");
                            if (addr.TryGetProperty("addressRegion", out var region))
                                parts.Add(region.GetString() ?? "");
                            if (addr.TryGetProperty("addressCountry", out var country))
                                parts.Add(country.GetString() ?? "");
                            job.Location = string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
                        }
                        else if (addr.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            job.Location = addr.GetString() ?? "";
                        }
                    }

                    if (root.TryGetProperty("description", out var descEl))
                    {
                        var desc = descEl.GetString() ?? "";
                        desc = ConvertHtmlToPlainText(desc);
                        job.Description = desc;
                    }
                }
            }
            catch { }
        }

        // Fallback: try DOM-based extraction
        if (string.IsNullOrWhiteSpace(job.Title))
        {
            var titleMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"<h1[^>]*>([^<]+)</h1>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (titleMatch.Success)
                job.Title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
        }

        if (string.IsNullOrWhiteSpace(job.Title) && string.IsNullOrWhiteSpace(job.Description))
            return null;

        return job;
    }

    /// <summary>
    /// Converts HTML to plain text with proper newlines for block elements and list items.
    /// </summary>
    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = html;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"</(?:p|div|h\d)>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<li[^>]*>", "- ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"</li>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n ", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
