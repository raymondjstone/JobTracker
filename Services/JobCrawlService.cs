using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobTracker.Models;

namespace JobTracker.Services;

public class CrawlResult
{
    public int JobsFound { get; set; }
    public int JobsAdded { get; set; }
    public int PagesScanned { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class JobCrawlService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JobCrawlService> _logger;
    private readonly IConfiguration _configuration;
    private const int MaxPagesPerSite = 5;
    private const int DelayBetweenRequestsMs = 2000;

    public JobCrawlService(HttpClient httpClient, ILogger<JobCrawlService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<CrawlResult> CrawlAllSitesAsync(
        JobSiteUrls siteUrls, Guid userId, JobListingService jobService, CancellationToken ct = default)
    {
        var combined = new CrawlResult();

        var tasks = new (string Name, bool Enabled, Func<JobSiteUrls, Guid, JobListingService, CancellationToken, Task<CrawlResult>> Crawl)[]
        {
            ("LinkedIn", siteUrls.CrawlLinkedIn, CrawlLinkedInAsync),
            ("S1Jobs", siteUrls.CrawlS1Jobs, CrawlS1JobsAsync),
            ("WTTJ", siteUrls.CrawlWTTJ, CrawlWttjAsync),
            ("EnergyJobSearch", siteUrls.CrawlEnergyJobSearch, CrawlEnergyJobSearchAsync),
        };

        foreach (var (name, enabled, crawl) in tasks)
        {
            if (ct.IsCancellationRequested) break;

            if (!enabled)
            {
                _logger.LogInformation("[Crawl] Skipping {Site} (disabled) for user {User}", name, userId);
                continue;
            }

            try
            {
                _logger.LogInformation("[Crawl] Starting {Site} crawl for user {User}", name, userId);
                var result = await crawl(siteUrls, userId, jobService, ct);
                combined.JobsFound += result.JobsFound;
                combined.JobsAdded += result.JobsAdded;
                combined.PagesScanned += result.PagesScanned;
                combined.Errors.AddRange(result.Errors);
                _logger.LogInformation("[Crawl] {Site} complete: {Found} found, {Added} added, {Pages} pages",
                    name, result.JobsFound, result.JobsAdded, result.PagesScanned);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var msg = $"{name}: {ex.Message}";
                combined.Errors.Add(msg);
                _logger.LogWarning(ex, "[Crawl] Error crawling {Site}", name);
            }
        }

        return combined;
    }

    // --- LinkedIn ---

    public async Task<CrawlResult> CrawlLinkedInAsync(
        JobSiteUrls siteUrls, Guid userId, JobListingService jobService, CancellationToken ct)
    {
        var result = new CrawlResult();
        var baseUrl = siteUrls.LinkedIn;

        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.Contains("linkedin.com"))
        {
            result.Errors.Add("LinkedIn: No valid search URL configured");
            return result;
        }

        // LinkedIn /jobs/collections/ and /jobs/view/ URLs require authentication and won't work
        // for server-side crawling. Convert to the public /jobs/search/ endpoint.
        var searchUrl = ConvertToLinkedInSearchUrl(baseUrl);
        _logger.LogInformation("[Crawl] LinkedIn search URL: {Url}", searchUrl);

        // LinkedIn public (unauthenticated) search always returns the same ~60 jobs
        // regardless of the &start= parameter - pagination only works when logged in.
        // So we fetch a single page only.
        ct.ThrowIfCancellationRequested();

        var html = await FetchPageAsync(searchUrl, ct);

        if (html == null)
        {
            result.Errors.Add("LinkedIn: Failed to fetch search page");
            return result;
        }

        result.PagesScanned++;
        var jobs = ParseLinkedInJobs(html);

        foreach (var job in jobs)
        {
            result.JobsFound++;
            job.UserId = userId;
            if (jobService.AddJobListing(job, userId))
                result.JobsAdded++;
        }

        return result;
    }

    /// <summary>
    /// Converts any LinkedIn jobs URL into a public /jobs/search/ URL that works without auth.
    /// URLs like /jobs/collections/remote-jobs/ or /jobs/view/12345 need to become /jobs/search/
    /// with appropriate query params preserved (keywords, location, f_WT for remote, etc.)
    /// </summary>
    private static string ConvertToLinkedInSearchUrl(string url)
    {
        // Already a public search URL - use it as-is but strip non-search params
        if (url.Contains("/jobs/search/"))
        {
            return CleanLinkedInSearchUrl(url);
        }

        try
        {
            var uri = new Uri(url);
            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var searchParams = new List<string>();

            // Carry over known search params if present
            var keywords = queryParams["keywords"];
            if (!string.IsNullOrEmpty(keywords))
                searchParams.Add($"keywords={Uri.EscapeDataString(keywords)}");

            var location = queryParams["location"];
            if (!string.IsNullOrEmpty(location))
                searchParams.Add($"location={Uri.EscapeDataString(location)}");

            // f_WT=2 means remote jobs
            var fWt = queryParams["f_WT"];
            if (!string.IsNullOrEmpty(fWt))
                searchParams.Add($"f_WT={fWt}");

            // Detect "remote" from the URL path
            if (url.Contains("remote", StringComparison.OrdinalIgnoreCase) && !searchParams.Any(p => p.StartsWith("f_WT=")))
                searchParams.Add("f_WT=2");

            var query = searchParams.Count > 0 ? "?" + string.Join("&", searchParams) : "";
            return $"https://www.linkedin.com/jobs/search/{query}";
        }
        catch
        {
            // Fallback: just use the generic search endpoint
            return "https://www.linkedin.com/jobs/search/";
        }
    }

    private static string CleanLinkedInSearchUrl(string url)
    {
        // Remove params that break public search: currentJobId, discover, discoveryOrigin, start (we add our own)
        try
        {
            var uri = new Uri(url);
            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var cleanParams = new List<string>();

            var keepParams = new[] { "keywords", "location", "f_WT", "f_JT", "f_E", "f_TPR", "geoId", "sortBy", "f_C" };
            foreach (var key in keepParams)
            {
                var val = queryParams[key];
                if (!string.IsNullOrEmpty(val))
                    cleanParams.Add($"{key}={Uri.EscapeDataString(val)}");
            }

            var query = cleanParams.Count > 0 ? "?" + string.Join("&", cleanParams) : "";
            return $"https://www.linkedin.com/jobs/search/{query}";
        }
        catch
        {
            return url;
        }
    }

    private List<JobListing> ParseLinkedInJobs(string html)
    {
        var jobs = new List<JobListing>();

        // Match each job card: <div ... class="...job-search-card..." data-entity-urn="...">
        var cardPattern = new Regex(
            @"<div[^>]*class=""[^""]*base-card[^""]*job-search-card[^""]*""[^>]*data-entity-urn=""urn:li:jobPosting:(\d+)""[^>]*>(.*?)</div>\s*</div>\s*</div>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Also try a simpler pattern if the above doesn't match
        var cardPattern2 = new Regex(
            @"<div[^>]*data-entity-urn=""urn:li:jobPosting:(\d+)""[^>]*class=""[^""]*job-search-card[^""]*""[^>]*>(.*?)</li>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var matches = cardPattern.Matches(html);
        if (matches.Count == 0)
            matches = cardPattern2.Matches(html);

        // Fallback: extract individual job entries by looking for the entity URN pattern
        if (matches.Count == 0)
        {
            // Split on each job card boundary
            var sections = Regex.Split(html, @"(?=data-entity-urn=""urn:li:jobPosting:\d+"")");
            foreach (var section in sections)
            {
                var idMatch = Regex.Match(section, @"data-entity-urn=""urn:li:jobPosting:(\d+)""");
                if (!idMatch.Success) continue;

                var job = ExtractLinkedInJobFromSection(section, idMatch.Groups[1].Value);
                if (job != null)
                    jobs.Add(job);
            }
            return jobs;
        }

        foreach (Match m in matches)
        {
            var jobId = m.Groups[1].Value;
            var content = m.Groups[2].Value;
            var job = ExtractLinkedInJobFromSection(content, jobId);
            if (job != null)
                jobs.Add(job);
        }

        return jobs;
    }

    private JobListing? ExtractLinkedInJobFromSection(string content, string jobId)
    {
        var title = ExtractText(content, @"<h3[^>]*class=""[^""]*base-search-card__title[^""]*""[^>]*>(.*?)</h3>");
        if (string.IsNullOrWhiteSpace(title))
            title = ExtractText(content, @"<span[^>]*class=""[^""]*sr-only[^""]*""[^>]*>(.*?)</span>");

        if (string.IsNullOrWhiteSpace(title)) return null;

        var company = ExtractText(content, @"<h4[^>]*class=""[^""]*base-search-card__subtitle[^""]*""[^>]*>.*?<a[^>]*>(.*?)</a>", true);
        if (string.IsNullOrWhiteSpace(company))
            company = ExtractText(content, @"<h4[^>]*class=""[^""]*base-search-card__subtitle[^""]*""[^>]*>(.*?)</h4>");

        var location = ExtractText(content, @"<span[^>]*class=""[^""]*job-search-card__location[^""]*""[^>]*>(.*?)</span>");

        var dateMatch = Regex.Match(content, @"<time[^>]*datetime=""(\d{4}-\d{2}-\d{2})""", RegexOptions.IgnoreCase);
        var datePosted = dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out var dt) ? dt : DateTime.Now;

        var urlMatch = Regex.Match(content, @"href=""(https://[^""]*linkedin\.com/jobs/view/[^""]*?)""", RegexOptions.IgnoreCase);
        var jobUrl = urlMatch.Success ? CleanUrl(urlMatch.Groups[1].Value) : $"https://www.linkedin.com/jobs/view/{jobId}/";

        // Extract salary if present
        var salary = ExtractText(content, @"<span[^>]*class=""[^""]*job-search-card__salary-info[^""]*""[^>]*>(.*?)</span>");

        return new JobListing
        {
            Title = CleanHtmlText(title),
            Company = CleanHtmlText(company ?? ""),
            Location = CleanHtmlText(location ?? ""),
            Url = jobUrl,
            DatePosted = datePosted,
            DateAdded = DateTime.Now,
            Source = "LinkedIn",
            Salary = CleanHtmlText(salary ?? ""),
        };
    }

    // --- S1Jobs ---

    public async Task<CrawlResult> CrawlS1JobsAsync(
        JobSiteUrls siteUrls, Guid userId, JobListingService jobService, CancellationToken ct)
    {
        var result = new CrawlResult();
        var baseUrl = siteUrls.S1Jobs;

        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.Contains("s1jobs.com"))
        {
            result.Errors.Add("S1Jobs: No valid search URL configured");
            return result;
        }

        for (int page = 1; page <= MaxPagesPerSite; page++)
        {
            ct.ThrowIfCancellationRequested();

            var url = page == 1 ? baseUrl : AppendQueryParam(baseUrl, "page", page.ToString());
            var html = await FetchPageAsync(url, ct);

            if (html == null)
            {
                result.Errors.Add($"S1Jobs: Failed to fetch page {page}");
                break;
            }

            result.PagesScanned++;
            var jobs = ParseS1Jobs(html);

            if (jobs.Count == 0) break;

            foreach (var job in jobs)
            {
                result.JobsFound++;
                job.UserId = userId;
                if (jobService.AddJobListing(job, userId))
                    result.JobsAdded++;
            }

            if (page < MaxPagesPerSite)
                await Task.Delay(DelayBetweenRequestsMs, ct);
        }

        return result;
    }

    private List<JobListing> ParseS1Jobs(string html)
    {
        var jobs = new List<JobListing>();

        // S1Jobs uses article elements or div.job-card patterns
        // Try splitting by article elements first
        var sections = Regex.Split(html, @"(?=<article\b)");

        foreach (var section in sections)
        {
            if (!section.StartsWith("<article", StringComparison.OrdinalIgnoreCase)) continue;

            var title = ExtractText(section, @"<a[^>]*class=""[^""]*job-title[^""]*""[^>]*>(.*?)</a>");
            if (string.IsNullOrWhiteSpace(title))
                title = ExtractText(section, @"<h\d[^>]*>(.*?)</h\d>");

            if (string.IsNullOrWhiteSpace(title)) continue;

            var urlMatch = Regex.Match(section, @"<a[^>]*href=""([^""]*?)""[^>]*class=""[^""]*job-title[^""]*""", RegexOptions.IgnoreCase);
            if (!urlMatch.Success)
                urlMatch = Regex.Match(section, @"<a[^>]*href=""(/[^""]*?/job/[^""]*?)""", RegexOptions.IgnoreCase);

            var jobUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
            if (!string.IsNullOrEmpty(jobUrl) && jobUrl.StartsWith("/"))
                jobUrl = "https://www.s1jobs.com" + jobUrl;

            var company = ExtractText(section, @"<span[^>]*class=""[^""]*company[^""]*""[^>]*>(.*?)</span>");
            if (string.IsNullOrWhiteSpace(company))
                company = ExtractText(section, @"<div[^>]*class=""[^""]*company[^""]*""[^>]*>(.*?)</div>");

            var location = ExtractText(section, @"<span[^>]*class=""[^""]*location[^""]*""[^>]*>(.*?)</span>");
            if (string.IsNullOrWhiteSpace(location))
                location = ExtractText(section, @"<div[^>]*class=""[^""]*location[^""]*""[^>]*>(.*?)</div>");

            var salary = ExtractText(section, @"<span[^>]*class=""[^""]*salary[^""]*""[^>]*>(.*?)</span>");
            if (string.IsNullOrWhiteSpace(salary))
                salary = ExtractText(section, @"<div[^>]*class=""[^""]*salary[^""]*""[^>]*>(.*?)</div>");

            jobs.Add(new JobListing
            {
                Title = CleanHtmlText(title),
                Company = CleanHtmlText(company ?? ""),
                Location = CleanHtmlText(location ?? ""),
                Salary = CleanHtmlText(salary ?? ""),
                Url = jobUrl,
                DatePosted = DateTime.Now,
                DateAdded = DateTime.Now,
                Source = "S1Jobs",
            });
        }

        // Fallback: try div-based job cards if no articles found
        if (jobs.Count == 0)
        {
            var divSections = Regex.Split(html, @"(?=<div[^>]*class=""[^""]*job-card[^""]*"")");
            foreach (var section in divSections)
            {
                if (!Regex.IsMatch(section, @"^<div[^>]*class=""[^""]*job-card")) continue;

                var title = ExtractText(section, @"<a[^>]*>(.*?)</a>");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var urlMatch = Regex.Match(section, @"<a[^>]*href=""([^""]*?)""", RegexOptions.IgnoreCase);
                var jobUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
                if (!string.IsNullOrEmpty(jobUrl) && jobUrl.StartsWith("/"))
                    jobUrl = "https://www.s1jobs.com" + jobUrl;

                var company = ExtractText(section, @"class=""[^""]*company[^""]*""[^>]*>(.*?)<");
                var location = ExtractText(section, @"class=""[^""]*location[^""]*""[^>]*>(.*?)<");
                var salary = ExtractText(section, @"class=""[^""]*salary[^""]*""[^>]*>(.*?)<");

                jobs.Add(new JobListing
                {
                    Title = CleanHtmlText(title),
                    Company = CleanHtmlText(company ?? ""),
                    Location = CleanHtmlText(location ?? ""),
                    Salary = CleanHtmlText(salary ?? ""),
                    Url = jobUrl,
                    DatePosted = DateTime.Now,
                    DateAdded = DateTime.Now,
                    Source = "S1Jobs",
                });
            }
        }

        return jobs;
    }

    // --- WTTJ ---

    public async Task<CrawlResult> CrawlWttjAsync(
        JobSiteUrls siteUrls, Guid userId, JobListingService jobService, CancellationToken ct)
    {
        var result = new CrawlResult();
        var baseUrl = siteUrls.WTTJ;

        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.Contains("welcometothejungle"))
        {
            result.Errors.Add("WTTJ: No valid search URL configured");
            return result;
        }

        // Only search URLs are supported (must contain /jobs with query params, or /en/jobs etc.)
        // URLs like app.welcometothejungle.com/jobs/XXXXX are saved job links, not searches
        var isSearchUrl = baseUrl.Contains("/jobs") &&
            (baseUrl.Contains("query=") || baseUrl.Contains("refinementList") ||
             Regex.IsMatch(baseUrl, @"welcometothejungle\.com/\w{2}/jobs\s*$") ||
             Regex.IsMatch(baseUrl, @"welcometothejungle\.com/\w{2}/jobs\?"));

        if (!isSearchUrl)
        {
            result.Errors.Add("WTTJ: URL is not a search URL. Configure a search URL like: https://www.welcometothejungle.com/en/jobs?query=.net&refinementList[offices.country_code][]=GB");
            return result;
        }

        // WTTJ is a React SPA that uses Algolia for search
        // Public read-only credentials sourced from config (defaults are WTTJ's public frontend keys)
        var algoliaAppId = _configuration["Algolia:AppId"] ?? "CSEKHVMS53";
        var algoliaApiKey = _configuration["Algolia:ApiKey"] ?? "4bd8f6215d0cc52b26430765769e65a0";
        var algoliaIndexPrefix = _configuration["Algolia:IndexPrefix"] ?? "wttj_jobs_production";

        // Extract language from URL path (e.g. /en/jobs -> "en", /fr/jobs -> "fr")
        var langMatch = Regex.Match(baseUrl, @"welcometothejungle\.com/(\w{2})/");
        var lang = langMatch.Success ? langMatch.Groups[1].Value : "en";
        var algoliaIndex = $"{algoliaIndexPrefix}_{lang}";
        var algoliaUrl = $"https://{algoliaAppId}-dsn.algolia.net/1/indexes/{algoliaIndex}/query";

        try
        {
            var (query, filters) = ParseWttjSearchUrl(baseUrl);

            // Always filter by language to avoid mixed-language results
            var langFilter = $"language:{lang}";
            filters = string.IsNullOrEmpty(filters) ? langFilter : $"{filters} AND {langFilter}";

            for (int page = 0; page < MaxPagesPerSite; page++)
            {
                ct.ThrowIfCancellationRequested();

                var requestBody = new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["hitsPerPage"] = 30,
                    ["page"] = page,
                };
                if (!string.IsNullOrEmpty(filters))
                    requestBody["filters"] = filters;

                var request = new HttpRequestMessage(HttpMethod.Post, algoliaUrl)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestBody),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Add("x-algolia-application-id", algoliaAppId);
                request.Headers.Add("x-algolia-api-key", algoliaApiKey);
                request.Headers.Add("Referer", "https://www.welcometothejungle.com/");

                string? json;
                try
                {
                    var response = await _httpClient.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Errors.Add($"WTTJ: Algolia returned {response.StatusCode} on page {page + 1}");
                        break;
                    }
                    json = await response.Content.ReadAsStringAsync(ct);
                }
                catch (HttpRequestException ex)
                {
                    result.Errors.Add($"WTTJ: {ex.Message}");
                    break;
                }

