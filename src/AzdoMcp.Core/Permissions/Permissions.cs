using System.Text.Json;
using System.Text.Json.Serialization;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Infrastructure;
using AzdoMcp.Core.Tools;

namespace AzdoMcp.Core.Permissions;

/// <summary>Order matters: a higher value is more restrictive.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PermissionMode>))]
public enum PermissionMode
{
    /// <summary>Erlaubt – runs immediately.</summary>
    Allowed = 0,

    /// <summary>Nachfragen – needs approval per call.</summary>
    Ask = 1,

    /// <summary>Gesperrt – not offered to the AI.</summary>
    Blocked = 2,
}

public static class PermissionModeNames
{
    public static string Display(this PermissionMode mode) => mode switch
    {
        PermissionMode.Allowed => "Erlaubt",
        PermissionMode.Ask => "Nachfragen",
        PermissionMode.Blocked => "Gesperrt",
        _ => mode.ToString(),
    };
}

/// <summary>Modes set on one level (global, a connection or a project).</summary>
public sealed class ScopeModes
{
    public Dictionary<ToolGroup, PermissionMode> Groups { get; set; } = [];

    public Dictionary<string, PermissionMode> Tools { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Groups.Count == 0 && Tools.Count == 0;

    public ScopeModes Clone() => new() { Groups = new(Groups), Tools = new(Tools) };
}

/// <summary>Where a mode came from – for the "wie Gruppe / Ausnahme" hints and messages.</summary>
public enum ModeSource
{
    Default,
    Group,
    Tool,
    ConnectionGroup,
    ConnectionTool,
    ProjectGroup,
    ProjectTool,
}

/// <summary>Content of permissions.json (v2: four levels).</summary>
public sealed class PermissionSettings
{
    /// <summary>Global group modes.</summary>
    public Dictionary<ToolGroup, PermissionMode> Groups { get; set; } = [];

    /// <summary>Global per-tool modes.</summary>
    public Dictionary<string, PermissionMode> Tools { get; set; } = [];

    /// <summary>Connection ID → modes.</summary>
    public Dictionary<string, ScopeModes> Connections { get; set; } = [];

    /// <summary>"connectionId/projectKey" → modes.</summary>
    public Dictionary<string, ScopeModes> Projects { get; set; } = [];

    /// <summary>v1 only: connection ID → blocked groups. Migrated to <see cref="Connections"/> on read.</summary>
    public Dictionary<string, List<ToolGroup>>? ConnectionBlocks { get; set; }

    public PermissionSettings Clone() => new()
    {
        Groups = new(Groups),
        Tools = new(Tools),
        Connections = Connections.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        Projects = Projects.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
    };

    public ScopeModes ForConnection(string id) => Connections.TryGetValue(id, out var m) ? m : Connections[id] = new ScopeModes();

    public ScopeModes ForProject(string key) => Projects.TryGetValue(key, out var m) ? m : Projects[key] = new ScopeModes();

    public static PermissionMode DefaultFor(ToolGroup group) =>
        group == ToolGroup.WorkItemsWrite ? PermissionMode.Ask : PermissionMode.Allowed;

    /// <summary>Moves v1 connection blocks into connection-level group modes.</summary>
    internal void MigrateV1()
    {
        if (ConnectionBlocks is null)
        {
            return;
        }

        foreach (var (id, groups) in ConnectionBlocks)
        {
            foreach (var g in groups)
            {
                ForConnection(id).Groups[g] = PermissionMode.Blocked;
            }
        }

        ConnectionBlocks = null;
    }
}

/// <param name="Reason">German text for blocked or ask decisions, null when allowed.</param>
public sealed record PermissionDecision(PermissionMode Mode, string? Reason, PermissionBlockKind Kind = PermissionBlockKind.None);

public enum PermissionBlockKind
{
    None,
    Global,
    Connection,
    Project,
    WriteDisabled,
    MissingScope,
    NotVisible,
}

public static class PermissionEvaluator
{
    /// <summary>The global group mode (System is always allowed).</summary>
    public static PermissionMode GroupMode(PermissionSettings s, ToolGroup group) =>
        group == ToolGroup.System ? PermissionMode.Allowed
        : s.Groups.TryGetValue(group, out var m) ? m : PermissionSettings.DefaultFor(group);

    /// <summary>The global tool mode or the inherited group mode.</summary>
    public static PermissionMode ToolMode(PermissionSettings s, ToolDefinition tool) =>
        tool.Group == ToolGroup.System ? PermissionMode.Allowed
        : s.Tools.TryGetValue(tool.Name, out var m) ? m : GroupMode(s, tool.Group);

