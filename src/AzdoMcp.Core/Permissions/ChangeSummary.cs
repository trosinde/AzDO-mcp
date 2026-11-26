using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Tools;

namespace AzdoMcp.Core.Permissions;

/// <summary>
/// Builds the German title and summary of an approval request. Values appear only here
/// (shown to the user), never in the audit log.
/// </summary>
public static class ChangeSummary
{
    private const int MaxValue = 60;

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System.State"] = "Status",
        ["System.Title"] = "Titel",
        ["System.AssignedTo"] = "Zugewiesen an",
        ["System.AreaPath"] = "Bereich",
        ["System.IterationPath"] = "Iteration",
        ["System.Tags"] = "Tags",
        ["System.Description"] = "Beschreibung",
        ["System.Reason"] = "Grund",
        ["Microsoft.VSTS.Common.Priority"] = "Priorität",
        ["Microsoft.VSTS.Common.Severity"] = "Schweregrad",
        ["Microsoft.VSTS.Scheduling.RemainingWork"] = "Restarbeit",
        ["Microsoft.VSTS.Scheduling.OriginalEstimate"] = "Schätzung",
        ["Microsoft.VSTS.Scheduling.StoryPoints"] = "Story Points",
        ["Microsoft.VSTS.Common.AcceptanceCriteria"] = "Akzeptanzkriterien",
    };

    public static string Label(string field) => Labels.TryGetValue(field, out var l) ? l : field;

    public static string Title(string client, ToolDefinition tool, ToolArgs args, string? connection)
    {
        var where = connection is null ? "" : $" in {connection}";
        var id = args.Has("id") ? SafeInt(args, "id") : null;
        return tool.Name switch
        {
            "create_work_item" => $"{client} möchte ein {args.String("type") ?? "Work Item"}{where} anlegen",
            _ when tool.IsWrite && id is not null => $"{client} möchte #{id}{where} ändern",
            _ => $"{client} möchte {tool.Name}{where} ausführen",
        };
    }

    /// <summary>
    /// Summary line; for update_work_item the current values are read first (read call) to show "alt → neu".
    /// </summary>
    public static async Task<string> BuildAsync(ToolDefinition tool, ToolArgs args, AzdoClient? client, CancellationToken ct)
    {
        string detail;
        try
        {
            detail = tool.Name switch
            {
                "update_work_item" => await UpdateSummary(args, client, ct).ConfigureAwait(false),
                "create_work_item" => $"Typ {args.String("type")}, Titel „{Short(args.String("title"))}“" + FieldList(args.Object("fields")),
                "add_work_item_comment" => $"Kommentar: „{Short(args.String("text"))}“",
                "update_work_item_comment" => $"Kommentar {args.String("commentId")} ändern: „{Short(args.String("text"))}“",
                "add_work_item_relation" => $"Verknüpfung {args.String("relationType")} → #{args.String("targetId")}",
                "add_work_item_attachment" => $"Anhang {args.String("fileName") ?? Path.GetFileName(args.String("filePath") ?? "")}",
                _ => GenericSummary(args),
            };
        }
        catch (Exception ex) when (ex is ToolArgumentException or FormatException or InvalidOperationException)
        {
            detail = GenericSummary(args);
        }

        return $"{tool.Name}: {detail}";
    }

    private static async Task<string> UpdateSummary(ToolArgs args, AzdoClient? client, CancellationToken ct)
    {
        var changes = new List<(string Field, JsonNode? Value)>();
        void Add(string field, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                changes.Add((field, JsonValue.Create(value)));
            }
        }

        Add("System.Title", args.String("title"));
        Add("System.State", args.String("state"));
        Add("System.AssignedTo", args.String("assignedTo"));
        foreach (var (k, v) in args.Object("fields") ?? [])
        {
            changes.Add((k, v));
        }

        if (changes.Count == 0)
        {
            return "keine Änderung angegeben";
        }

        JsonNode? current = null;
        if (client is not null && args.Int("id") is { } id)
        {
            try
            {
                var fields = string.Join(',', changes.Select(c => c.Field).Distinct());
                current = (await client.GetAsync($"{client.ProjectUrl}/_apis/wit/workitems/{id}?fields={Uri.EscapeDataString(fields)}&api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false))?["fields"];
            }
            catch (AzdoApiException)
            {
                // Without old values the summary shows only the new ones.
            }
        }

        return string.Join(", ", changes.Select(c =>
        {
            var old = current?[c.Field];
            var newText = Format(c.Value);
            return old is null ? $"{Label(c.Field)} → {newText}" : $"{Label(c.Field)} {Format(old)} → {newText}";
        }));
    }

    private static string FieldList(System.Text.Json.Nodes.JsonObject? fields) =>
        fields is null || fields.Count == 0 ? "" : ", " + string.Join(", ", fields.Select(kv => $"{Label(kv.Key)} = {Format(kv.Value)}"));

    private static string GenericSummary(ToolArgs args) => "Aufruf mit den angegebenen Parametern";

    private static string Format(JsonNode? node) => node switch
    {
        null => "(leer)",
        JsonObject o when o["displayName"] is { } dn => Short(dn.ToString()),
        JsonValue v => Short(v.ToString()),
        _ => Short(node.ToJsonString()),
    };

    private static string Short(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        text = text.ReplaceLineEndings(" ");
        return text.Length <= MaxValue ? text : text[..(MaxValue - 1)] + "…";
    }

    private static int? SafeInt(ToolArgs args, string name)
    {
        try
        {
            return args.Int(name);
        }
        catch (ToolArgumentException)
        {
            return null;
        }
    }
}
