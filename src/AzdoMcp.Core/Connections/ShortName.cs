using System.Text;
using System.Text.RegularExpressions;

namespace AzdoMcp.Core.Connections;

/// <summary>
/// Kurzname of a project: what the user and the AI write in prompts ("offene Bugs in SHOP").
/// 2–10 characters (letters, digits, "-"), starts with a letter, stored in capitals.
/// </summary>
public static partial class ShortName
{
    public const string RuleText = "Kurzname: 2–10 Zeichen, Buchstaben, Ziffern und -, beginnt mit Buchstabe";

    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PR", "BUG", "ID", "TEST", "TASK", "ALL" };

    /// <summary>Error text for a new or edited Kurzname, or null when it is valid.</summary>
    public static string? Validate(string? value)
    {
        var v = (value ?? "").Trim();
        if (!Pattern().IsMatch(v))
        {
            return RuleText;
        }

        return Reserved.Contains(v) ? $"„{v.ToUpperInvariant()}“ ist reserviert" : null;
    }

    public static bool IsValid(string? value) => Validate(value) is null;

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    /// <summary>
    /// Initials of several words (numbers kept whole), otherwise the word itself when it has up to 8
    /// characters, else its first five letters; a digit on a clash.
    /// </summary>
    public static string Suggest(string projectName, IEnumerable<string>? used = null)
    {
        var taken = new HashSet<string>(used ?? [], StringComparer.OrdinalIgnoreCase);
        var words = Words().Matches(projectName).Select(m => m.Value).ToList();
        string baseName;
        if (words.Count >= 2)
        {
            var initials = new StringBuilder();
            foreach (var w in words)
            {
                initials.Append(w.All(char.IsDigit) ? w : w[..1]);
            }

            baseName = initials.ToString();
        }
        else
        {
            var word = words.FirstOrDefault() ?? "";
            baseName = word.Length > 8 ? word[..5] : word;
        }

        baseName = baseName.ToUpperInvariant();
        if (baseName.Length == 0 || !char.IsLetter(baseName[0]))
        {
            baseName = "P" + baseName;
        }

        if (baseName.Length < 2)
        {
            baseName += "P";
        }

        if (baseName.Length > 10)
        {
            baseName = baseName[..10];
        }

        if (IsValid(baseName) && !taken.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var stem = baseName.Length + suffix.Length > 10 ? baseName[..(10 - suffix.Length)] : baseName;
            var candidate = stem + suffix;
            if (IsValid(candidate) && !taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9-]{1,9}$")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