    /// <summary>
    /// Mode from the hierarchy for a tool: deepest set level wins –
    /// project+tool, project+group, connection+tool, connection+group, tool, group, default.
    /// </summary>
    public static (PermissionMode Mode, ModeSource Source) Resolve(PermissionSettings s, ToolDefinition tool, string? connectionId, string? projectKey)
    {
        if (tool.Group == ToolGroup.System)
        {
            return (PermissionMode.Allowed, ModeSource.Default);
        }

        if (projectKey is not null && s.Projects.TryGetValue(projectKey, out var pm))
        {
            if (pm.Tools.TryGetValue(tool.Name, out var m))
            {
                return (m, ModeSource.ProjectTool);
            }

            if (pm.Groups.TryGetValue(tool.Group, out m))
            {
                return (m, ModeSource.ProjectGroup);
            }
        }

        if (connectionId is not null && s.Connections.TryGetValue(connectionId, out var cm))
        {
            if (cm.Tools.TryGetValue(tool.Name, out var m))
            {
                return (m, ModeSource.ConnectionTool);
            }

            if (cm.Groups.TryGetValue(tool.Group, out m))
            {
                return (m, ModeSource.ConnectionGroup);
            }
        }

        if (s.Tools.TryGetValue(tool.Name, out var t))
        {
            return (t, ModeSource.Tool);
        }

        return s.Groups.TryGetValue(tool.Group, out var g) ? (g, ModeSource.Group) : (PermissionSettings.DefaultFor(tool.Group), ModeSource.Default);
    }

    /// <summary>Mode a group has on a level when no tool-specific mode is set (for UI rows).</summary>
    public static (PermissionMode Mode, ModeSource Source) ResolveGroup(PermissionSettings s, ToolGroup group, string? connectionId, string? projectKey)
    {
        if (group == ToolGroup.System)
        {
            return (PermissionMode.Allowed, ModeSource.Default);
        }

        if (projectKey is not null && s.Projects.TryGetValue(projectKey, out var pm) && pm.Groups.TryGetValue(group, out var m))
        {
            return (m, ModeSource.ProjectGroup);
        }

        if (connectionId is not null && s.Connections.TryGetValue(connectionId, out var cm) && cm.Groups.TryGetValue(group, out m))
        {
            return (m, ModeSource.ConnectionGroup);
        }

        return s.Groups.TryGetValue(group, out var g) ? (g, ModeSource.Group) : (PermissionSettings.DefaultFor(group), ModeSource.Default);
    }

    /// <summary>
    /// Effective mode for a call: the most restrictive of the hierarchy mode, visibility,
    /// the effective right of the tool's area and the project write switch.
    /// </summary>
    public static PermissionDecision Evaluate(ToolDefinition tool, ProjectRef? target, PermissionSettings s)
    {
        if (tool.Group == ToolGroup.System)
        {
            return new(PermissionMode.Allowed, null);
        }

        var (mode, source) = Resolve(s, tool, target?.Connection.Id, target?.PermissionKey);

        if (target is not null)
        {
            var alias = target.Alias;
            if (!target.Project.Visible)
            {
                return new(PermissionMode.Blocked, $"kein Zugriff – {alias} ist in Azure DevOps nicht mehr sichtbar", PermissionBlockKind.NotVisible);
            }

            var effective = target.Effective;
            var area = Access.AreaOf(tool.Group);
            // Only a checked "nein" restricts; "n/a" and "?" leave the decision to Azure DevOps.
            if (effective[area].Read == Check.No)
            {
                return new(PermissionMode.Blocked, $"403 Scope fehlt – {area.Display()}: kein Zugriff in {alias}", PermissionBlockKind.MissingScope);
            }

            if (tool.IsWrite && !target.Project.AllowWrite)
            {
                return new(PermissionMode.Blocked, $"Für {alias} gesperrt (Schreibschalter aus)", PermissionBlockKind.WriteDisabled);
            }

            if (tool.IsWrite && effective[AccessArea.WorkItems].Write == Check.No)
            {
                return new(PermissionMode.Blocked, $"Für {alias} gesperrt (nur Lesen)", PermissionBlockKind.WriteDisabled);
            }
        }

        if (mode == PermissionMode.Blocked)
        {
            return source switch
            {
                ModeSource.ProjectGroup or ModeSource.ProjectTool => new(PermissionMode.Blocked, $"Für {target!.Alias} gesperrt", PermissionBlockKind.Project),
                ModeSource.ConnectionGroup or ModeSource.ConnectionTool => new(PermissionMode.Blocked, $"Für {target!.Connection.Name} gesperrt", PermissionBlockKind.Connection),
                _ => new(PermissionMode.Blocked, "Tool gesperrt", PermissionBlockKind.Global),
            };
        }

        return mode == PermissionMode.Ask
            ? new(PermissionMode.Ask, "Freigabe nötig")
            : new(PermissionMode.Allowed, null);
    }
}

/// <summary>Reads and writes permissions.json; re-reads when the file changed.</summary>
public sealed class PermissionStore(AppPaths paths)
{
    private readonly object _gate = new();
    private PermissionSettings _cache = new();
    private (DateTime, long)? _stamp;

