using System.Text;
using System.Text.RegularExpressions;

namespace AzdoMcp.Core.Azdo;

/// <summary>Removes PATs and Authorization values from text that may reach the AI, logs or the UI.</summary>
public static partial class SecretScrubber
{
    public const string Mask = "***";

    public static string Scrub(string text, string? pat)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (!string.IsNullOrEmpty(pat))
        {
            text = text.Replace(pat, Mask, StringComparison.Ordinal);
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat));
            text = text.Replace(basic, Mask, StringComparison.Ordinal);
        }

        return AuthHeader().Replace(text, "${1}" + Mask);
    }

    [GeneratedRegex(@"((?:Authorization|Basic|Bearer)\s*[:=]?\s*)[A-Za-z0-9+/=._\-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex AuthHeader();
}
