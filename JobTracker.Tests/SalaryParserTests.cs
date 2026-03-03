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
    public void Parse_DailyRate_MultipliesBy260()
    {
        var (min, max) = SalaryParser.Parse("£500 a day");
        Assert.Equal(130000m, min);
        Assert.Equal(130000m, max);
    }

    [Fact]
    public void Parse_HourlyRate_MultipliesBy2080()
    {
        var (min, max) = SalaryParser.Parse("$50 an hour");
        Assert.Equal(104000m, min);
        Assert.Equal(104000m, max);
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

    // ParseFull tests

    [Fact]
    public void ParseFull_DetectsCurrency_GBP()
    {
        var result = SalaryParser.ParseFull("£50,000");
        Assert.Equal("GBP", result.Currency);
        Assert.Equal("year", result.Period);
    }

    [Fact]
    public void ParseFull_DetectsCurrency_USD()
    {
        var result = SalaryParser.ParseFull("$80,000");
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void ParseFull_DetectsCurrency_EUR()
    {
        var result = SalaryParser.ParseFull("€60,000");
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void ParseFull_DetectsPeriod_Day()
    {
        var result = SalaryParser.ParseFull("£500 per day");
        Assert.Equal("day", result.Period);
        Assert.Equal("GBP", result.Currency);
        Assert.Equal(130000m, result.Min);
    }

    [Fact]
    public void ParseFull_DetectsPeriod_Hour()
    {
        var result = SalaryParser.ParseFull("$50 an hour");
        Assert.Equal("hour", result.Period);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void ParseFull_DetectsPeriod_Month()
    {
        var result = SalaryParser.ParseFull("€4000 per month");
        Assert.Equal("month", result.Period);
        Assert.Equal("EUR", result.Currency);
    }

    // ExtractFromText tests

    [Theory]
    [InlineData("Salary: £30,000 - £40,000 per annum", "£30,000 - £40,000 per annum")]
    [InlineData("The role pays £45,000 and offers great benefits", "£45,000")]
    [InlineData("Compensation package of $80,000-$100,000", "$80,000-$100,000")]
    [InlineData("£500 per day contract role", "£500 per day")]
    public void ExtractFromText_CurrencySymbol_Matches(string input, string expected)
    {
        var result = SalaryParser.ExtractFromText(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("GBP 30,000 - 40,000", "GBP 30,000 - 40,000")]
    [InlineData("EUR 50,000 per year", "EUR 50,000 per year")]
    [InlineData("USD 80,000-100,000", "USD 80,000-100,000")]
    public void ExtractFromText_TextCurrency_Matches(string input, string expected)
    {
        var result = SalaryParser.ExtractFromText(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Salary: 30,000 - 40,000", "30,000 - 40,000")]
    [InlineData("Pay: 50000 per year", "50000 per year")]
    public void ExtractFromText_LabelledNoCurrency_Matches(string input, string expected)
    {
        var result = SalaryParser.ExtractFromText(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("3300+ strong, €350/£300m revenue business")]
    [InlineData("generating more than $10 billion in lifetime revenue")]
    [InlineData("outbid energy generators and earn potentially >£50 in an afternoon ")]
    public void ExtractFromText_TextCurrency_Null(string input)
    {
        var result = SalaryParser.ExtractFromText(input);
        Assert.Null(result);
    }


    [Fact]
    public void ExtractFromText_SalaryInTitle_Matches()
    {
        // Real-world case: salary embedded in LinkedIn job title
        var result = SalaryParser.ExtractFromText("Software Engineer | £60,000 - £70,000");
        Assert.Equal("£60,000 - £70,000", result);
    }

    [Fact]
    public void ExtractFromText_SalaryInDescriptionHeader_Matches()
    {
        // Real-world case: description starts with title line containing salary
        var desc = "Software Engineer | £60,000 - £70,000\n\n\nWe're working with a leading enterprise SaaS platform that powers critical back-office systems used by thousands on a daily basis";
        var result = SalaryParser.ExtractFromText(desc);
        Assert.NotNull(result);
        Assert.Contains("60,000", result);
        Assert.Contains("70,000", result);
    }

    [Fact]
    public void ExtractFromText_SkipsFalsePositive_FindsRealSalaryLater()
    {
        // €350 revenue figure should be skipped, real salary found later in text
        var desc = "We are a €350m revenue business. Salary: £65,000 - £75,000 per annum";
        var result = SalaryParser.ExtractFromText(desc);
        Assert.NotNull(result);
        Assert.Contains("65,000", result);
        Assert.Contains("75,000", result);
    }

    [Fact]
    public void ExtractFromText_SkipsMultipleFalsePositives_FindsRealSalary()
    {
        // Multiple sub-threshold values before the real salary
        var desc = "3300+ strong, €350/£300m revenue business. The role pays £55,000 - £65,000";
        var result = SalaryParser.ExtractFromText(desc);
        Assert.NotNull(result);
        Assert.Contains("55,000", result);
    }

    [Fact]
    public void ExtractFromText_NoSalary_ReturnsNull()
    {
        var result = SalaryParser.ExtractFromText("We are looking for a software engineer with 5 years experience");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractFromText_YearsInDescription_ReturnsNull()
    {
        // Years like 2021, 2023, 2025 should NOT be extracted as salary
        var desc = "In 2021, a major compromise in the NPM package. By 2023, these attacks were becoming more frequent. In early 2025, we started Ossprey.";
        var result = SalaryParser.ExtractFromText(desc);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractFromText_CompetitiveSalary_ReturnsNull()
    {
        var result = SalaryParser.ExtractFromText("Comp: Competitive salary + options (depending on role/scope)");
        Assert.Null(result);
    }


    [Fact]
    public void Parse_CompetitiveSalary_ReturnsNulls()
    {
        var (min, max) = SalaryParser.Parse("Competitive salary + options");
        Assert.Null(min);
        Assert.Null(max);
    }

    // Currency conversion tests

    [Fact]
    public void Convert_USDtoGBP_AppliesRate()
    {
        var result = SalaryParser.ConvertToGBP(100000m, "USD");
        Assert.Equal(79000m, result);
    }

    [Fact]
    public void Convert_SameCurrency_NoChange()
    {
        var result = SalaryParser.Convert(100000m, "GBP", "GBP");
        Assert.Equal(100000m, result);
    }

    [Fact]
    public void Convert_EmptyCurrency_NoChange()
    {
        var result = SalaryParser.Convert(100000m, "", "GBP");
        Assert.Equal(100000m, result);
    }
}
