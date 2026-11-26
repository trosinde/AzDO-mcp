using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;

namespace AzdoMcp.Core.Discovery;

/// <summary>One changed value; only moves to or from "nein" count.</summary>
public sealed record AreaChange(AccessArea Area, AccessColumn Column, Check? Old, Check? New)
{
    public string Text => $"{Area.Display()} {Column.Display()}: {Old.Display()} → {New.Display()}";
}

public sealed record ChangedProject(ProjectEntry Project, DiscoveredProject Now, IReadOnlyList<AreaChange> Changes);

public sealed record DiscoveryDiff(
    IReadOnlyList<DiscoveredProject> New,
    IReadOnlyList<ProjectEntry> Missing,
    IReadOnlyList<ChangedProject> Changed,
    IReadOnlyList<ProjectEntry> Unchanged)
{
    public int ChangeCount => New.Count + Missing.Count + Changed.Count;
}

/// <summary>What the user chose for one new project (wizard step or defaults).</summary>
public sealed record ProjectChoice(
    string Alias,
    bool IsDefault = false,
    bool AllowWrite = false,
    IReadOnlyDictionary<ToolGroup, PermissionMode>? ChangedModes = null);

public sealed record MergeResult(Connection Connection, IReadOnlyDictionary<string, IReadOnlyDictionary<ToolGroup, PermissionMode>> ProjectModes);

/// <summary>Combines a discovery with the stored connection. Never overwrites user settings.</summary>
public static class DiscoveryMerger
{
    public static DiscoveryDiff Diff(Connection stored, DiscoveryResult result)
    {
        var news = new List<DiscoveredProject>();
        var changed = new List<ChangedProject>();
        var unchanged = new List<ProjectEntry>();
        var matched = new HashSet<string>();

        foreach (var d in result.Projects)
        {
            var existing = Match(stored, d);
            if (existing is null)
            {
                news.Add(d);
                continue;
            }

            matched.Add(existing.Key);
            var before = stored.Effective(existing);
            var changes = (
                from a in Access.Areas
                from col in new[] { AccessColumn.Read, AccessColumn.Write }
                let old = before[a][col]
                let now = d.Effective[a][col]
                where (old == Check.No) != (now == Check.No)
                select new AreaChange(a, col, old, now)).ToList();
            if (changes.Count > 0 || !existing.Visible)
            {
                changed.Add(new ChangedProject(existing, d, changes));
            }
            else
            {
                unchanged.Add(existing);
            }
        }

        var missing = stored.Projects.Where(p => !matched.Contains(p.Key)).ToList();
        return new DiscoveryDiff(news, missing, changed, unchanged);
    }

    /// <summary>
    /// Applies a discovery: refreshes PAT scope, rights and visibility of known projects and adds the
    /// chosen new projects. Alias, default flag, write switch and modes of existing projects stay as they are.
    /// </summary>
    public static MergeResult Apply(Connection stored, DiscoveryResult result, IReadOnlyDictionary<string, ProjectChoice> newProjects)
    {
        var c = stored.Clone();
        c.PatScope = result.PatScope.Clone();
        c.LastDiscovery = result.At;
        var modes = new Dictionary<string, IReadOnlyDictionary<ToolGroup, PermissionMode>>();

        var seen = new HashSet<string>();
        foreach (var d in result.Projects)
        {
            var existing = Match(c, d);
            if (existing is not null)
            {
                seen.Add(existing.Key);
                existing.AzdoId = d.AzdoId;
                existing.Name = d.Name;
                existing.ProjectRights = d.ProjectRights.Clone();
                existing.Visible = true;
                continue;
            }

            if (!newProjects.TryGetValue(d.AzdoId, out var choice))
            {
                continue;
            }

            var entry = new ProjectEntry
            {
                AzdoId = d.AzdoId,
                Name = d.Name,
                Alias = choice.Alias,
                IsDefault = choice.IsDefault,
                // Only a checked "nein" prevents writing; more rights later never switch it on.
                AllowWrite = choice.AllowWrite && d.Effective[AccessArea.WorkItems].Write != Check.No,
                ProjectRights = d.ProjectRights.Clone(),
                Visible = true,
                AddedAt = result.At,
            };
            c.Projects.Add(entry);
            seen.Add(entry.Key);
            if (choice.ChangedModes is { Count: > 0 } changedModes)
            {
                modes[$"{c.Id}/{entry.Key}"] = changedModes;
            }
        }

        foreach (var p in c.Projects.Where(p => !seen.Contains(p.Key)))
        {
            p.Visible = false; // marked, never deleted automatically
        }

        if (c.Projects.Count(p => p.IsDefault) > 1)
        {
            var keep = c.Projects.Last(p => p.IsDefault);
            foreach (var p in c.Projects)
            {
                p.IsDefault = ReferenceEquals(p, keep);
            }
        }

        return new MergeResult(c, modes);
    }

    /// <summary>Default choices for all new projects (CLI --apply): alias = name, nothing changed.</summary>
    public static Dictionary<string, ProjectChoice> DefaultChoices(DiscoveryDiff diff, Func<string, IEnumerable<string>, string> suggestAlias)
    {
        var reserved = new List<string>();
        var result = new Dictionary<string, ProjectChoice>();
        foreach (var d in diff.New)
        {
            var alias = suggestAlias(d.Name, reserved);
            reserved.Add(alias);
            result[d.AzdoId] = new ProjectChoice(alias);
        }

        return result;
    }

    private static ProjectEntry? Match(Connection c, DiscoveredProject d) =>
        c.Projects.FirstOrDefault(p => p.AzdoId.Length > 0 && p.AzdoId == d.AzdoId)
        ?? c.Projects.FirstOrDefault(p => p.AzdoId.Length == 0 && string.Equals(p.Name, d.Name, StringComparison.OrdinalIgnoreCase));
}
