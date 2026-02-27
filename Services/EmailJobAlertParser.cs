using System.Text.RegularExpressions;

namespace JobTracker.Services;

public class JobAlertParseResult
{
    public bool IsJobAlert { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<string> JobUrls { get; set; } = new();
}

public class EmailJobAlertParser
{
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

            return new JobAlertParseResult
            {
                IsJobAlert = true,
                Source = source,
                JobUrls = urls.ToList()
            };
        }

        return new JobAlertParseResult();
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
