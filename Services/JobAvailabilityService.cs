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

public class ContactEnrichmentProgress
{
    public int TotalContacts { get; set; }
    public int CheckedCount { get; set; }
    public int EnrichedCount { get; set; }
    public string? CurrentContactName { get; set; }
    public bool IsComplete { get; set; }
    public bool WasCancelled { get; set; }
}

public class ContactProfileDetails
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Headline { get; set; }
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

        // SSRF protection: validate URL before making requests
        if (!ValidateExternalUrl(job.Url))
        {
            _logger.LogWarning("Blocked URL (SSRF protection): {Url}", job.Url);
            return new JobAvailabilityCheckResult { Result = JobAvailabilityResult.Skipped };
        }

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
                    var parsed = LinkedInJobExtractor.TryParseJobFromHtml(html, job.Url, _logger);
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

                // Parse job details from WTTJ page HTML (JSON-LD)
                if (html.Length > 0)
                {
                    var parsed = TryParseWttjFromHtml(html, job.Url);
                    return new JobAvailabilityCheckResult
                    {
                        Result = JobAvailabilityResult.Available,
                        ParsedJob = parsed
                    };
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

    private HttpRequestMessage CreateBrowserRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.5");
        return request;
    }

    /// <summary>
    /// Builds the LinkedIn contact-info overlay URL from a profile URL.
    /// e.g. https://www.linkedin.com/in/johndoe -> https://www.linkedin.com/in/johndoe/overlay/contact-info/
    /// </summary>
    internal static string? BuildContactInfoOverlayUrl(string profileUrl)
    {
        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri))
            return null;
        var host = uri.Host.ToLowerInvariant();
        if (!host.Contains("linkedin.com"))
            return null;
        // Only works for /in/ profile URLs
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.StartsWith("/in/", StringComparison.OrdinalIgnoreCase))
            return null;
        return $"{uri.Scheme}://{uri.Host}{path}/overlay/contact-info/";
    }

    /// <summary>
    /// Fetches a single contact's profile page and contact-info overlay to update their details.
    /// Returns true if the contact was updated.
    /// </summary>
    public async Task<bool> EnrichContactAsync(
        Contact contact,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var profileUrl = contact.ProfileUrl;
        if (string.IsNullOrWhiteSpace(profileUrl))
            return false;

        if (!ValidateExternalUrl(profileUrl))
        {
            _logger.LogWarning("Blocked profile URL (SSRF protection): {Url}", profileUrl);
            return false;
        }

        // 1. Fetch main profile page (for headline from <title>)
        var response = await _httpClient.SendAsync(CreateBrowserRequest(profileUrl), cancellationToken);
        var profileHtml = await response.Content.ReadAsStringAsync(cancellationToken);

        var details = new ContactProfileDetails();

        // Extract headline from main profile page
        if (profileHtml.Length > 0)
        {
            var headlineDetails = ExtractHeadlineFromProfile(profileHtml, contact.Name);
            details.Headline = headlineDetails;
        }

        // 2. Fetch the contact-info overlay page (for email/phone)
        var overlayUrl = BuildContactInfoOverlayUrl(profileUrl);
        if (overlayUrl != null)
        {
            _logger.LogDebug("Fetching contact info overlay: {Url}", overlayUrl);
            try
            {
                var overlayResponse = await _httpClient.SendAsync(CreateBrowserRequest(overlayUrl), cancellationToken);
                var overlayHtml = await overlayResponse.Content.ReadAsStringAsync(cancellationToken);

                if (overlayHtml.Length > 0)
                {
                    var contactInfo = ExtractContactInfoFromOverlay(overlayHtml);
                    details.Email ??= contactInfo.Email;
                    details.Phone ??= contactInfo.Phone;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug(ex, "Could not fetch contact info overlay for {Name}", contact.Name);
            }
        }

        // 3. If overlay didn't yield results, try the main profile page as fallback
        if (profileHtml.Length > 0 && (details.Email == null || details.Phone == null))
        {
            var fallback = ExtractContactDetailsFromProfile(profileHtml, contact.Name);
            details.Email ??= fallback.Email;
            details.Phone ??= fallback.Phone;
        }

        // 4. Apply extracted details to the contact
        bool updated = false;

        if (!string.IsNullOrWhiteSpace(details.Email) &&
            (overwrite || string.IsNullOrWhiteSpace(contact.Email)))
        {
            if (contact.Email != details.Email)
            {
                contact.Email = details.Email;
                updated = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(details.Phone) &&
            (overwrite || string.IsNullOrWhiteSpace(contact.Phone)))
        {
            if (contact.Phone != details.Phone)
            {
                contact.Phone = details.Phone;
                updated = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(details.Headline) &&
            (overwrite || string.IsNullOrWhiteSpace(contact.Role) || contact.Role == "Recruiter"))
        {
            if (overwrite)
            {
                if (contact.Role != details.Headline)
                {
                    contact.Role = details.Headline;
                    updated = true;
                }
            }
            else if (contact.Role == "Recruiter" && !details.Headline.Contains("Recruiter", StringComparison.OrdinalIgnoreCase))
            {
                contact.Role = details.Headline;
                updated = true;
            }
            else if (string.IsNullOrWhiteSpace(contact.Role))
            {
                contact.Role = details.Headline;
                updated = true;
            }
        }

        if (updated)
        {
            _logger.LogInformation("Enriched contact {Name}: Email={Email}, Phone={Phone}, Headline={Headline}",
                contact.Name, details.Email, details.Phone, details.Headline);
        }

        return updated;
    }

    /// <summary>
    /// Extracts the headline/title from a LinkedIn profile page's title tag.
    /// </summary>
    internal static string? ExtractHeadlineFromProfile(string html, string contactName)
    {
        var titleMatch = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>(.*?)</title>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!titleMatch.Success)
            return null;

        var title = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
        var pipeIdx = title.IndexOf('|');
        if (pipeIdx > 0)
            title = title.Substring(0, pipeIdx).Trim();
        var dashIdx = title.IndexOf(" - ");
        if (dashIdx <= 0)
            return null;

        // Validate the title belongs to this contact
        var namePart = title.Substring(0, dashIdx).Trim();
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            var nameParts = contactName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!nameParts.Any(part => namePart.Contains(part, StringComparison.OrdinalIgnoreCase)))
                return null;
        }

        var headline = title.Substring(dashIdx + 3).Trim();
        return !string.IsNullOrWhiteSpace(headline) && headline.Length < 100 ? headline : null;
    }

    /// <summary>
    /// Extracts email and phone from the LinkedIn contact-info overlay page.
    /// This page is much simpler and purpose-built — contact details appear as
    /// mailto: links, tel: links, and in ci-email/ci-phone sections.
    /// </summary>
    internal static ContactProfileDetails ExtractContactInfoFromOverlay(string html)
    {
        var details = new ContactProfileDetails();

        // Strip scripts/styles
        var clean = System.Text.RegularExpressions.Regex.Replace(html,
            @"<script[^>]*>[\s\S]*?</script>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        clean = System.Text.RegularExpressions.Regex.Replace(clean,
            @"<style[^>]*>[\s\S]*?</style>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Email: look for mailto: links first (strongest signal on overlay page)
        var mailtoRegex = new System.Text.RegularExpressions.Regex(
            @"href=""mailto:([a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,})""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var mailtoMatch = mailtoRegex.Match(clean);
        if (mailtoMatch.Success)
        {
            var email = mailtoMatch.Groups[1].Value;
            if (!email.Contains("@linkedin.com", StringComparison.OrdinalIgnoreCase) &&
                !email.Contains("@licdn.com", StringComparison.OrdinalIgnoreCase))
            {
                details.Email = email;
            }
        }

        // Email fallback: look in ci-email sections
        if (details.Email == null)
        {
            var ciEmailMatch = System.Text.RegularExpressions.Regex.Match(clean,
                @"ci-email[^>]*>([\s\S]*?)</",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (ciEmailMatch.Success)
            {
                var emailRegex = new System.Text.RegularExpressions.Regex(
                    @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}");
                var emailMatch = emailRegex.Match(ciEmailMatch.Groups[1].Value);
                if (emailMatch.Success)
                    details.Email = emailMatch.Value;
            }
        }

        // Email fallback: any email in the overlay that isn't a LinkedIn system address
        if (details.Email == null)
        {
            var emailRegex = new System.Text.RegularExpressions.Regex(
                @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}");
            var systemDomains = new[] { "linkedin.com", "licdn.com", "w3.org", "schema.org" };
            foreach (System.Text.RegularExpressions.Match m in emailRegex.Matches(clean))
            {
                var email = m.Value;
                var domain = email.Substring(email.IndexOf('@') + 1).ToLowerInvariant();
                if (!systemDomains.Any(d => domain.Contains(d)))
                {
                    details.Email = email;
                    break;
                }
            }
        }

        // Phone: look for tel: links first
        var telRegex = new System.Text.RegularExpressions.Regex(
            @"href=""tel:([\+\d\s\-\(\)]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var telMatch = telRegex.Match(clean);
        if (telMatch.Success)
        {
            var phone = telMatch.Groups[1].Value.Trim();
            if (phone.Count(char.IsDigit) >= 7)
                details.Phone = phone;
        }

        // Phone fallback: ci-phone sections
        if (details.Phone == null)
        {
            var ciPhoneMatch = System.Text.RegularExpressions.Regex.Match(clean,
                @"ci-phone[^>]*>([\s\S]*?)</",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (ciPhoneMatch.Success)
            {
                var phoneRegex = new System.Text.RegularExpressions.Regex(
                    @"\+?[\d\s\-\(\)]{7,20}");
                var phoneMatch = phoneRegex.Match(ciPhoneMatch.Groups[1].Value);
                if (phoneMatch.Success && phoneMatch.Value.Count(char.IsDigit) >= 7)
                    details.Phone = phoneMatch.Value.Trim();
            }
        }

        // Phone fallback: phone near keyword in overlay
        if (details.Phone == null)
        {
            var phoneContextRegex = new System.Text.RegularExpressions.Regex(
                @"(?:phone|mobile|cell|tel|telephone)[\s:.\-]*(\+?[\d\s\-\(\)]{7,20})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var phoneContextMatch = phoneContextRegex.Match(clean);
            if (phoneContextMatch.Success && phoneContextMatch.Groups[1].Value.Count(char.IsDigit) >= 7)
                details.Phone = phoneContextMatch.Groups[1].Value.Trim();
        }

        return details;
    }

    public async Task EnrichContactsAsync(
        IReadOnlyList<Contact> contacts,
        Action<Contact> saveContact,
        bool overwrite = false,
        Action<ContactEnrichmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var progress = new ContactEnrichmentProgress
        {
            TotalContacts = contacts.Count
        };

        foreach (var contact in contacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress.CurrentContactName = contact.Name;
            onProgress?.Invoke(progress);

            try
            {
                var enriched = await EnrichContactAsync(contact, overwrite, cancellationToken);
                if (enriched)
                {
                    saveContact(contact);
                    progress.EnrichedCount++;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HTTP error enriching contact: {Name}", contact.Name);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout enriching contact: {Name}", contact.Name);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error enriching contact: {Name}", contact.Name);
            }

            progress.CheckedCount++;
            onProgress?.Invoke(progress);

            // Rate-limit: 5-second delay between LinkedIn requests
            if (progress.CheckedCount < contacts.Count)
            {
                await Task.Delay(5000, cancellationToken);
            }
        }

        progress.IsComplete = true;
        progress.CurrentContactName = null;
        onProgress?.Invoke(progress);
    }

    /// <summary>
    /// Fallback extraction of email/phone from the main LinkedIn profile page HTML.
    /// Used when the contact-info overlay doesn't yield results.
    /// </summary>
    internal static ContactProfileDetails ExtractContactDetailsFromProfile(string html, string contactName = "")
    {
        var details = new ContactProfileDetails();

        // --- Extract profile-specific content sections only ---
        // Strip all <script> and <style> blocks to avoid matching JS/CSS content
        var cleanHtml = System.Text.RegularExpressions.Regex.Replace(html,
            @"<script[^>]*>[\s\S]*?</script>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleanHtml = System.Text.RegularExpressions.Regex.Replace(cleanHtml,
            @"<style[^>]*>[\s\S]*?</style>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Try to isolate LinkedIn profile content sections where contact info actually appears.
        // These are the "About" section and the "Contact info" overlay content.
        var profileSections = new List<string>();

        // About section: <section ... class="...pv-about-section..."> or data-section="summary"
        var aboutMatches = System.Text.RegularExpressions.Regex.Matches(cleanHtml,
            @"<section[^>]*(?:pv-about|summary|about)[^>]*>([\s\S]*?)</section>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in aboutMatches)
            profileSections.Add(m.Groups[1].Value);

        // Contact info section (LinkedIn puts this in a specific section/div)
        var contactInfoMatches = System.Text.RegularExpressions.Regex.Matches(cleanHtml,
            @"<section[^>]*(?:contact-info|pv-contact-info)[^>]*>([\s\S]*?)</section>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in contactInfoMatches)
            profileSections.Add(m.Groups[1].Value);

        // Also look for ci-email / ci-phone sections (LinkedIn contact info overlay)
        var ciMatches = System.Text.RegularExpressions.Regex.Matches(cleanHtml,
            @"<div[^>]*(?:ci-email|ci-phone|ci-vanity-url)[^>]*>([\s\S]*?)</div>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in ciMatches)
            profileSections.Add(m.Groups[1].Value);

        // LinkedIn public profile sometimes renders contact info as plain text in the top card area
        var topCardMatches = System.Text.RegularExpressions.Regex.Matches(cleanHtml,
            @"<div[^>]*(?:top-card|profile-header|pv-top-card)[^>]*>([\s\S]*?)</div>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in topCardMatches)
            profileSections.Add(m.Groups[1].Value);

        // Use the isolated sections if we found any, otherwise fall back to script-stripped HTML
        // but with much stricter filtering
        var searchText = profileSections.Count > 0
            ? string.Join(" ", profileSections)
            : cleanHtml;
        bool usingFullPage = profileSections.Count == 0;

        // --- Email extraction ---
        var emailRegex = new System.Text.RegularExpressions.Regex(
            @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}");

        var excludedEmailDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "linkedin.com", "licdn.com", "sentry.io", "w3.org", "schema.org",
            "googletagmanager.com", "google.com", "facebook.com", "meta.com",
            "apple.com", "microsoft.com", "cloudflare.com", "amazonaws.com",
            "gstatic.com", "doubleclick.net", "googlesyndication.com",
            "twitter.com", "x.com", "github.com", "example.com", "example.org",
            "test.com", "localhost"
        };
        var excludedEmailPrefixes = new[] { "noreply", "no-reply", "donotreply", "support", "info@cdn", "admin" };
        var assetExtensions = new[] { ".png", ".jpg", ".jpeg", ".svg", ".gif", ".css", ".js", ".woff" };

        var candidateEmails = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in emailRegex.Matches(searchText))
        {
            var email = match.Value;
            var domain = email.Substring(email.IndexOf('@') + 1).ToLowerInvariant();
            var localPart = email.Substring(0, email.IndexOf('@')).ToLowerInvariant();

            if (excludedEmailDomains.Contains(domain))
                continue;
            if (excludedEmailDomains.Any(d => domain.EndsWith("." + d)))
                continue;
            if (excludedEmailPrefixes.Any(p => localPart.StartsWith(p)))
                continue;
            if (assetExtensions.Any(ext => email.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                continue;

            // When using full-page fallback, require the email to look personal
            // (contain part of the contact's name, or be in a mailto: link)
            if (usingFullPage && !string.IsNullOrWhiteSpace(contactName))
            {
                var nameParts = contactName.ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => p.Length >= 3) // skip short name fragments
                    .ToArray();
                var emailLower = email.ToLowerInvariant();
                bool nameInEmail = nameParts.Any(part => emailLower.Contains(part));

                // Also check if it appears in a mailto: link (strong signal)
                bool inMailtoLink = searchText.Contains($"mailto:{email}", StringComparison.OrdinalIgnoreCase);

                if (!nameInEmail && !inMailtoLink)
                    continue;
            }

            candidateEmails.Add(email);
        }
        if (candidateEmails.Count > 0)
            details.Email = candidateEmails[0];

        // --- Phone extraction ---
        // Only look for phone numbers that appear in a recognisable context:
        // within href="tel:..." links, or near phone-related keywords
        var telLinkRegex = new System.Text.RegularExpressions.Regex(
            @"href=""tel:([\+\d\s\-\(\)]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var telMatch = telLinkRegex.Match(searchText);
        if (telMatch.Success)
        {
            var phone = telMatch.Groups[1].Value.Trim();
            var digitCount = phone.Count(char.IsDigit);
            if (digitCount >= 10 && digitCount <= 15)
                details.Phone = phone;
        }

        // If no tel: link found, look for phone numbers near phone-related keywords
        if (details.Phone == null)
        {
            var phoneContextRegex = new System.Text.RegularExpressions.Regex(
                @"(?:phone|mobile|cell|tel|telephone|call)[\s:.\-]*(\+?[\d\s\-\(\)]{10,20})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var phoneContextMatch = phoneContextRegex.Match(searchText);
            if (phoneContextMatch.Success)
            {
                var phone = phoneContextMatch.Groups[1].Value.Trim();
                var digitCount = phone.Count(char.IsDigit);
                if (digitCount >= 10 && digitCount <= 15)
                    details.Phone = phone;
            }
        }

        return details;
    }

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
    /// Parses job details from a WTTJ (Welcome to the Jungle) job detail page HTML using JSON-LD.
    /// </summary>
    public static JobListing? TryParseWttjFromHtml(string html, string url)
    {
        var job = new JobListing { Url = url, Source = "WTTJ" };

        // WTTJ pages include JSON-LD JobPosting structured data
        var jsonLdMatches = System.Text.RegularExpressions.Regex.Matches(html,
            @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match jsonLdMatch in jsonLdMatches)
        {
            try
            {
                var jsonText = System.Net.WebUtility.HtmlDecode(jsonLdMatch.Groups[1].Value);
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                if (!root.TryGetProperty("@type", out var typeEl) ||
                    typeEl.GetString() != "JobPosting")
                    continue;

                if (root.TryGetProperty("title", out var titleEl))
                    job.Title = titleEl.GetString() ?? "";

                if (root.TryGetProperty("hiringOrganization", out var org) &&
                    org.TryGetProperty("name", out var orgName))
                    job.Company = orgName.GetString() ?? "";

                // jobLocation can be an array or single object
                if (root.TryGetProperty("jobLocation", out var loc))
                {
                    System.Text.Json.JsonElement addrEl;
                    if (loc.ValueKind == System.Text.Json.JsonValueKind.Array && loc.GetArrayLength() > 0)
                        addrEl = loc[0];
                    else
                        addrEl = loc;

                    if (addrEl.TryGetProperty("address", out var addr) &&
                        addr.ValueKind == System.Text.Json.JsonValueKind.Object)
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
                }

                if (root.TryGetProperty("description", out var descEl))
                {
                    var desc = descEl.GetString() ?? "";
                    desc = ConvertHtmlToPlainText(desc);
                    job.Description = desc;
                }

                if (root.TryGetProperty("industry", out var industryEl))
                {
                    var industry = industryEl.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(industry))
                        job.Skills = industry.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                }

                break; // Found JobPosting, done
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(job.Title) && string.IsNullOrWhiteSpace(job.Description))
            return null;

        job.IsRemote = job.Location?.Contains("Remote", StringComparison.OrdinalIgnoreCase) == true ||
                      job.Description?.Contains("remote", StringComparison.OrdinalIgnoreCase) == true;

        return job;
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
