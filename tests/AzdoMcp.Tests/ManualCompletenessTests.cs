using System.Text;
using AzdoMcp.Core;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Manager.Core.Services;

namespace AzdoMcp.Tests;

/// <summary>
/// Spec documentation "Manual completeness" (design D8): the user manual in docs/manual describes every tool,
/// tool group, permission mode, update footer text and server command. Texts come from the code wherever the
/// code provides them; each failure names the missing text and its source.
/// </summary>
public class ManualCompletenessTests
{
    private const string ToolsChapter = "07-";
    private const string UpdateChapter = "10-";
    private const string ReferenceChapter = "11-";

    // Footer install mode, from MainViewModel.FooterInstall (src/AzdoMcp.Manager.Core/ViewModels/MainViewModel.cs).
    private const string PortableFooter = "Portable";

    // Server commands, from src/AzdoMcp.Server/Program.cs ("--stdio", "discover") and DiscoverCommand.cs ("--apply").
    private static readonly (string Text, string Source)[] Commands =
    [
        ("--stdio", "AzdoMcp.Server Program.cs"),
        ("discover", "AzdoMcp.Server Program.cs"),
        ("--apply", "AzdoMcp.Server DiscoverCommand.cs"),
    ];

    private static ManualText M => ManualText.Current;

    [Fact]
    public void Every_tool_is_described_in_the_tools_chapter()
    {
        var report = new StringBuilder();
        var chapter = Chapter(ToolsChapter, report);
        foreach (var tool in ToolCatalog.All.Where(t => !ManualText.ContainsWord(chapter, t.Name)))
        {
            report.AppendLine($"  {tool.Name} (ToolCatalog, group \"{tool.Group.Display()}\")");
        }

        Assert.True(report.Length == 0, $"Missing in the tools chapter {Describe(ToolsChapter)}:\n{report}");
    }

    [Fact]
    public void Every_tool_group_and_permission_mode_appears_in_the_manual()
    {
        var required = Enum.GetValues<ToolGroup>().Select(g => (Text: g.Display(), Source: $"ToolGroupNames.Display(ToolGroup.{g})"))
            .Concat(Enum.GetValues<PermissionMode>().Select(m => (Text: m.Display(), Source: $"PermissionModeNames.Display(PermissionMode.{m})")));
        var missing = required.Where(r => !M.AllPlain.Contains(r.Text, StringComparison.Ordinal))
            .Select(r => $"  \"{r.Text}\" ({r.Source})")
            .ToList();

        Assert.True(missing.Count == 0, $"Missing anywhere in the manual ({M.Directory}):\n{string.Join('\n', missing)}");
    }

    [Fact]
    public async Task Update_footer_texts_are_in_the_update_chapter()
    {
        var ct = TestContext.Current.CancellationToken;
        var notConfigured = new UpdateService(null, isInstalled: true).StatusText;

        var backend = new FakeUpdateBackend();
        var service = new UpdateService(backend, isInstalled: true);
        await service.CheckAsync(ct);
        var upToDate = service.StatusText;

        // The UI appends the version ("Update verfügbar (2.0.1)"); the manual only needs the fixed part.
        const string probeVersion = "9.9.9";
        backend.Available = probeVersion;
        await service.CheckAsync(ct);
        var available = service.StatusText;
        var suffix = $" ({probeVersion})";
        Assert.EndsWith(suffix, available, StringComparison.Ordinal);
        available = available[..^suffix.Length];

        var required = new (string Text, string Source)[]
        {
            (upToDate, "UpdateService.StatusText, UpToDate"),
            (available, "UpdateService.StatusText, Available (without version)"),
            (notConfigured, "UpdateService.StatusText, NotConfigured"),
            (PortableFooter, "MainViewModel.FooterInstall"),
        };

        var report = new StringBuilder();
        var chapter = Chapter(UpdateChapter, report);
        foreach (var (text, source) in required.Where(r => !chapter.Contains(r.Text, StringComparison.Ordinal)))
        {
            report.AppendLine($"  \"{text}\" ({source})");
        }

        Assert.True(report.Length == 0, $"Missing in the update chapter {Describe(UpdateChapter)}:\n{report}");
    }

    [Fact]
    public void Server_commands_are_in_the_reference_chapter()
    {
        var report = new StringBuilder();
        var chapter = Chapter(ReferenceChapter, report);
        foreach (var (text, source) in Commands.Where(c => !ManualText.ContainsWord(chapter, c.Text)))
        {
            report.AppendLine($"  \"{text}\" ({source})");
        }

        Assert.True(report.Length == 0, $"Missing in the reference chapter {Describe(ReferenceChapter)}:\n{report}");
    }

    /// <summary>Plain text of the chapter; a missing chapter is reported and yields an empty text.</summary>
    private static string Chapter(string prefix, StringBuilder report)
    {
        if (M.ChapterFile(prefix) is { } file)
        {
            return M.Plain[file];
        }

        report.AppendLine($"  chapter file {prefix}*.md not found in {M.Directory}");
        return string.Empty;
    }

    private static string Describe(string prefix) => M.ChapterFile(prefix) ?? $"{prefix}*.md";
}
