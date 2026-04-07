using System.Text.RegularExpressions;

namespace JobTracker.Services;

public class ParsedJobFromEmail
{
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Location { get; set; }
    public string? Salary { get; set; }
}

public class JobAlertParseResult
{
    public bool IsJobAlert { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<string> JobUrls { get; set; } = new();
    public string? Title { get; set; }
    public string? Company { get; set; }

    /// <summary>
    /// Detailed per-job info extracted from multi-job alert emails.
    /// If populated, this should be preferred over the single Title/Company fields.
    /// </summary>
    public List<ParsedJobFromEmail> Jobs { get; set; } = new();
}

public class EmailJobAlertParser
{
    // Best-effort title/company extraction (email formats vary widely and are frequently changed by senders)
    private static readonly Regex LinkedInSubjectTitleCompany = new(
        @"^(?:New jobs? for you:\s*)?(?<title>.+?)\s+at\s+(?<company>.+?)(?:\s+[\-|•]\s+LinkedIn.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Match LinkedIn job blocks: link with job ID, followed by title, company, location, salary
    // LinkedIn emails have structure like: <a href="...jobs/view/123...">Title</a> ... Company · Location ... £XX-£YY
    private static readonly Regex LinkedInJobBlockPattern = new(
        @"<a[^>]*href=""(?<url>https?://[a-z.]*linkedin\.com/(?:comm/)?jobs/view/\d+[^""]*)""[^>]*>\s*(?<title>[^<]{3,80}?)\s*</a>" +
        @"(?:[^<]*<[^>]+>)*[^<]*?" +
        @"(?<company>[A-Za-z][^<·|•\r\n]{2,60}?)\s*[·|•]\s*(?<location>[^<·|•\r\n£$€]{3,80}?)(?:\s*[·|•][^<£$€]*?)?" +
        @"(?:\s*(?<salary>[£$€][0-9,KkMm.\-–\s]+(?:/\s*(?:year|hour|hr|month|annum))?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    // Simpler fallback: just extract URL and immediate title
    private static readonly Regex LinkedInHtmlTitleCompany = new(
        @"(?is)<a[^>]*href=""https?://[a-z.]*linkedin\.com/(?:comm/)?jobs/view/\d+[^""]*""[^>]*>\s*(?<title>[^<]{3,}?)\s*</a>.*?(?:at|@)\s*(?<company>[^<]{2,}?)\s*(?:</|\(|\||\u00a0)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (string Source, string[] SenderDomains, Regex[] UrlPatterns)[] AlertSources =
    {
        ("LinkedIn", new[] { "linkedin.com", "e.linkedin.com" }, new[]
        {
            new Regex(@"https?://[a-z.]*linkedin\.com/(?:comm/)?jobs/view/\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        }),
        ("Indeed", new[] { "indeed.com", "indeedmail.com", "email.indeed.com" }, new[]
        {
            new Regex(@"https?://[a-z.]*indeed\.com/viewjob\?[^\s""<]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"https?://[a-z.]*indeed\.com/rc/clk[^\s""<]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        }),
        ("S1Jobs", new[] { "s1jobs.com" }, new[]
        {
            new Regex(@"https?://[a-z.]*s1jobs\.com/job/[^\s""<]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        }),
        ("WTTJ", new[] { "welcometothejungle.com" }, new[]
        {
            new Regex(@"https?://[a-z.]*welcometothejungle\.com/.*/jobs/[^\s""<]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        }),
        ("EnergyJobSearch", new[] { "energyjobsearch.com" }, new[]
        {
            new Regex(@"https?://[a-z.]*energyjobsearch\.com/jobs/\d+[^\s""<]*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        }),
    };

    private static readonly Regex TrackingParamsRegex = new(
        @"[?&](utm_\w+|ref|tracking|trk|refId|trackingId|click|campaign)=[^&\s""<]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public JobAlertParseResult Parse(IncomingEmail email)
    {
        var senderAddress = email.FromAddress.Trim().ToLowerInvariant();
        var senderDomain = senderAddress.Split('@').LastOrDefault() ?? "";

        foreach (var (source, domains, patterns) in AlertSources)
        {
            if (!domains.Any(d => senderDomain.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
                continue;

            // This is from a known job alert sender — extract URLs from HTML body
            var body = email.HtmlBody ?? email.TextBody ?? "";
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Matches(body))
                {
                    var url = CleanUrl(match.Value);
                    urls.Add(url);
                }
            }

            if (urls.Count == 0)
                return new JobAlertParseResult();

            string? title = null;
            string? company = null;
            var jobs = new List<ParsedJobFromEmail>();

            if (string.Equals(source, "LinkedIn", StringComparison.OrdinalIgnoreCase))
            {
                jobs = ExtractLinkedInJobs(email, urls);

                // For backwards compatibility, also set single title/company from subject or first job
                if (!string.IsNullOrWhiteSpace(email.Subject))
                {
                    var m = LinkedInSubjectTitleCompany.Match(email.Subject.Trim());
                    if (m.Success)
                    {
                        title = m.Groups["title"].Value.Trim();
                        company = m.Groups["company"].Value.Trim();
                    }
                }

                // Fallback to first job if we extracted any
                if (string.IsNullOrWhiteSpace(title) && jobs.Count > 0)
                {
                    title = jobs[0].Title;
                    company = jobs[0].Company;
                }
            }

            return new JobAlertParseResult
            {
                IsJobAlert = true,
                Source = source,
                JobUrls = urls.ToList(),
                Title = title,
                Company = company,
                Jobs = jobs
            };
        }

        return new JobAlertParseResult();
    }

    private List<ParsedJobFromEmail> ExtractLinkedInJobs(IncomingEmail email, HashSet<string> urls)
    {
        var jobs = new List<ParsedJobFromEmail>();
        var htmlBody = email.HtmlBody ?? "";

        if (string.IsNullOrWhiteSpace(htmlBody))
            return jobs;

        // Try to extract detailed job info using block pattern
        var matches = LinkedInJobBlockPattern.Matches(htmlBody);
        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in matches)
        {
            var rawUrl = m.Groups["url"].Value;
            var url = CleanUrl(rawUrl);

            url = UrlHelper.NormalizeLinkedInJobUrl(url);

            if (processedUrls.Contains(url))
                continue;
            processedUrls.Add(url);

            var job = new ParsedJobFromEmail
            {
                Url = url,
                Title = CleanExtractedText(m.Groups["title"].Value),
                Company = CleanExtractedText(m.Groups["company"].Value),
                Location = CleanExtractedText(m.Groups["location"].Value),
                Salary = CleanExtractedText(m.Groups["salary"].Value)
            };

            // Validate we got meaningful data
            if (!string.IsNullOrWhiteSpace(job.Title) && job.Title.Length >= 3)
            {
                jobs.Add(job);
            }
        }

        // For any URLs we didn't extract details for, create basic entries
        foreach (var url in urls)
        {
            var normalizedUrl = UrlHelper.NormalizeLinkedInJobUrl(url);

            if (!processedUrls.Contains(normalizedUrl) && !jobs.Any(j => j.Url.Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase)))
            {
                jobs.Add(new ParsedJobFromEmail { Url = normalizedUrl });
            }
        }

        return jobs;
    }

    private static string? CleanExtractedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Remove extra whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Remove common noise
        text = text.Trim('·', '|', '•', '-', ' ', '\t', '\r', '\n');

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string CleanUrl(string url)
    {
        // Decode HTML entities
        url = url.Replace("&amp;", "&");

        // Strip tracking parameters
        url = TrackingParamsRegex.Replace(url, "");

        // Clean up trailing ? or & if all params were stripped
        url = url.TrimEnd('?', '&');

        return url;
    }
}
