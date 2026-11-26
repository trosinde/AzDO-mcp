using System.Text.Json.Serialization;

namespace AzdoMcp.Core.Connections;

[JsonConverter(typeof(JsonStringEnumConverter<ServerType>))]
public enum ServerType
{
    /// <summary>Azure DevOps Services (dev.azure.com / *.visualstudio.com).</summary>
    Services,

    /// <summary>Azure DevOps Server (on-prem collection).</summary>
    Server,
}

[JsonConverter(typeof(JsonStringEnumConverter<AccessArea>))]
public enum AccessArea
{
    WorkItems,
    Code,
    Test,
    Iterations,

    /// <summary>Builds and releases (read only).</summary>
    Pipelines,
}

public static class Access
{
    public static readonly AccessArea[] Areas = [AccessArea.WorkItems, AccessArea.Code, AccessArea.Pipelines, AccessArea.Test, AccessArea.Iterations];

    /// <summary>"ja", "nein", "n/a" or "?".</summary>
    public static string Display(this Check? check) => check switch
    {
        Check.Yes => "ja",
        Check.No => "nein",
        Check.NotTestable => "n/a",
        _ => "?",
    };

    /// <summary>Tooltip for values that are not a checked fact; null for ja/nein.</summary>
    public static string? Explain(this Check? check) => check switch
    {
        Check.NotTestable => "nicht prüfbar ohne echtes Schreiben",
        null => "noch nicht geprüft – Neu einlesen",
        _ => null,
    };

    public static string Display(this AccessArea area) => area switch
    {
        AccessArea.WorkItems => "Work Items",
        AccessArea.Code => "Code",
        AccessArea.Test => "Test",
        AccessArea.Pipelines => "Pipelines",
        _ => "Iterationen",
    };

    public static string Display(this AccessColumn column) => column == AccessColumn.Read ? "Lesen" : "Schreiben";

    /// <summary>nein beats ?, ? beats n/a, n/a beats ja.</summary>
    public static Check? Combine(Check? a, Check? b) =>
        a == Check.No || b == Check.No ? Check.No
        : a is null || b is null ? null
        : a == Check.NotTestable || b == Check.NotTestable ? Check.NotTestable
        : Check.Yes;

    /// <summary>Area a tool group needs.</summary>
    public static AccessArea AreaOf(ToolGroup group) => group switch
    {
        ToolGroup.Git => AccessArea.Code,
        ToolGroup.Pipelines => AccessArea.Pipelines,
        ToolGroup.Test => AccessArea.Test,
        ToolGroup.Planning => AccessArea.Iterations,
        _ => AccessArea.WorkItems,
    };
}

/// <summary>Result of one check: allowed, denied, or not testable without a real write. Null = not checked yet ("?").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Check>))]
public enum Check
{
    Yes,
    No,
    NotTestable,
}

public enum AccessColumn
{
    Read,
    Write,
}

/// <summary>Lesen and Schreiben of one area.</summary>
public sealed record AreaAccess(Check? Read = null, Check? Write = null)
{
    public static readonly AreaAccess Unknown = new();

    public Check? this[AccessColumn column] => column == AccessColumn.Read ? Read : Write;

    public static AreaAccess Combine(AreaAccess a, AreaAccess b) => new(Access.Combine(a.Read, b.Read), Access.Combine(a.Write, b.Write));
}

/// <summary>Lesen/Schreiben per area. A missing area or column means "?" (not checked yet).</summary>
public sealed class AreaRights
{
    public Dictionary<AccessArea, AreaAccess> Areas { get; set; } = [];

    public AreaAccess this[AccessArea area]
    {
        get => Areas.TryGetValue(area, out var a) ? a : AreaAccess.Unknown;
        set
        {
            if (value.Read is null && value.Write is null)
            {
                Areas.Remove(area);
            }
            else
            {
                Areas[area] = value;
            }
        }
    }

    public static AreaRights All(Check? read, Check? write) => new() { Areas = Access.Areas.ToDictionary(a => a, _ => new AreaAccess(read, write)) };

