using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;

namespace AzdoMcp.Core.Tools;

/// <summary>Turns raw Azure DevOps JSON into the compact fields the analysis tools return.</summary>
internal static class Shape
{
    /// <summary>{ name, login } – no avatar, descriptor or identity URL.</summary>
    public static JsonObject? Person(JsonNode? identity)
    {
        if (identity is not JsonObject)
        {
            return null;
        }

        return new JsonObject { ["name"] = Str(identity["displayName"]) ?? Str(identity["name"]), ["login"] = Login(identity) };
    }

    /// <summary>uniqueName, else the identity ID; used for grouping people.</summary>
    public static string? Login(JsonNode? identity) =>
        Str(identity?["uniqueName"]) is { Length: > 0 } u ? u : Str(identity?["id"]);

    /// <summary>Text for a reviewer vote.</summary>
    public static string VoteText(int vote) => vote switch
    {
        >= 10 => "approved",
        >= 5 => "approved with suggestions",
        <= -10 => "rejected",
        <= -5 => "waiting",
        _ => "no vote",
    };

    public static string? Str(JsonNode? node)
    {
        if (node is not JsonValue v)
        {
            return null;
        }

        return v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
            _ => null,
        };
    }

    public static int? Int(JsonNode? node)
    {
        var s = Str(node);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    public static long? Long(JsonNode? node)
    {
        var s = Str(node);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    public static DateTimeOffset? Date(JsonNode? node)
    {
        var s = Str(node);
        if (s is null || !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
        {
            return null;
        }

        // Azure DevOps uses 0001-01-01 for "not set".
        return d.Year < 1900 ? null : d;
    }

    public static string? Iso(DateTimeOffset? d) => d?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>refs/heads/main → main</summary>
    public static string? Branch(string? refName) =>
        refName is null ? null : refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..] : refName;

    /// <summary>Cuts long text and removes anything that looks like a secret.</summary>
    public static string? Shorten(string? text, int max, string? pat = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var clean = SecretScrubber.Scrub(text, pat);
        return clean.Length <= max ? clean : clean[..max] + "…";
    }

    public static double Hours(DateTimeOffset from, DateTimeOffset to) => Math.Round((to - from).TotalHours, 1);

    /// <summary>Parses an ISO 8601 argument; error names the parameter.</summary>
    public static DateTimeOffset? DateArg(ToolArgs args, string name)
    {
        if (args.String(name) is not { Length: > 0 } s)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
        {
            throw new ToolArgumentException($"Parameter „{name}“ muss ein Datum im Format JJJJ-MM-TT sein.");
        }

        return d;
    }

    /// <summary>A person filter matches display name, login or ID (case-insensitive).</summary>
    public static bool Matches(JsonNode? identity, string filter) =>
        string.Equals(Str(identity?["displayName"]), filter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Str(identity?["uniqueName"]), filter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Str(identity?["id"]), filter, StringComparison.OrdinalIgnoreCase);

    public static JsonArray Array(IEnumerable<JsonNode?> items) => new(items.ToArray());

    public static JsonArray Strings(IEnumerable<string> items) => new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

    public static JsonArray Ints(IEnumerable<int> items) => new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

    /// <summary>Common result envelope: data plus processed/truncated and notes.</summary>
    public static JsonObject Envelope(string listName, JsonArray items, int processed, bool truncated, IReadOnlyCollection<string>? notes = null)
    {
        var o = new JsonObject
        {
            ["count"] = items.Count,
            ["processed"] = processed,
            ["truncated"] = truncated,
            [listName] = items,
        };
        if (notes is { Count: > 0 })
        {
            o["notes"] = Strings(notes);
        }

        return o;
    }
}

/// <summary>Bounded parallel loading of details.</summary>
internal static class Expand
{
    public const int Parallel = 4;

    public static Task ForEachAsync<T>(IEnumerable<T> items, Func<T, CancellationToken, Task> body, CancellationToken ct) =>
        System.Threading.Tasks.Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = ct }, async (item, token) => await body(item, token).ConfigureAwait(false));
}
