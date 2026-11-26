using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace AzdoMcp.Tests;

/// <summary>
/// The user manual (docs/manual/*.md) as the reader sees it: Markdown rendered with the same pipeline as
/// build/ManualBuilder, tags removed, entities decoded, whitespace collapsed. Comparing against the rendered
/// text (not the source) means Markdown escapes cannot hide or fake a text.
/// </summary>
internal sealed partial class ManualText
{
    private static readonly Lazy<ManualText> Instance = new(Load);

    private ManualText(string directory, IReadOnlyDictionary<string, string> plain)
    {
        Directory = directory;
        Plain = plain;
        AllPlain = string.Join(" ", plain.Values);
    }

    public static ManualText Current => Instance.Value;

    private static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>Folder that holds index.md and the chapters.</summary>
    public string Directory { get; }

    /// <summary>File name (e.g. "07-tools.md") to rendered, normalised text.</summary>
    public IReadOnlyDictionary<string, string> Plain { get; }

    /// <summary>All files joined.</summary>
    public string AllPlain { get; }

    /// <summary>File name of the chapter with the given prefix (e.g. "07-"), or null when there is none.</summary>
    public string? ChapterFile(string prefix) =>
        Plain.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();

    /// <summary>True when <paramref name="text"/> occurs as a whole word (not inside a longer identifier).</summary>
    public static bool ContainsWord(string haystack, string text) =>
        Regex.IsMatch(haystack, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(text)}(?![\p{{L}}\p{{N}}_])");

    public static string ToPlainText(string markdown)
    {
        var html = Markdown.ToHtml(markdown, Pipeline);
        html = HtmlComment().Replace(html, " ");
        html = InlineTag().Replace(html, string.Empty);
        html = AnyTag().Replace(html, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(html), " ").Trim();
    }

    private static ManualText Load()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "docs", "manual");
        var plain = System.IO.Directory.Exists(directory)
            ? System.IO.Directory.EnumerateFiles(directory, "*.md")
                .ToDictionary(f => Path.GetFileName(f), f => ToPlainText(File.ReadAllText(f)), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new ManualText(directory, plain);
    }

    /// <summary>Walks up from the test binaries to the folder that contains AzdoMcp.sln.</summary>
    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AzdoMcp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"AzdoMcp.sln not found above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    // Inline markup disappears without a gap ("<code>--stdio</code>." stays "--stdio."), block tags become a space.
    [GeneratedRegex(@"</?(code|em|strong|a|span|kbd|del|sup|sub|mark)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
}