    public AreaRights Clone() => new() { Areas = new(Areas) };

    /// <summary>Column-wise combination; a null side counts as "?".</summary>
    public static AreaRights Combine(AreaRights? a, AreaRights? b)
    {
        var r = new AreaRights();
        foreach (var area in Access.Areas)
        {
            r[area] = AreaAccess.Combine(a?[area] ?? AreaAccess.Unknown, b?[area] ?? AreaAccess.Unknown);
        }

        return r;
    }

    public bool SameAs(AreaRights? other) => Access.Areas.All(a => this[a] == (other?[a] ?? AreaAccess.Unknown));
}

/// <summary>One configured Azure DevOps project of a connection.</summary>
public sealed class ProjectEntry
{
    /// <summary>Stable internal key; permission overrides refer to it.</summary>
    public string Key { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Azure DevOps project ID; empty for migrated entries until the first rescan.</summary>
    public string AzdoId { get; set; } = "";

    /// <summary>Project name in Azure DevOps.</summary>
    public string Name { get; set; } = "";

    /// <summary>Name the AI uses as project="…"; unique across all connections.</summary>
    public string Alias { get; set; } = "";

    public bool IsDefault { get; set; }

    /// <summary>Schreibschalter. New projects start with it off.</summary>
    public bool AllowWrite { get; set; }

    /// <summary>The user's rights in this project (Security API); null = unknown.</summary>
    public AreaRights? ProjectRights { get; set; }

    /// <summary>False when the last discovery no longer saw the project.</summary>
    public bool Visible { get; set; } = true;

    public DateTimeOffset? AddedAt { get; set; }

    public ProjectEntry Clone()
    {
        var p = (ProjectEntry)MemberwiseClone();
        p.ProjectRights = ProjectRights?.Clone();
        return p;
    }
}

/// <summary>One collection with one PAT and its configured projects. Contains no secret.</summary>
public sealed class Connection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Name der Verbindung, e.g. the server name.</summary>
    public string Name { get; set; } = "";

    public ServerType ServerType { get; set; } = ServerType.Server;

    /// <summary>For example https://dev.azure.com/org or https://azdo.example.local/DefaultCollection.</summary>
    public string CollectionUrl { get; set; } = "";

    public DateOnly? PatExpires { get; set; }

    /// <summary>Erinnerung: days before expiry that count as "bald".</summary>
    public int ReminderDays { get; set; } = 7;

    /// <summary>PAT scope per area from the last test/discovery; null = unknown.</summary>
    public AreaRights? PatScope { get; set; }

    public DateTimeOffset? LastTested { get; set; }

    public DateTimeOffset? LastDiscovery { get; set; }

    /// <summary>Set when Azure DevOps answered 401 for this connection; cleared on success.</summary>
    public DateTimeOffset? LastAuthFailure { get; set; }

    public List<ProjectEntry> Projects { get; set; } = [];

    [JsonIgnore]
    public string Host => Uri.TryCreate(CollectionUrl, UriKind.Absolute, out var u) ? u.Host : CollectionUrl;

    [JsonIgnore]
    public string CollectionBase => CollectionUrl.TrimEnd('/');

    /// <summary>Effective right per area and column from PAT scope and project right.</summary>
    public AreaRights Effective(ProjectEntry project) => AreaRights.Combine(PatScope, project.ProjectRights);

    public Connection Clone()
    {
        var c = (Connection)MemberwiseClone();
        c.PatScope = PatScope?.Clone();
        c.Projects = Projects.Select(p => p.Clone()).ToList();
        return c;
    }
}

/// <summary>A configured project together with its connection – what a tool call runs against.</summary>
public sealed record ProjectRef(Connection Connection, ProjectEntry Project)
{
    public string Alias => Project.Alias;

    /// <summary>Key for project-level permissions: "connectionId/projectKey".</summary>
    public string PermissionKey => $"{Connection.Id}/{Project.Key}";

    public string ProjectBase => $"{Connection.CollectionBase}/{Uri.EscapeDataString(Project.Name)}";

    public AreaRights Effective => Connection.Effective(Project);
}
