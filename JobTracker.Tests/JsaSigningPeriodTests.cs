using JobTracker.Models;
using Xunit;

namespace JobTracker.Tests;

public class JsaSigningPeriodTests
{
    [Fact]
    public void GetCurrentPeriod_WithValidSettings_ReturnsCorrectPeriod()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        // Test date: Jan 20, 2025 (19 days after start = in 2nd period)
        var testDate = new DateTime(2025, 1, 20);
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        Assert.Equal(new DateTime(2025, 1, 15), from); // Start of 2nd period
        Assert.Equal(new DateTime(2025, 1, 28), to);   // End of 2nd period
    }

    [Fact]
    public void GetCurrentPeriod_OnFirstDayOfPeriod_ReturnsCorrectPeriod()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        var testDate = new DateTime(2025, 1, 15); // First day of 2nd period
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        Assert.Equal(new DateTime(2025, 1, 15), from);
        Assert.Equal(new DateTime(2025, 1, 28), to);
    }

    [Fact]
    public void GetCurrentPeriod_OnLastDayOfPeriod_ReturnsCorrectPeriod()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        var testDate = new DateTime(2025, 1, 14); // Last day of 1st period
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        Assert.Equal(new DateTime(2025, 1, 1), from);
        Assert.Equal(new DateTime(2025, 1, 14), to);
    }

    [Fact]
    public void GetCurrentPeriod_BeforeStartDate_ReturnsFallback()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 2, 1),
            PeriodLengthDays = 14
        };

        var testDate = new DateTime(2025, 1, 15); // Before start date
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        // Should return fallback: last 14 days
        Assert.Equal(testDate.AddDays(-14), from);
        Assert.Equal(testDate, to);
    }

    [Fact]
    public void GetCurrentPeriod_NoStartDate_ReturnsFallback()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = null,
            PeriodLengthDays = 14
        };

        var testDate = DateTime.Today;
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        Assert.Equal(testDate.AddDays(-14), from);
        Assert.Equal(testDate, to);
    }

    [Fact]
    public void GetCurrentPeriod_ZeroPeriodLength_ReturnsFallback()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 0
        };

        var testDate = DateTime.Today;
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        Assert.Equal(testDate.AddDays(-14), from);
        Assert.Equal(testDate, to);
    }

    [Fact]
    public void GetPreviousPeriod_WithValidSettings_ReturnsCorrectPeriod()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        // Test date: Jan 20, 2025 (in 2nd period)
        var testDate = new DateTime(2025, 1, 20);
        var (from, to) = GetPreviousSigningPeriod(settings, testDate);

        Assert.Equal(new DateTime(2025, 1, 1), from);  // Start of 1st period
        Assert.Equal(new DateTime(2025, 1, 14), to);   // End of 1st period
    }

    [Fact]
    public void GetPreviousPeriod_InFirstPeriod_ReturnsFallback()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        var testDate = new DateTime(2025, 1, 10); // In first period
        var (from, to) = GetPreviousSigningPeriod(settings, testDate);

        // Should return fallback since there's no previous period
        Assert.Equal(testDate.AddDays(-14), from);
        Assert.Equal(testDate, to);
    }

    [Fact]
    public void GetPreviousPeriod_InThirdPeriod_ReturnsSecondPeriod()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        var testDate = new DateTime(2025, 2, 5); // Day 35 = 3rd period
        var (from, to) = GetPreviousSigningPeriod(settings, testDate);

        Assert.Equal(new DateTime(2025, 1, 15), from); // Start of 2nd period
        Assert.Equal(new DateTime(2025, 1, 28), to);   // End of 2nd period
    }

    [Fact]
    public void GetCurrentPeriod_WithFutureEndDate_IncludesFutureDates()
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = 14
        };

        // Test on first day of period - end date will be in future
        var testDate = new DateTime(2025, 1, 1);
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        Assert.Equal(new DateTime(2025, 1, 1), from);
        Assert.Equal(new DateTime(2025, 1, 14), to);
        Assert.True(to > testDate); // End date is in the future
    }

    [Theory]
    [InlineData(7)]   // Weekly
    [InlineData(14)]  // Fortnightly (typical JSA)
    [InlineData(28)]  // Monthly
    public void GetCurrentPeriod_WithVariousPeriodLengths_CalculatesCorrectly(int periodDays)
    {
        var settings = new JsaSettings
        {
            SigningStartDate = new DateTime(2025, 1, 1),
            PeriodLengthDays = periodDays
        };

        var testDate = new DateTime(2025, 1, 1).AddDays(periodDays * 2 + 3); // In 3rd period
        var (from, to) = GetCurrentSigningPeriod(settings, testDate);

        var expectedFrom = new DateTime(2025, 1, 1).AddDays(periodDays * 2);
        var expectedTo = expectedFrom.AddDays(periodDays - 1);

        Assert.Equal(expectedFrom, from);
        Assert.Equal(expectedTo, to);
    }

    // Helper methods that mirror the logic in JsaReport.razor
    private static (DateTime from, DateTime to) GetCurrentSigningPeriod(JsaSettings settings, DateTime today)
    {
        if (settings.SigningStartDate.HasValue && settings.PeriodLengthDays > 0)
        {
            var start = settings.SigningStartDate.Value.Date;
            var periodDays = settings.PeriodLengthDays;
            var daysSinceStart = (today - start).Days;
            if (daysSinceStart >= 0)
            {
                var currentPeriodIndex = daysSinceStart / periodDays;
                var periodStart = start.AddDays(currentPeriodIndex * periodDays);
                var periodEnd = periodStart.AddDays(periodDays - 1);
                return (periodStart, periodEnd);
            }
        }
        // Fallback: last 14 days
        return (today.AddDays(-14), today);
    }

    private static (DateTime from, DateTime to) GetPreviousSigningPeriod(JsaSettings settings, DateTime today)
    {
        if (settings.SigningStartDate.HasValue && settings.PeriodLengthDays > 0)
        {
            var start = settings.SigningStartDate.Value.Date;
            var periodDays = settings.PeriodLengthDays;
            var daysSinceStart = (today - start).Days;
            if (daysSinceStart >= 0)
            {
                var currentPeriodIndex = daysSinceStart / periodDays;
                var previousPeriodIndex = currentPeriodIndex - 1;
                if (previousPeriodIndex >= 0)
                {
                    var periodStart = start.AddDays(previousPeriodIndex * periodDays);
                    var periodEnd = periodStart.AddDays(periodDays - 1);
                    return (periodStart, periodEnd);
                }
            }
        }
        // Fallback if no previous period available
        return (today.AddDays(-14), today);
    }
}
