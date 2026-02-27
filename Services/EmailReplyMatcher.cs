using System.Text.RegularExpressions;
using JobTracker.Models;

namespace JobTracker.Services;

public class ReplyMatchResult
{
    public bool Matched { get; set; }
    public Guid JobId { get; set; }
    public ApplicationStage SuggestedStage { get; set; }
    public string MatchReason { get; set; } = string.Empty;
}

public class EmailReplyMatcher
{
    // Keywords must be specific enough to avoid false positives from marketing emails.
    // Ordered by priority — first match wins.
    private static readonly (Regex Pattern, ApplicationStage Stage)[] StagePatterns =
    {
        // Offer — require job-specific phrasing, not bare "offer"
        (new Regex(@"pleased to offer you", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Offer),
        (new Regex(@"extend(ing)? (you )?(a |an )?offer", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Offer),
        (new Regex(@"(formal|job|employment) offer", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Offer),
        (new Regex(@"compensation package", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Offer),
        (new Regex(@"offer (letter|of employment)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Offer),

        // Interview
        (new Regex(@"schedule (a |an )?interview", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Interview),
        (new Regex(@"invite(d)? (you )?(to |for )(a |an )?interview", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Interview),
        (new Regex(@"meet the team", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Interview),
        (new Regex(@"interview (slot|time|date|availability)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Interview),
        (new Regex(@"(phone|video|teams|zoom) (screen|call|interview)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Interview),

        // Tech test
        (new Regex(@"technical (test|assessment|challenge)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.TechTest),
        (new Regex(@"coding (challenge|test|assessment)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.TechTest),
        (new Regex(@"take-home (test|assignment|exercise)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.TechTest),
        (new Regex(@"hackerrank|codility|leetcode|codesignal", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.TechTest),

        // Rejected
        (new Regex(@"(unfortunately|regret).{0,40}(not (been )?successful|unable to (proceed|move forward))", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Rejected),
        (new Regex(@"(decided to )?proceed with other candidates", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Rejected),
        (new Regex(@"position has been filled", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Rejected),
        (new Regex(@"not (progressing|moving forward) with your application", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Rejected),

        // Pending — only when currently Applied
        (new Regex(@"received your application", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Pending),
        (new Regex(@"thank you for (applying|your application)", RegexOptions.IgnoreCase | RegexOptions.Compiled), ApplicationStage.Pending),
    };

    // Stage progression order — higher index = further along
    private static readonly Dictionary<ApplicationStage, int> StageOrder = new()
    {
        [ApplicationStage.None] = 0,
        [ApplicationStage.Applied] = 1,
        [ApplicationStage.NoReply] = 2,
        [ApplicationStage.Pending] = 3,
        [ApplicationStage.TechTest] = 4,
        [ApplicationStage.Interview] = 5,
        [ApplicationStage.Offer] = 6,
        [ApplicationStage.Ghosted] = 2,
        [ApplicationStage.Rejected] = -1,
    };

    // Common multi-part TLDs (country-code second-level domains)
    private static readonly HashSet<string> SecondLevelTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "net.uk",
        "co.au", "com.au", "org.au", "net.au",
        "co.nz", "org.nz", "net.nz",
        "co.za", "org.za", "net.za",
        "co.in", "org.in", "net.in",
        "co.jp", "or.jp", "ne.jp",
        "co.kr", "or.kr",
        "com.br", "org.br", "net.br",
        "com.cn", "org.cn", "net.cn",
        "com.mx", "org.mx",
        "co.il", "org.il",
        "com.sg", "org.sg",
        "co.id", "or.id",
        "com.my", "org.my",
        "com.ph", "org.ph",
        "co.th", "or.th",
        "com.tr", "org.tr",
        "com.ar", "org.ar",
        "com.pl", "org.pl",
        "co.de",
    };

    // Known transactional/marketing senders that are never job-related
    private static readonly HashSet<string> IgnoredSenderDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Payment / financial
        "paypal.com", "paypal.co.uk", "paypal.de", "paypal.fr",
        "stripe.com", "revolut.com", "wise.com", "monzo.com",
        "chase.com", "barclays.co.uk", "hsbc.co.uk", "natwest.com",
        "lloydsbank.co.uk", "santander.co.uk", "bankofamerica.com",
        // Retail / ecommerce
        "amazon.com", "amazon.co.uk", "amazon.de",
        "ebay.com", "ebay.co.uk",
        // Social / marketing
        "facebook.com", "facebookmail.com", "twitter.com", "x.com",
        "instagram.com", "tiktok.com", "pinterest.com", "reddit.com",
        // Services / utilities
        "google.com", "apple.com", "microsoft.com",
        "netflix.com", "spotify.com", "uber.com",
        "dropbox.com", "slack.com", "zoom.us",
        // Newsletters / marketing platforms
        "mailchimp.com", "sendgrid.net", "constantcontact.com",
        "hubspot.com", "salesforce.com",
    };

    // Generic email providers — never match by domain
    private static readonly HashSet<string> GenericEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "outlook.com", "hotmail.com", "yahoo.com", "live.com",
        "icloud.com", "mail.com", "protonmail.com", "aol.com",
        "btinternet.com", "virginmedia.com", "sky.com", "talktalk.net",
    };

    public ReplyMatchResult Match(IncomingEmail email, List<JobListing> jobs, List<Contact> contacts)
    {
        var senderAddress = email.FromAddress.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(senderAddress))
            return new ReplyMatchResult();

        var senderDomain = senderAddress.Split('@').LastOrDefault() ?? "";

        // Skip known non-job senders immediately
        if (IsIgnoredSender(senderDomain))
            return new ReplyMatchResult();

        // Try to match by contact email first (high confidence)
        var matchedJob = FindJobByContactEmail(senderAddress, jobs, contacts);

        // Fall back to matching by company domain (lower confidence)
        if (matchedJob == null)
            matchedJob = FindJobByCompanyDomain(senderDomain, jobs);

        if (matchedJob == null)
            return new ReplyMatchResult();

        // Scan subject + body for stage keywords
        var textToScan = $"{email.Subject} {email.TextBody ?? ""} {StripHtml(email.HtmlBody ?? "")}".ToLowerInvariant();
        var suggestedStage = DetectStage(textToScan, matchedJob.ApplicationStage);

        if (suggestedStage == ApplicationStage.None)
            return new ReplyMatchResult();

        // Stage progression guard
        if (!ShouldAdvance(matchedJob.ApplicationStage, suggestedStage))
            return new ReplyMatchResult();

        return new ReplyMatchResult
        {
            Matched = true,
            JobId = matchedJob.Id,
            SuggestedStage = suggestedStage,
            MatchReason = $"Email from {senderAddress} matched job '{matchedJob.Title}' at {matchedJob.Company}"
        };
    }

    private static bool IsIgnoredSender(string senderDomain)
    {
        if (IgnoredSenderDomains.Contains(senderDomain))
            return true;

        // Also check base domain for subdomains (e.g., "email.paypal.co.uk")
        var baseDomain = ExtractBaseDomain(senderDomain);
        return baseDomain != senderDomain && IgnoredSenderDomains.Contains(baseDomain);
    }

    private static JobListing? FindJobByContactEmail(string senderAddress, List<JobListing> jobs, List<Contact> contacts)
    {
        // Check inline contacts on jobs
        foreach (var job in jobs)
        {
            if (job.Contacts?.Any(c => string.Equals(c.Email?.Trim(), senderAddress, StringComparison.OrdinalIgnoreCase)) == true)
                return job;
        }

        // Check Contact entities linked to jobs
        var matchedContact = contacts.FirstOrDefault(c =>
            string.Equals(c.Email?.Trim(), senderAddress, StringComparison.OrdinalIgnoreCase));

        if (matchedContact != null)
        {
            // Find a job linked to this contact — for now, return the first applied job
            // (contact-job linking would need the storage backend to resolve properly)
            return null;
        }

        return null;
    }

    private static JobListing? FindJobByCompanyDomain(string senderDomain, List<JobListing> jobs)
    {
        if (string.IsNullOrEmpty(senderDomain))
            return null;

        if (GenericEmailDomains.Contains(senderDomain))
            return null;

        var baseDomain = ExtractBaseDomain(senderDomain);
        var domainName = ExtractDomainName(baseDomain);

        // Domain name must be at least 3 chars to avoid false matches ("co", "uk", etc.)
        if (domainName.Length < 3)
            return null;

        // Find applied jobs where the company name or URL matches the sender domain
        var appliedJobs = jobs
            .Where(j => j.HasApplied && !j.IsArchived)
            .OrderByDescending(j => j.DateApplied)
            .ToList();

        foreach (var job in appliedJobs)
        {
            // Check if company URL contains the base domain
            if (!string.IsNullOrWhiteSpace(job.Url) &&
                job.Url.Contains(baseDomain, StringComparison.OrdinalIgnoreCase))
                return job;

            // Check if company name closely matches the domain name
            var companyNormalized = job.Company.ToLowerInvariant()
                .Replace(" ", "").Replace("-", "").Replace(".", "");

            // Require strong match: domain name must equal or be contained in company name
            // but NOT the reverse (avoids "co" matching "company")
            if (!string.IsNullOrEmpty(companyNormalized) && companyNormalized.Contains(domainName))
                return job;
        }

        return null;
    }

    /// <summary>
    /// Extract the registrable base domain, handling multi-part TLDs like .co.uk
    /// e.g., "email.paypal.co.uk" → "paypal.co.uk", "jobs.company.com" → "company.com"
    /// </summary>
    private static string ExtractBaseDomain(string domain)
    {
        var parts = domain.Split('.');
        if (parts.Length <= 2)
            return domain;

        // Check for known multi-part TLDs
        var lastTwo = $"{parts[^2]}.{parts[^1]}";
        if (SecondLevelTlds.Contains(lastTwo) && parts.Length >= 3)
            return $"{parts[^3]}.{lastTwo}";

        return $"{parts[^2]}.{parts[^1]}";
    }

    /// <summary>
    /// Extract just the domain name portion (e.g., "paypal.co.uk" → "paypal", "company.com" → "company")
    /// </summary>
    private static string ExtractDomainName(string baseDomain)
    {
        return baseDomain.Split('.')[0].ToLowerInvariant();
    }

    private static ApplicationStage DetectStage(string text, ApplicationStage currentStage)
    {
        foreach (var (pattern, stage) in StagePatterns)
        {
            if (pattern.IsMatch(text))
            {
                // "Pending" only applies if currently at Applied
                if (stage == ApplicationStage.Pending && currentStage != ApplicationStage.Applied)
                    continue;

                return stage;
            }
        }

        return ApplicationStage.None;
    }

    private static bool ShouldAdvance(ApplicationStage current, ApplicationStage suggested)
    {
        // Rejected can apply from any stage
        if (suggested == ApplicationStage.Rejected)
            return current != ApplicationStage.Rejected;

        var currentOrder = StageOrder.GetValueOrDefault(current, 0);
        var suggestedOrder = StageOrder.GetValueOrDefault(suggested, 0);

        // Only advance forward
        return suggestedOrder > currentOrder;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        return Regex.Replace(html, "<[^>]+>", " ");
    }
}