                result.PagesScanned++;
                var (jobs, nbPages) = ParseWttjAlgoliaResponse(json);

                if (jobs.Count == 0) break;

                foreach (var job in jobs)
                {
                    result.JobsFound++;
                    job.UserId = userId;
                    if (jobService.AddJobListing(job, userId))
                        result.JobsAdded++;
                }

                // Stop if we've reached the last page
                if (page + 1 >= nbPages) break;

                if (page < MaxPagesPerSite - 1)
                    await Task.Delay(DelayBetweenRequestsMs, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"WTTJ: {ex.Message}");
        }

        return result;
    }

    private static (string query, string filters) ParseWttjSearchUrl(string pageUrl)
    {
        // Parse a WTTJ search URL like:
        // https://www.welcometothejungle.com/en/jobs?query=software&refinementList%5Boffices.country_code%5D%5B%5D=GB
        // into Algolia query + filters

        var uri = new Uri(pageUrl);
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var query = queryParams["query"] ?? "";
        var filterParts = new List<string>();

        foreach (var key in queryParams.AllKeys)
        {
            if (key == null || key == "query" || key == "page") continue;

            // Parse refinementList[field][] = value format
            var refinementMatch = Regex.Match(key, @"refinementList\[([^\]]+)\]");
            if (refinementMatch.Success)
            {
                var field = refinementMatch.Groups[1].Value;
                var value = queryParams[key];
                if (!string.IsNullOrEmpty(value))
                    filterParts.Add($"{field}:{value}");
            }
        }

        return (query, string.Join(" AND ", filterParts));
    }

    private (List<JobListing> jobs, int nbPages) ParseWttjAlgoliaResponse(string json)
    {
        var jobs = new List<JobListing>();
        int nbPages = 1;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("nbPages", out var nbPagesEl))
                nbPages = nbPagesEl.GetInt32();

            if (root.TryGetProperty("hits", out var hitsArray))
            {
                foreach (var item in hitsArray.EnumerateArray())
                {
                    var job = ParseWttjJob(item);
                    if (job != null)
                        jobs.Add(job);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Crawl] WTTJ: Failed to parse Algolia response");
        }

        return (jobs, nbPages);
    }

    private JobListing? ParseWttjJob(JsonElement item)
    {
        var title = GetJsonString(item, "name");
        if (string.IsNullOrWhiteSpace(title)) return null;

        var company = "";
        var orgSlug = "";
        if (item.TryGetProperty("organization", out var org))
        {
            company = GetJsonString(org, "name") ?? "";
            orgSlug = GetJsonString(org, "slug") ?? "";
        }

        // offices is an array - take the first one
        var location = "";
        if (item.TryGetProperty("offices", out var offices) &&
            offices.ValueKind == JsonValueKind.Array && offices.GetArrayLength() > 0)
        {
            var firstOffice = offices[0];
            var city = GetJsonString(firstOffice, "city");
            var country = GetJsonString(firstOffice, "country");
            location = string.Join(", ", new[] { city, country }.Where(s => !string.IsNullOrEmpty(s)));
        }

        var slug = GetJsonString(item, "slug");
        var jobUrl = !string.IsNullOrEmpty(slug) && !string.IsNullOrEmpty(orgSlug)
            ? $"https://www.welcometothejungle.com/en/companies/{orgSlug}/jobs/{slug}"
            : "";

        // Salary fields are at root level: salary_minimum, salary_maximum, salary_currency, salary_period
        var salary = "";
        var salaryMin = GetJsonString(item, "salary_minimum");
        var salaryMax = GetJsonString(item, "salary_maximum");
        var salaryCurrency = GetJsonString(item, "salary_currency") ?? "";
        var salaryPeriod = GetJsonString(item, "salary_period") ?? "";
        if (!string.IsNullOrEmpty(salaryMin) || !string.IsNullOrEmpty(salaryMax))
        {
            salary = salaryCurrency;
            if (!string.IsNullOrEmpty(salaryMin) && !string.IsNullOrEmpty(salaryMax))
                salary += $" {salaryMin}-{salaryMax}";
            else
                salary += $" {salaryMin}{salaryMax}";
            if (!string.IsNullOrEmpty(salaryPeriod))
                salary += $" ({salaryPeriod})";
            salary = salary.Trim();
        }

        // has_remote is a boolean, remote is a string like "full", "partial", "unknown"
        var isRemote = false;
        if (item.TryGetProperty("has_remote", out var hasRemoteEl) && hasRemoteEl.ValueKind == JsonValueKind.True)
            isRemote = true;
        var remoteStr = GetJsonString(item, "remote");
        if (remoteStr != null && remoteStr.Equals("fulltime", StringComparison.OrdinalIgnoreCase))
            isRemote = true;

        var contractType = GetJsonString(item, "contract_type");
        var jobType = contractType?.ToLower() switch
        {
            "full_time" => JobType.FullTime,
            "part_time" => JobType.PartTime,
            "internship" => JobType.Internship,
            "freelance" or "contract" => JobType.Contract,
            "temporary" or "temp" => JobType.Temporary,
            _ => JobType.FullTime
        };

        var publishedAt = GetJsonString(item, "published_at");
        var datePosted = DateTime.TryParse(publishedAt, out var dt) ? dt : DateTime.Now;

        // Extract description from profile field (HTML) and summary
        var description = GetJsonString(item, "summary") ?? "";

        return new JobListing
        {
            Title = title,
            Company = company,
            Location = location,
            Url = jobUrl,
            DatePosted = datePosted,
            DateAdded = DateTime.Now,
            Source = "WTTJ",
            Salary = salary,
            IsRemote = isRemote,
            JobType = jobType,
            Description = description,
        };
    }

    // --- EnergyJobSearch ---

    public async Task<CrawlResult> CrawlEnergyJobSearchAsync(
        JobSiteUrls siteUrls, Guid userId, JobListingService jobService, CancellationToken ct)
    {
        var result = new CrawlResult();
        var baseUrl = siteUrls.EnergyJobSearch;

        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.Contains("energyjobsearch.com"))
        {
            result.Errors.Add("EnergyJobSearch: No valid search URL configured");
            return result;
        }

        // EnergyJobSearch is a React SPA - server-side HTML may not contain rendered job data.
        // Attempt to parse what we can from the HTML; fail gracefully if empty.
        for (int page = 1; page <= MaxPagesPerSite; page++)
        {
            ct.ThrowIfCancellationRequested();

            var url = page == 1 ? baseUrl : AppendQueryParam(baseUrl, "page", page.ToString());
            var html = await FetchPageAsync(url, ct);

            if (html == null)
            {
                result.Errors.Add($"EnergyJobSearch: Failed to fetch page {page}");
                break;
            }

            result.PagesScanned++;
            var jobs = ParseEnergyJobSearchJobs(html);

            if (jobs.Count == 0)
            {
                if (page == 1)
                    result.Errors.Add("EnergyJobSearch: No jobs found in HTML (React SPA - use browser extension for best results)");
                break;
            }

            foreach (var job in jobs)
            {
                result.JobsFound++;
                job.UserId = userId;
                if (jobService.AddJobListing(job, userId))
                    result.JobsAdded++;
            }

            if (page < MaxPagesPerSite)
                await Task.Delay(DelayBetweenRequestsMs, ct);
        }

        return result;
    }

    private List<JobListing> ParseEnergyJobSearchJobs(string html)
    {
        var jobs = new List<JobListing>();

        // Try JSON-LD structured data first (detail pages)
        var jsonLdMatches = Regex.Matches(html,
            @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match jsonLdMatch in jsonLdMatches)
        {
            try
            {
                var jsonText = WebUtility.HtmlDecode(jsonLdMatch.Groups[1].Value);
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                if (root.TryGetProperty("@type", out var typeEl) &&
                    typeEl.GetString() == "JobPosting")
                {
                    var title = GetJsonString(root, "title");
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var company = "";
                    if (root.TryGetProperty("hiringOrganization", out var org))
                        company = GetJsonString(org, "name") ?? "";

                    var location = "";
                    if (root.TryGetProperty("jobLocation", out var loc) &&
                        loc.TryGetProperty("address", out var addr))
                    {
                        if (addr.ValueKind == JsonValueKind.Object)
                        {
                            var parts = new List<string>();
                            var locality = GetJsonString(addr, "addressLocality");
                            var region = GetJsonString(addr, "addressRegion");
                            var country = GetJsonString(addr, "addressCountry");
                            if (!string.IsNullOrEmpty(locality)) parts.Add(locality);
                            if (!string.IsNullOrEmpty(region)) parts.Add(region);
                            if (!string.IsNullOrEmpty(country)) parts.Add(country);
                            location = string.Join(", ", parts);
                        }
                        else if (addr.ValueKind == JsonValueKind.String)
                        {
                            location = addr.GetString() ?? "";
                        }
                    }

                    var datePosted = GetJsonString(root, "datePosted");
                    var dt = DateTime.TryParse(datePosted, out var parsed) ? parsed : DateTime.Now;

                    var description = GetJsonString(root, "description") ?? "";
                    // Convert HTML description to plain text with proper formatting
                    if (!string.IsNullOrEmpty(description))
                        description = ConvertHtmlToPlainText(description);

                    // Try to extract URL from the page
                    var urlMatch = Regex.Match(html, @"<link[^>]*rel=""canonical""[^>]*href=""([^""]+)""",
                        RegexOptions.IgnoreCase);
                    var jobUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";

                    jobs.Add(new JobListing
                    {
                        Title = title,
                        Company = company,
                        Location = location,
                        Url = jobUrl,
                        DatePosted = dt,
                        DateAdded = DateTime.Now,
                        Source = "EnergyJobSearch",
                        Description = description,
                    });
                }
            }
            catch (JsonException) { }
        }

        if (jobs.Count > 0) return jobs;

        // Fallback: try to extract job links from the HTML
        // Look for links matching /jobs/{slug}/{id} pattern
        var linkPattern = new Regex(
            @"<a[^>]*href=""(/jobs/[^""]+/(\d+))""[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var seenIds = new HashSet<string>();
        foreach (Match m in linkPattern.Matches(html))
        {
            var path = m.Groups[1].Value;
            var jobId = m.Groups[2].Value;
            var linkText = CleanHtmlText(m.Groups[3].Value);

            if (seenIds.Contains(jobId) || string.IsNullOrWhiteSpace(linkText) || linkText.Length < 3)
                continue;
            seenIds.Add(jobId);

            var jobUrl = path.StartsWith("http") ? path : $"https://energyjobsearch.com{path}";

            jobs.Add(new JobListing
            {
                Title = linkText,
                Company = "",
                Location = "",
                Url = jobUrl,
                DatePosted = DateTime.Now,
                DateAdded = DateTime.Now,
                Source = "EnergyJobSearch",
            });
        }

        return jobs;
    }

    // --- Helpers ---

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "linkedin.com", "www.linkedin.com",
        "indeed.com", "www.indeed.com", "uk.indeed.com",
        "s1jobs.com", "www.s1jobs.com",
        "welcometothejungle.com", "www.welcometothejungle.com",
        "energyjobsearch.com", "www.energyjobsearch.com",
    };

    /// <summary>
    /// Validates that a URL is safe for server-side requests (SSRF protection).
    /// Requires HTTPS and an allowed host.
    /// </summary>
    internal static bool ValidateExternalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;
        var host = uri.Host;
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.ToString().StartsWith("10.") ||
                ip.ToString().StartsWith("172.") || ip.ToString().StartsWith("192.168."))
                return false;
        }
        return AllowedHosts.Contains(host) ||
               AllowedHosts.Any(allowed => host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> FetchPageAsync(string url, CancellationToken ct)
    {
        if (!ValidateExternalUrl(url))
        {
            _logger.LogWarning("[Crawl] Blocked URL (SSRF protection): {Url}", url);
            return null;
        }

        try
        {
            _logger.LogDebug("[Crawl] Fetching: {Url}", url);
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.Forbidden ||
                response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode == (HttpStatusCode)503)
            {
                _logger.LogWarning("[Crawl] Blocked ({Status}) for {Url}", response.StatusCode, url);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[Crawl] HTTP error fetching {Url}", url);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[Crawl] Timeout fetching {Url}", url);
            return null;
        }
    }

    private static string AppendQueryParam(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{key}={value}";
    }

    private static string? ExtractText(string html, string pattern, bool singleLine = false)
    {
        var options = RegexOptions.IgnoreCase;
        if (singleLine) options |= RegexOptions.Singleline;

        var match = Regex.Match(html, pattern, options);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string CleanHtmlText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Regex.Replace(text, @"<[^>]+>", ""); // Strip HTML tags
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = html;
        text = Regex.Replace(text, @"<br\s*/?>", "\n");
        text = Regex.Replace(text, @"</(?:p|div|h\d)>", "\n");
        text = Regex.Replace(text, @"<li[^>]*>", "- ");
        text = Regex.Replace(text, @"</li>", "\n");
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n ", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string CleanUrl(string url)
    {
        // Remove tracking parameters
        var idx = url.IndexOf('?');
        if (idx > 0)
        {
            var baseUrl = url[..idx];
            // Keep only essential params
            return baseUrl;
        }
        return url;
    }

    private static string? GetJsonString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val))
        {
            return val.ValueKind switch
            {
                JsonValueKind.String => val.GetString(),
                JsonValueKind.Number => val.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }
        return null;
    }
}
