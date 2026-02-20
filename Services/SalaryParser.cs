using System.Text.RegularExpressions;

namespace JobTracker.Services;

public static class SalaryParser
{
    /// <summary>
    /// Parses a free-text salary string into normalised annual min/max values.
    /// Daily rates are multiplied by 230 working days, hourly by 1840 hours, monthly by 12.
    /// No currency conversion is performed.
    /// </summary>
    public static (decimal? Min, decimal? Max) Parse(string? salary)
    {
        if (string.IsNullOrWhiteSpace(salary))
            return (null, null);

        var text = salary.Trim();

        // Check for "not provided" style strings
        if (Regex.IsMatch(text, @"(?i)salary\s+not\s+provided|not\s+specified|competitive|negotiable"))
            return (null, null);

        // Determine period multiplier
        decimal multiplier = GetPeriodMultiplier(text);

        // Detect "Up to" / "From" modifiers
        bool isUpTo = Regex.IsMatch(text, @"(?i)^up\s+to\b");
        bool isFrom = Regex.IsMatch(text, @"(?i)^from\b");

        // Extract all numbers from the string
        var numbers = ExtractNumbers(text);

        if (numbers.Count == 0)
            return (null, null);

        if (numbers.Count == 1)
        {
            var value = Math.Round(numbers[0] * multiplier, 0);
            if (isUpTo)
                return (null, value);
            if (isFrom)
                return (value, null);
            return (value, value);
        }

        // Two or more numbers - take first two as range
        var min = Math.Round(numbers[0] * multiplier, 0);
        var max = Math.Round(numbers[1] * multiplier, 0);

        // Ensure min <= max
        if (min > max)
            (min, max) = (max, min);

        return (min, max);
    }

    private static decimal GetPeriodMultiplier(string text)
    {
        if (Regex.IsMatch(text, @"(?i)\b(a\s+day|daily|per\s+day|\/day)\b"))
            return 230m;
        if (Regex.IsMatch(text, @"(?i)\b(an?\s+hour|hourly|per\s+hour|\/hour|\/hr|p/h)\b"))
            return 1840m;
        if (Regex.IsMatch(text, @"(?i)\b(a\s+month|monthly|per\s+month|\/month|pcm)\b"))
            return 12m;
        // yearly / annual / per annum / default
        return 1m;
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
