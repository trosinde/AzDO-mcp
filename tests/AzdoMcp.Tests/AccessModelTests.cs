using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core;

namespace AzdoMcp.Tests;

public class AccessModelTests
{
    [Theory]
    [InlineData(Check.No, Check.Yes, Check.No)]
    [InlineData(Check.No, null, Check.No)]
    [InlineData(Check.NotTestable, Check.No, Check.No)]
    [InlineData(null, Check.Yes, null)]
    [InlineData(null, Check.NotTestable, null)]
    [InlineData(Check.NotTestable, Check.Yes, Check.NotTestable)]
    [InlineData(Check.Yes, Check.Yes, Check.Yes)]
    public void Combine_nein_beats_unknown_beats_untestable_beats_ja(Check? a, Check? b, Check? expected)
    {
        Assert.Equal(expected, Access.Combine(a, b));
        Assert.Equal(expected, Access.Combine(b, a));
    }

    [Theory]
    [InlineData(Check.Yes, "ja")]
    [InlineData(Check.No, "nein")]
    [InlineData(Check.NotTestable, "n/a")]
    [InlineData(null, "?")]
    public void Display_texts(Check? c, string text) => Assert.Equal(text, c.Display());

    [Fact]
    public void Only_untested_values_have_an_explanation()
    {
        Assert.Equal("nicht prüfbar ohne echtes Schreiben", ((Check?)Check.NotTestable).Explain());
        Assert.Equal("noch nicht geprüft – Neu einlesen", ((Check?)null).Explain());
        Assert.Null(((Check?)Check.Yes).Explain());
        Assert.Null(((Check?)Check.No).Explain());
    }

    [Fact]
    public void Effective_combines_per_column()
    {
        var c = new Connection { PatScope = Acc.Of(Acc.W, code: Acc.Rn) };
        var p = new ProjectEntry { ProjectRights = Acc.Of(Acc.Ro, code: Acc.W) };
        var e = c.Effective(p);
        Assert.Equal(Acc.Ro, e[AccessArea.WorkItems]);
        Assert.Equal(Acc.Rn, e[AccessArea.Code]);
    }
}

public class ProjectAccessStatusTests
{
    private static ProjectRef P(AreaAccess patWi, AreaAccess projWi, bool allowWrite = true, bool visible = true)
    {
        var project = new ProjectEntry { Key = "k", Name = "SHOP", Alias = "SHOP", AllowWrite = allowWrite, Visible = visible, ProjectRights = Acc.Of(projWi) };
        return new ProjectRef(new Connection { Id = "c", Name = "tfs", PatScope = Acc.Of(patWi), Projects = [project] }, project);
    }

    private static string Text(ProjectRef p, PermissionSettings? s = null) => ProjectAccessStatus.For(p, s ?? new PermissionSettings()).Text;

    [Fact]
    public void Writes_with_approval_by_default() => Assert.Equal("schreibt mit Freigabe", Text(P(Acc.W, Acc.W)));

    [Fact]
    public void Writes_directly_when_allowed() =>
        Assert.Equal("schreibt direkt", Text(P(Acc.W, Acc.W), new PermissionSettings { Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Allowed } }));

    [Fact]
    public void Reason_pat() => Assert.Equal("nur Lesen · PAT", Text(P(Acc.Ro, Acc.W)));

    [Fact]
    public void Reason_project_right() => Assert.Equal("nur Lesen · Projektrecht", Text(P(Acc.W, Acc.Ro)));

    [Fact]
    public void Reason_write_switch() => Assert.Equal("nur Lesen · Schreibschalter aus", Text(P(Acc.W, Acc.W, allowWrite: false)));

    [Fact]
    public void Reason_tools_blocked() =>
        Assert.Equal("nur Lesen · Schreib-Tools gesperrt", Text(P(Acc.W, Acc.W), new PermissionSettings { Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Blocked } }));

    [Fact]
    public void Untested_write_is_named() => Assert.Equal("schreibt mit Freigabe · Schreibrecht nicht geprüft", Text(P(Acc.W, Acc.Rn)));

    [Fact]
    public void No_access_and_not_visible()
    {
        var none = P(Acc.N, Acc.N);
        foreach (var a in Access.Areas)
        {
            none.Project.ProjectRights![a] = Acc.N;
        }

        Assert.Equal("kein Zugriff", Text(none));
        Assert.Equal("nicht mehr sichtbar", Text(P(Acc.W, Acc.W, visible: false)));
    }

    [Fact]
    public void Tooltip_shows_derivation()
    {
        var tip = ProjectAccessStatus.For(P(Acc.W, Acc.Ro), new PermissionSettings()).Tooltip;
        Assert.Contains("Work Items Schreiben: PAT ja · Projektrecht nein → nein", tip);
        Assert.Contains("Schreibschalter an · Modus Schreib-Tools: Nachfragen", tip);
    }
}
