using JobTracker.Services;
using Xunit;

namespace JobTracker.Tests;

public class SalaryParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullEmptyOrWhitespace_ReturnsNulls(string? input)
    {
        var (min, max) = SalaryParser.Parse(input);
        Assert.Null(min);
        Assert.Null(max);
    }

    [Theory]
    [InlineData("Competitive")]
    [InlineData("Not specified")]
    [InlineData("Salary not provided")]
    [InlineData("Negotiable")]
    public void Parse_NonNumericDescriptors_ReturnsNulls(string input)
    {
        var (min, max) = SalaryParser.Parse(input);
        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void Parse_SingleAnnualFigure_ReturnsSameMinMax()
    {
        var (min, max) = SalaryParser.Parse("£50,000");
        Assert.Equal(50000m, min);
        Assert.Equal(50000m, max);
    }

    [Fact]
    public void Parse_AnnualRange_ReturnsMinAndMax()
    {
        var (min, max) = SalaryParser.Parse("£40,000 - £60,000");
        Assert.Equal(40000m, min);
        Assert.Equal(60000m, max);
    }

    [Fact]
    public void Parse_KSuffixRange_ExpandsBothNumbers()
    {
        var (min, max) = SalaryParser.Parse("$80-90k");
        Assert.Equal(80000m, min);
        Assert.Equal(90000m, max);
    }

    [Fact]
    public void Parse_DailyRate_MultipliesBy230()
    {
        var (min, max) = SalaryParser.Parse("£500 a day");
        Assert.Equal(115000m, min);
        Assert.Equal(115000m, max);
    }

    [Fact]
    public void Parse_HourlyRate_MultipliesBy1840()
    {
        var (min, max) = SalaryParser.Parse("$50 an hour");
        Assert.Equal(92000m, min);
        Assert.Equal(92000m, max);
    }

    [Fact]
    public void Parse_MonthlyRate_MultipliesBy12()
    {
        var (min, max) = SalaryParser.Parse("€4000 per month");
        Assert.Equal(48000m, min);
        Assert.Equal(48000m, max);
    }

    [Fact]
    public void Parse_UpTo_ReturnsNullMinWithMax()
    {
        var (min, max) = SalaryParser.Parse("Up to £60,000");
        Assert.Null(min);
        Assert.Equal(60000m, max);
    }

    [Fact]
    public void Parse_From_ReturnsMinWithNullMax()
    {
        var (min, max) = SalaryParser.Parse("From £40k");
        Assert.Equal(40000m, min);
        Assert.Null(max);
    }

    [Fact]
    public void Parse_ReversedRange_NormalisesMinMax()
    {
        var (min, max) = SalaryParser.Parse("£60,000 - £40,000");
        Assert.Equal(40000m, min);
        Assert.Equal(60000m, max);
    }

    [Fact]
    public void Parse_DollarSign_ParsesCorrectly()
    {
        var (min, max) = SalaryParser.Parse("$100,000");
        Assert.Equal(100000m, min);
        Assert.Equal(100000m, max);
    }

    [Fact]
    public void Parse_KSuffixSingle_ExpandsToThousands()
    {
        var (min, max) = SalaryParser.Parse("£50k");
        Assert.Equal(50000m, min);
        Assert.Equal(50000m, max);
    }
}