    public string FilePath => Path.Combine(paths.Root, "permissions.json");

    public PermissionSettings Get()
    {
        lock (_gate)
        {
            var fi = new FileInfo(FilePath);
            (DateTime, long)? stamp = fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : null;
            if (stamp != _stamp)
            {
                _cache = Read();
                _stamp = stamp;
            }

            return _cache.Clone();
        }
    }

    public void Save(PermissionSettings settings)
    {
        FileUtil.WithMutex($@"Local\AzdoMcp-permissions-{paths.RootHash}", () =>
        {
            lock (_gate)
            {
                var clean = settings.Clone();
                clean.Groups.Remove(ToolGroup.System);
                foreach (var key in clean.Connections.Where(kv => kv.Value.IsEmpty).Select(kv => kv.Key).ToList())
                {
                    clean.Connections.Remove(key);
                }

                foreach (var key in clean.Projects.Where(kv => kv.Value.IsEmpty).Select(kv => kv.Key).ToList())
                {
                    clean.Projects.Remove(key);
                }

                paths.EnsureCreated();
                FileUtil.WriteAllTextAtomic(FilePath, JsonSerializer.Serialize(clean, Json.File));
                _stamp = null;
            }
        });
    }

    /// <summary>Applies a change to the stored settings under the lock.</summary>
    public void Update(Action<PermissionSettings> change)
    {
        var s = Get();
        change(s);
        Save(s);
    }

    private PermissionSettings Read()
    {
        if (!File.Exists(FilePath))
        {
            return new PermissionSettings();
        }

        try
        {
            var s = JsonSerializer.Deserialize<PermissionSettings>(File.ReadAllText(FilePath), Json.File) ?? new();
            s.MigrateV1();
            return s;
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"permissions.json ist ungültig: {ex.Message}", ex);
        }
    }
}

public static class ToolListing
{
    /// <summary>
    /// Tools offered in tools/list: omitted when blocked for every configured project; when blocked
    /// for some projects only, the description names them.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> Visible(PermissionSettings settings, IReadOnlyList<ProjectRef> projects)
    {
        var result = new List<ToolDefinition>();
        var shown = projects.Where(p => p.Project.Visible).ToList();
        foreach (var tool in ToolCatalog.All)
        {
            if (!tool.NeedsConnection || projects.Count == 0)
            {
                if (PermissionEvaluator.Evaluate(tool, null, settings).Mode != PermissionMode.Blocked)
                {
                    result.Add(tool);
                }

                continue;
            }

            var blockedFor = projects
                .Where(p => PermissionEvaluator.Evaluate(tool, p, settings).Mode == PermissionMode.Blocked)
                .Select(p => p.Alias)
                .ToList();

            if (blockedFor.Count == projects.Count)
            {
                continue;
            }

            var listed = tool with { InputSchema = WithShortNames(tool.InputSchema, shown) };
            result.Add(blockedFor.Count == 0
                ? listed
                : listed with { Description = $"{tool.Description} Nicht verfügbar für: {string.Join(", ", blockedFor)}." });
        }

        return result;
    }

    /// <summary>Stable fingerprint to detect when list_changed must be sent (includes the Kurznamen).</summary>
    public static string Signature(IEnumerable<ToolDefinition> tools) =>
        string.Join("\n", tools.Select(t => t.Name + "|" + t.Description + "|" + t.InputSchema.ToJsonString()));

    /// <summary>The "project" parameter lists the configured Kurznamen as allowed values.</summary>
    public static System.Text.Json.Nodes.JsonObject WithShortNames(System.Text.Json.Nodes.JsonObject schema, IReadOnlyList<ProjectRef> projects)
    {
        if (projects.Count == 0 || schema["properties"]?["project"] is null)
        {
            return schema;
        }

        var copy = (System.Text.Json.Nodes.JsonObject)schema.DeepClone();
        var project = (System.Text.Json.Nodes.JsonObject)copy["properties"]!["project"]!;
        var table = string.Join(", ", projects.Select(p => $"{p.Alias} = {p.Project.Name}"));
        var standard = projects.FirstOrDefault(p => p.Project.IsDefault)?.Alias;
        project["description"] = $"Kurzname des Azure-DevOps-Projekts: {table}." + (standard is null ? "" : $" Ohne Angabe: Standardprojekt {standard}.");
        project["enum"] = new System.Text.Json.Nodes.JsonArray(projects.Select(p => (System.Text.Json.Nodes.JsonNode)p.Alias).ToArray());
        return copy;
    }
}
