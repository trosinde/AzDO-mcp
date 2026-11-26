using System.Text.Json.Nodes;

namespace AzdoMcp.Core.Tools;

/// <summary>Small statistics helpers for the analysis tools. Deterministic, invariant culture.</summary>
internal static class Aggregate
{
    public const int MaxRows = 500;

    public static double? Avg(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }

    /// <summary>Median; the mean of the two middle values for an even count.</summary>
    public static double? Median(IEnumerable<double> values)
    {
        var s = values.Order().ToList();
        if (s.Count == 0)
        {
            return null;
        }

        var mid = s.Count / 2;
        return Math.Round(s.Count % 2 == 1 ? s[mid] : (s[mid - 1] + s[mid]) / 2, 1);
    }

    /// <summary>Nearest-rank percentile.</summary>
    public static double? Percentile(IEnumerable<double> values, double p)
    {
        var s = values.Order().ToList();
        if (s.Count == 0)
        {
            return null;
        }

        var rank = (int)Math.Ceiling(p / 100.0 * s.Count);
        return Math.Round(s[Math.Clamp(rank, 1, s.Count) - 1], 1);
    }

    /// <summary>Share in percent with one decimal; null when there is nothing to divide by.</summary>
    public static double? Rate(int part, int whole) => whole == 0 ? null : Math.Round(100.0 * part / whole, 1);

    public static double? Ratio(double part, double whole) => whole == 0 ? null : Math.Round(part / whole, 2);

    public static JsonObject Stats(IEnumerable<double> values)
    {
        var list = values.ToList();
        return new JsonObject
        {
            ["n"] = list.Count,
            ["avg"] = Avg(list),
            ["median"] = Median(list),
            ["p90"] = Percentile(list, 90),
        };
    }

    /// <summary>Checks requested metrics against the allowed list; empty means the defaults.</summary>
    public static IReadOnlyList<string> Metrics(IReadOnlyList<string> requested, IReadOnlyList<string> allowed, IReadOnlyList<string> defaults)
    {
        if (requested.Count == 0)
        {
            return defaults;
        }

        var unknown = requested.Where(m => !allowed.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolArgumentException($"Unbekannte Kennzahl „{string.Join(", ", unknown)}“. Erlaubt: {string.Join(", ", allowed)}.");
        }

        return requested.Select(m => allowed.First(a => a.Equals(m, StringComparison.OrdinalIgnoreCase))).Distinct().ToList();
    }

    public static string Choice(string? value, IReadOnlyList<string> allowed, string name)
    {
        if (value is null or "")
        {
            return allowed[0];
        }

        return allowed.FirstOrDefault(a => a.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolArgumentException($"Unbekannter Wert „{value}“ für {name}. Erlaubt: {string.Join(", ", allowed)}.");
    }

    /// <summary>ISO week key, e.g. 2026-W07.</summary>
    public static string Week(DateTimeOffset d) =>
        $"{System.Globalization.ISOWeek.GetYear(d.UtcDateTime)}-W{System.Globalization.ISOWeek.GetWeekOfYear(d.UtcDateTime):00}";

    public static string Month(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public static string Day(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Regex from a tool argument: length-limited and with a match timeout.</summary>
    public static System.Text.RegularExpressions.Regex? Pattern(string? pattern, string name)
    {
        if (pattern is null or "")
        {
            return null;
        }

        if (pattern.Length > 200)
        {
            throw new ToolArgumentException($"„{name}“ ist zu lang (höchstens 200 Zeichen).");
        }

        try
        {
            return new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            throw new ToolArgumentException($"„{name}“ ist kein gültiger regulärer Ausdruck.");
        }
    }

    public static bool IsMatch(System.Text.RegularExpressions.Regex rx, string? text, string name)
    {
        if (text is null)
        {
            return false;
        }

        try
        {
            return rx.IsMatch(text);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            throw new ToolArgumentException($"„{name}“ braucht zu lange – Muster vereinfachen.");
        }
    }
}
