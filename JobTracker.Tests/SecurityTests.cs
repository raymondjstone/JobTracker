using Xunit;

namespace JobTracker.Tests;

public class SecurityTests
{
    // Mirrors the GetSafeReturnUrl logic from Login.razor
    private static string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/";
        if (!returnUrl.StartsWith("/") || returnUrl.StartsWith("//") || returnUrl.StartsWith("/\\"))
            return "/";
        if (returnUrl.Contains(':') || returnUrl.Contains("%2f", StringComparison.OrdinalIgnoreCase))
            return "/";
        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
            return "/";
        return returnUrl;
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("  ", "/")]
    [InlineData("/jobs", "/jobs")]
    [InlineData("/settings", "/settings")]
    [InlineData("/jobs?tab=applied", "/jobs?tab=applied")]
    public void GetSafeReturnUrl_AllowsLocalPaths(string? input, string expected)
    {
        Assert.Equal(expected, GetSafeReturnUrl(input));
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("/%2f/evil.com")]
    [InlineData("/%2F/evil.com")]
    public void GetSafeReturnUrl_BlocksMaliciousUrls(string input)
    {
        Assert.Equal("/", GetSafeReturnUrl(input));
    }
}
