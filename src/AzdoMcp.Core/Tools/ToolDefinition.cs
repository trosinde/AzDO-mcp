using System.Text.Json;
using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;

using AzdoMcp.Core.Permissions;

namespace AzdoMcp.Core.Tools;

public sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>Context for one tool call.</summary>
public sealed class ToolInvocation
{
    public required ToolArgs Args { get; init; }

    /// <summary>Null only for tools with <see cref="ToolDefinition.NeedsConnection"/> = false.</summary>
    public AzdoClient? Client { get; init; }

    public required ConnectionStore Store { get; init; }

    public required TimeProvider Time { get; init; }

    /// <summary>Kurzname of the target project (for messages).</summary>
    public string? ProjectAlias { get; init; }

    /// <summary>Target project; null for tools without a connection.</summary>
    public ProjectRef? Target { get; init; }

    /// <summary>False only when the effective Lesen of <paramref name="area"/> was checked and denied.</summary>
    public bool CanRead(AccessArea area) => Target is null || Target.Effective[area].Read != Check.No;

    /// <summary>Note for data of an area that was left out.</summary>
    public static string LeftOut(AccessArea area) => $"{area.Display()}: kein Lesezugriff – ausgelassen";

    /// <summary>Permission settings at call time (for status texts).</summary>
    public PermissionSettings Permissions { get; init; } = new();

    public CancellationToken CancellationToken { get; init; }

    /// <summary>Set by handlers so the audit record can show the item ID.</summary>
    public int? WorkItemId { get; set; }

    public AzdoClient Azdo => Client ?? throw new InvalidOperationException("Tool braucht eine Verbindung.");
}

public sealed record ToolDefinition(
    string Name,
    ToolGroup Group,
    string Description,
    JsonObject InputSchema,
    Func<ToolInvocation, Task<string>> Handler,
    bool NeedsConnection = true)
{
    public bool IsWrite => Group == ToolGroup.WorkItemsWrite;

    public JsonElement InputSchemaElement => JsonSerializer.SerializeToElement(InputSchema);
}

/// <summary>Typed access to tool arguments with German error messages.</summary>
public sealed class ToolArgs(IReadOnlyDictionary<string, JsonElement>? values)
{
    private readonly IReadOnlyDictionary<string, JsonElement> _values = values ?? new Dictionary<string, JsonElement>();

    public bool Has(string name) => _values.TryGetValue(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    public string? String(string name)
    {
        if (!_values.TryGetValue(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    public string RequiredString(string name) =>
        String(name) is { Length: > 0 } s ? s : throw Missing(name);

    public bool Bool(string name)
    {
        if (!_values.TryGetValue(name, out var v))
        {
            return false;
        }

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false,
        };
    }

    public int? Int(string name)
    {
        if (!_values.TryGetValue(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
        {
            return i;
        }

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j))
        {
            return j;
        }

        throw new ToolArgumentException($"Parameter „{name}“ muss eine ganze Zahl sein.");
    }

    public int RequiredInt(string name) => Int(name) ?? throw Missing(name);

    public int IntOr(string name, int fallback, int min = 1, int max = int.MaxValue)
    {
        var v = Int(name) ?? fallback;
        return Math.Clamp(v, min, max);
    }

    public IReadOnlyList<int> IntList(string name)
    {
        if (!_values.TryGetValue(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (v.ValueKind == JsonValueKind.Array)
        {
            return v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!)).ToList();
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            return v.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
        }

        throw new ToolArgumentException($"Parameter „{name}“ muss eine Liste von IDs sein.");
    }

    public IReadOnlyList<string> StringList(string name)
    {
        if (!_values.TryGetValue(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        return v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Select(e => e.ToString()).ToList(),
            JsonValueKind.String => v.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            _ => throw new ToolArgumentException($"Parameter „{name}“ muss eine Liste sein."),
        };
    }

    public JsonObject? Object(string name)
    {
        if (!_values.TryGetValue(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Object)
        {
            return JsonNode.Parse(v.GetRawText())!.AsObject();
        }

        if (v.ValueKind == JsonValueKind.String && JsonNode.Parse(v.GetString()!) is JsonObject o)
        {
            return o;
        }

        throw new ToolArgumentException($"Parameter „{name}“ muss ein Objekt sein.");
    }

    private static ToolArgumentException Missing(string name) => new($"Parameter „{name}“ fehlt.");
}

/// <summary>Small builder for JSON input schemas.</summary>
internal sealed class Schema
{
    private readonly JsonObject _props = new();
    private readonly JsonArray _required = new();

    public static Schema Create() => new();

    public Schema Int(string name, string description, bool required = false) => Add(name, "integer", description, required);

    public Schema Str(string name, string description, bool required = false, params string[] allowed)
    {
        var p = new JsonObject { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0)
        {
            p["enum"] = new JsonArray(allowed.Select(a => (JsonNode)a).ToArray());
        }

        return Add(name, p, required);
    }

    public Schema Bool(string name, string description) => Add(name, "boolean", description, false);

    public Schema IntArray(string name, string description, bool required = false) =>
        Add(name, new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" }, ["description"] = description }, required);

    public Schema StrArray(string name, string description) =>
        Add(name, new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description }, false);

    public Schema Obj(string name, string description, bool required = false) =>
        Add(name, new JsonObject { ["type"] = "object", ["description"] = description, ["additionalProperties"] = true }, required);

    public Schema Project() =>
        Str("project", "Kurzname des Azure-DevOps-Projekts (siehe list_projects). Ohne Angabe: Standardprojekt.");

    public JsonObject Build()
    {
        var o = new JsonObject { ["type"] = "object", ["properties"] = _props.DeepClone() };
        if (_required.Count > 0)
        {
            o["required"] = _required.DeepClone();
        }

        return o;
    }

    private Schema Add(string name, string type, string description, bool required) =>
        Add(name, new JsonObject { ["type"] = type, ["description"] = description }, required);

    private Schema Add(string name, JsonObject p, bool required)
    {
        _props[name] = p;
        if (required)
        {
            _required.Add(name);
        }

        return this;
    }
}
