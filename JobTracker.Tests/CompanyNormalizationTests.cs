using JobTracker.Services;
using Xunit;

namespace JobTracker.Tests;

public class CompanyNormalizationTests
{
    [Theory]
    [InlineData("Accenture Ltd", "Accenture")]
    [InlineData("Accenture Ltd.", "Accenture")]
    [InlineData("Accenture Limited", "Accenture")]
    [InlineData("Accenture plc", "Accenture")]
    [InlineData("Accenture PLC", "Accenture")]
    [InlineData("Google Inc", "Google")]
    [InlineData("Google Inc.", "Google")]
    [InlineData("Google Incorporated", "Google")]
    [InlineData("Microsoft Corp", "Microsoft")]
    [InlineData("Microsoft Corp.", "Microsoft")]
    [InlineData("Microsoft Corporation", "Microsoft")]
    [InlineData("Amazon LLC", "Amazon")]
    [InlineData("Amazon L.L.C", "Amazon")]
    [InlineData("SAP SE", "SAP")]
    [InlineData("Siemens AG", "Siemens")]
    [InlineData("TotalEnergies S.A", "TotalEnergies")]
    [InlineData("Shell GmbH", "Shell")]
    public void StripsSuffixes(string input, string expected)
    {
        Assert.Equal(expected, JobListingService.NormalizeCompanyName(input));
    }

    [Theory]
    [InlineData("Accenture", "Accenture")]
    [InlineData("JP Morgan", "JP Morgan")]
    [InlineData("  Accenture  ", "Accenture")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void PreservesCleanNames(string? input, string expected)
    {
        Assert.Equal(expected, JobListingService.NormalizeCompanyName(input!));
    }

    [Theory]
    [InlineData("Deloitte LLP", "Deloitte")]
    [InlineData("PwC L.L.P", "PwC")]
    public void StripsPartnershipSuffixes(string input, string expected)
    {
        Assert.Equal(expected, JobListingService.NormalizeCompanyName(input));
    }

    [Theory]
    [InlineData("Foo Group Ltd", "Foo Group")]
    [InlineData("Bar Corp.", "Bar")]
    [InlineData("Baz Limited,", "Baz")]
    public void HandlesTrailingPunctuation(string input, string expected)
    {
        Assert.Equal(expected, JobListingService.NormalizeCompanyName(input));
    }

    [Theory]
    [InlineData("Global  Solutions  Ltd", "Global Solutions")]
    public void NormalizesWhitespace(string input, string expected)
    {
        Assert.Equal(expected, JobListingService.NormalizeCompanyName(input));
    }

    [Fact]
    public void DoesNotStripSubstringsFromMiddle()
    {
        // "Ltd" in the middle of a name should NOT be stripped
        Assert.Equal("Altdorf Systems", JobListingService.NormalizeCompanyName("Altdorf Systems"));
        // "Inc" at start should not be stripped
        Assert.Equal("Incorta", JobListingService.NormalizeCompanyName("Incorta"));
        // "Corp" in middle
        Assert.Equal("CorpTech Solutions", JobListingService.NormalizeCompanyName("CorpTech Solutions"));
    }
}
