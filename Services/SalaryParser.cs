using System.Text.RegularExpressions;

namespace JobTracker.Services;

public record SalaryParseResult(
    decimal? Min,
    decimal? Max,
    string Currency,
    string Period
);

public static class SalaryParser
{
    // Fixed approximate exchange rates to GBP
    private static readonly Dictionary<string, decimal> RatesToGBP = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = 1m,
        ["USD"] = 0.79m,
        ["EUR"] = 0.86m
    };

    /// <summary>
    /// Attempts to extract a salary snippet from a longer text (e.g. a job description).
    /// Returns the first salary-like substring found, or null if none.
    /// </summary>
    public static string? ExtractFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Try multiple patterns in order of specificity

        // Pattern 1: Currency symbol before number (£30,000 - £40,000 per annum)
        var currencySymbolPattern =
            @"(?:(?:salary|pay|compensation|remuneration|package)\s*[:;]?\s*)?" +
            @"[£$€]\s*\d[\d,]*\.?\d*\s*[kK]?" +
            @"(?:" +
                @"\s*[-–—to]+\s*[£$€]?\s*\d[\d,]*\.?\d*\s*[kK]?" +
            @")?" +
            @"(?:\s*(?:per\s+(?:year|annum|month|hour|day|week)|(?:p/?a|p/?m|p/?h|p/?d|p/?w))\b" +
                @"|\s*/\s*(?:year|yr|annum|month|hour|hr|day|week)\b" +
                @"|\s+(?:yearly|annual|monthly|hourly|daily|a\s+year|a\s+month|a\s+day|an?\s+hour)\b" +
            @")?";

        // Pattern 2: Text currency code before number (GBP 30,000 - 40,000)
        var textCurrencyBeforePattern =
            @"(?:(?:salary|pay|compensation|remuneration|package)\s*[:;]?\s*)?" +
            @"(?:GBP|USD|EUR)\s*\d[\d,]*\.?\d*\s*[kK]?" +
            @"(?:" +
                @"\s*[-–—to]+\s*(?:GBP|USD|EUR)?\s*\d[\d,]*\.?\d*\s*[kK]?" +
            @")?" +
            @"(?:\s*(?:per\s+(?:year|annum|month|hour|day|week)|(?:p/?a|p/?m|p/?h|p/?d|p/?w))\b" +
                @"|\s*/\s*(?:year|yr|annum|month|hour|hr|day|week)\b" +
                @"|\s+(?:yearly|annual|monthly|hourly|daily|a\s+year|a\s+month|a\s+day|an?\s+hour)\b" +
            @")?";

        // Pattern 3: Salary label followed by numbers without explicit currency (Salary: 30,000 - 40,000)
        var labelledNoCurrencyPattern =
            @"(?:salary|pay)\s*[:;]\s*" +
            @"\d[\d,]*\.?\d*\s*[kK]?" +
            @"(?:" +
                @"\s*[-–—to]+\s*\d[\d,]*\.?\d*\s*[kK]?" +
            @")?" +
            @"(?:\s*(?:per\s+(?:year|annum|month|hour|day|week)|(?:p/?a|p/?m|p/?h|p/?d|p/?w))\b" +
                @"|\s*/\s*(?:year|yr|annum|month|hour|hr|day|week)\b" +
                @"|\s+(?:yearly|annual|monthly|hourly|daily|a\s+year|a\s+month|a\s+day|an?\s+hour)\b" +
            @")?";

        var patterns = new[] { currencySymbolPattern, textCurrencyBeforePattern, labelledNoCurrencyPattern };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Length >= 4)
            {
                var result = match.Value.Trim();
                // Strip leading label prefix if present — we just want the value
                var prefixMatch = Regex.Match(result, @"^(?:salary|pay|compensation|remuneration|package)\s*[:;]?\s*", RegexOptions.IgnoreCase);
                if (prefixMatch.Success)
                    result = result[prefixMatch.Length..];
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a free-text salary string into normalised annual min/max values with currency and period metadata.
    /// Daily rates are multiplied by 230 working days, hourly by 1840 hours, monthly by 12.
    /// Currency conversion is applied when a preferred currency is specified.
    /// </summary>
    public static SalaryParseResult ParseFull(string? salary, string preferredCurrency = "GBP")
    {
        if (string.IsNullOrWhiteSpace(salary))
            return new SalaryParseResult(null, null, "", "");

        var text = salary.Trim();

        // Check for "not provided" style strings
        if (Regex.IsMatch(text, @"(?i)salary\s+not\s+provided|not\s+specified|competitive|negotiable"))
            return new SalaryParseResult(null, null, "", "");

        // Detect currency and period
        string currency = DetectCurrency(text);
        var (multiplier, period) = GetPeriodAndMultiplier(text);

        // Detect "Up to" / "From" modifiers
        bool isUpTo = Regex.IsMatch(text, @"(?i)^up\s+to\b");
        bool isFrom = Regex.IsMatch(text, @"(?i)^from\b");

        // Extract all numbers from the string
        var numbers = ExtractNumbers(text);

        if (numbers.Count == 0)
            return new SalaryParseResult(null, null, currency, period);

        // Calculate conversion rate to preferred currency
        decimal conversionRate = GetConversionRate(currency, preferredCurrency);

        if (numbers.Count == 1)
        {
            var value = Math.Round(numbers[0] * multiplier * conversionRate, 0);
            if (isUpTo)
                return new SalaryParseResult(null, value, currency, period);
            if (isFrom)
                return new SalaryParseResult(value, null, currency, period);
            return new SalaryParseResult(value, value, currency, period);
        }

        // Two or more numbers - take first two as range
        var min = Math.Round(numbers[0] * multiplier * conversionRate, 0);
        var max = Math.Round(numbers[1] * multiplier * conversionRate, 0);

        // Ensure min <= max
        if (min > max)
            (min, max) = (max, min);

        return new SalaryParseResult(min, max, currency, period);
    }

    /// <summary>
    /// Legacy Parse method — returns annualised min/max without currency conversion.
    /// Kept for backward compatibility with existing call sites.
    /// </summary>
    public static (decimal? Min, decimal? Max) Parse(string? salary)
    {
        var result = ParseFull(salary, "");
        return (result.Min, result.Max);
    }

    /// <summary>
    /// Detects the currency from a salary string.
    /// Returns "GBP", "USD", "EUR", or "" if unknown.
    /// </summary>
    public static string DetectCurrency(string text)
    {
        if (Regex.IsMatch(text, @"[£]|GBP", RegexOptions.IgnoreCase))
            return "GBP";
        if (Regex.IsMatch(text, @"[$]|USD", RegexOptions.IgnoreCase))
            return "USD";
        if (Regex.IsMatch(text, @"[€]|EUR", RegexOptions.IgnoreCase))
            return "EUR";
        return "";
    }

    /// <summary>
    /// Converts a value from one currency to another using fixed approximate rates.
    /// </summary>
    public static decimal Convert(decimal value, string from, string to)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) ||
            string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return value;

        // Convert to GBP first, then to target
        var toGBP = RatesToGBP.GetValueOrDefault(from, 1m);
        var fromGBP = RatesToGBP.GetValueOrDefault(to, 1m);
        if (fromGBP == 0) return value;
        return value * toGBP / fromGBP;
    }

    /// <summary>
    /// Convenience: convert to GBP.
    /// </summary>
    public static decimal ConvertToGBP(decimal value, string fromCurrency)
        => Convert(value, fromCurrency, "GBP");

    /// <summary>
    /// Gets the currency symbol for display.
    /// </summary>
    public static string GetCurrencySymbol(string currency) => currency switch
    {
        "GBP" => "£",
        "USD" => "$",
        "EUR" => "€",
        _ => "£" // default
    };

    private static decimal GetConversionRate(string from, string preferredCurrency)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(preferredCurrency) ||
            string.Equals(from, preferredCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var fromToGBP = RatesToGBP.GetValueOrDefault(from, 1m);
        var prefToGBP = RatesToGBP.GetValueOrDefault(preferredCurrency, 1m);
        if (prefToGBP == 0) return 1m;
        return fromToGBP / prefToGBP;
    }

    private static (decimal Multiplier, string Period) GetPeriodAndMultiplier(string text)
    {
        if (Regex.IsMatch(text, @"(?i)\b(a\s+day|daily|per\s+day|\/day|p/?d)\b"))
            return (230m, "day");
        if (Regex.IsMatch(text, @"(?i)\b(an?\s+hour|hourly|per\s+hour|\/hour|\/hr|p/h)\b"))
            return (1840m, "hour");
        if (Regex.IsMatch(text, @"(?i)\b(a\s+month|monthly|per\s+month|\/month|pcm)\b"))
            return (12m, "month");
        // yearly / annual / per annum / default
        return (1m, "year");
    }

    // Keep the old method for the legacy Parse() path
    private static decimal GetPeriodMultiplier(string text)
    {
        var (multiplier, _) = GetPeriodAndMultiplier(text);
        return multiplier;
    }

    private static List<decimal> ExtractNumbers(string text)
    {
        var results = new List<decimal>();
        var hasKSuffix = new List<bool>();

        // Pattern matches: optional currency symbol, optional space, number with optional commas/decimals, optional k/K suffix
        // Also handles scientific notation like 3.51E+4
        var pattern = @"(?:[£$€]|EUR|USD|GBP)?\s*(\d[\d,]*\.?\d*(?:[eE][+\-]?\d+)?)\s*([kK])?";

        foreach (Match match in Regex.Matches(text, pattern))
        {
            var numStr = match.Groups[1].Value.Replace(",", "");
            if (decimal.TryParse(numStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                bool hasK = match.Groups[2].Success;
                if (hasK)
                    value *= 1000m;

                results.Add(value);
                hasKSuffix.Add(hasK);
            }
        }

        // Handle ranges like "$80-90k" where only the last number has k suffix.
        // If the last number has k and earlier ones don't, and they look like
        // they should be in the same magnitude (e.g. 80 vs 90000), multiply them up.
        if (results.Count >= 2)
        {
            bool lastHasK = hasKSuffix[^1];
            if (lastHasK)
            {
                for (int i = 0; i < results.Count - 1; i++)
                {
                    if (!hasKSuffix[i] && results[i] < 1000m)
                    {
                        results[i] *= 1000m;
                    }
                }
            }
        }

        return results;
    }
}
