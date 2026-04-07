namespace JobTracker.Services;

public static class UrlHelper
{
    /// <summary>
    /// Normalizes a LinkedIn URL by converting comm/jobs links to standard jobs links
    /// and trimming trailing dots.
    /// </summary>
    public static string NormalizeLinkedInJobUrl(string url)
    {
        var normalized = url.Replace(
            "linkedin.com/comm/jobs/view/",
            "linkedin.com/jobs/view/",
            StringComparison.OrdinalIgnoreCase);
        return normalized.TrimEnd('.');
    }
}
