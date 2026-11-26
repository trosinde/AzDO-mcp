using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Infrastructure;

namespace AzdoMcp.Core.Tools;

internal static class WorkItemReadTools
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;
    private const int BatchSize = 200;

    private static readonly string[] SummaryFields =
    [
        "System.Id", "System.WorkItemType", "System.Title", "System.State", "System.AssignedTo",
        "System.AreaPath", "System.IterationPath", "System.ChangedDate",
    ];

    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("get_work_item", ToolGroup.WorkItemsRead,
            "Liest ein Work Item per ID (optional ausgewählte Felder und Relationen).",
            Schema.Create().Int("id", "Work-Item-ID", required: true)
                .StrArray("fields", "Feld-Referenznamen, z. B. System.Title. Ohne Angabe: alle Felder.")
                .Bool("includeRelations", "Relationen mitliefern").Project().Build(),
            GetWorkItem);

        yield return new("query_by_id", ToolGroup.WorkItemsRead,
            "Führt eine gespeicherte Abfrage per ID (GUID) aus und liefert die Work Items.",
            Schema.Create().Str("queryId", "ID der gespeicherten Abfrage", required: true)
                .Int("top", $"Maximale Anzahl (Standard {DefaultLimit})").StrArray("fields", "Felder").Project().Build(),
            QueryById);

        yield return new("execute_wiql", ToolGroup.WorkItemsRead,
            "Führt eine WIQL-Abfrage aus und liefert die gefundenen Work Items.",
            Schema.Create().Str("wiql", "WIQL-Text, z. B. SELECT [System.Id] FROM WorkItems WHERE ...", required: true)
                .Int("top", $"Maximale Anzahl (Standard {DefaultLimit}, höchstens {MaxLimit})").StrArray("fields", "Felder").Project().Build(),
            ExecuteWiql);

        yield return new("query_work_items", ToolGroup.WorkItemsRead,
            "Sucht Work Items nach einfachen Filtern (Typ, Status, Zuweisung, Bereich, Iteration).",
            Schema.Create().Str("type", "Work-Item-Typ, z. B. Bug").Str("state", "Status, z. B. Active")
                .Str("assignedTo", "Zugewiesen an (Name, E-Mail oder @Me)").Str("areaPath", "Bereichspfad (inkl. Unterbereiche)")
                .Str("iterationPath", "Iterationspfad (inkl. Unteriterationen)").Str("titleContains", "Text im Titel")
                .Int("top", "Maximale Anzahl (Standard 50)").Project().Build(),
            QueryWorkItems);

        yield return new("get_work_items_with_relations", ToolGroup.WorkItemsRead,
            "Liest mehrere Work Items per ID inklusive aller Relationen.",
            Schema.Create().IntArray("ids", "Work-Item-IDs", required: true).Project().Build(),
            GetWithRelations);

        yield return new("get_linked_work_items", ToolGroup.WorkItemsRead,
            "Liefert die mit einem Work Item verknüpften Work Items (optional nach Relationstyp gefiltert).",
            Schema.Create().Int("id", "Work-Item-ID", required: true)
                .Str("relationType", "parent, child, related, predecessor, successor, duplicate, duplicate-of, tests, tested-by oder Referenzname").Project().Build(),
            GetLinked);

        yield return new("get_work_item_revisions", ToolGroup.WorkItemsRead,
            "Liefert die Änderungshistorie (Revisionen) eines Work Items.",
            Schema.Create().Int("id", "Work-Item-ID", required: true).Int("top", "Maximale Anzahl Revisionen (Standard 50)").Project().Build(),
            GetRevisions);
    }

    private static async Task<string> GetWorkItem(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var fields = t.Args.StringList("fields");
        var relations = t.Args.Bool("includeRelations");
        var query = relations ? "$expand=relations" : fields.Count > 0 ? "fields=" + Uri.EscapeDataString(string.Join(',', fields)) : "";
        var item = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/wit/workitems/{id}?{query}&api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        return Output(Simplify(item, t.Azdo));
    }

    private static async Task<string> ExecuteWiql(ToolInvocation t)
    {
        var wiql = t.Args.RequiredString("wiql");
        var limit = t.Args.IntOr("top", DefaultLimit, 1, MaxLimit);
        var result = await t.Azdo.PostJsonAsync($"{t.Azdo.ProjectUrl}/_apis/wit/wiql?api-version={AzdoClient.ApiVersion}", new { query = wiql }, t.CancellationToken).ConfigureAwait(false);
        return await FromQueryResult(t, result, limit).ConfigureAwait(false);
    }

    private static async Task<string> QueryById(ToolInvocation t)
    {
        var queryId = t.Args.RequiredString("queryId");
        if (!Guid.TryParse(queryId, out var guid))
        {
            throw new ToolArgumentException("Parameter „queryId“ muss eine GUID sein.");
        }

        var limit = t.Args.IntOr("top", DefaultLimit, 1, MaxLimit);
        var result = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/wit/wiql/{guid}?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        return await FromQueryResult(t, result, limit).ConfigureAwait(false);
    }

    private static async Task<string> QueryWorkItems(ToolInvocation t)
    {
        var conditions = new List<string> { "[System.TeamProject] = @project" };
        void Eq(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                conditions.Add(value.Equals("@me", StringComparison.OrdinalIgnoreCase) ? $"[{field}] = @Me" : $"[{field}] = '{Escape(value)}'");
            }
        }

        Eq("System.WorkItemType", t.Args.String("type"));
        Eq("System.State", t.Args.String("state"));
        Eq("System.AssignedTo", t.Args.String("assignedTo"));
        if (t.Args.String("areaPath") is { Length: > 0 } area)
        {
            conditions.Add($"[System.AreaPath] UNDER '{Escape(area)}'");
        }

        if (t.Args.String("iterationPath") is { Length: > 0 } iteration)
        {
            conditions.Add($"[System.IterationPath] UNDER '{Escape(iteration)}'");
        }

        if (t.Args.String("titleContains") is { Length: > 0 } title)
        {
            conditions.Add($"[System.Title] CONTAINS '{Escape(title)}'");
        }

        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", conditions)} ORDER BY [System.ChangedDate] DESC";
        var limit = t.Args.IntOr("top", 50, 1, MaxLimit);
        var result = await t.Azdo.PostJsonAsync($"{t.Azdo.ProjectUrl}/_apis/wit/wiql?api-version={AzdoClient.ApiVersion}", new { query = wiql }, t.CancellationToken).ConfigureAwait(false);
        return await FromQueryResult(t, result, limit, SummaryFields).ConfigureAwait(false);
    }

    private static async Task<string> GetWithRelations(ToolInvocation t)
    {
        var ids = t.Args.IntList("ids");
        if (ids.Count == 0)
        {
            throw new ToolArgumentException("Parameter „ids“ fehlt.");
        }

        if (ids.Count == 1)
        {
            t.WorkItemId = ids[0];
        }

        var items = await FetchBatch(t, ids.Take(MaxLimit).ToList(), null, expandRelations: true).ConfigureAwait(false);

        // Work item IDs are collection-wide: leave out items of other projects.
        var project = t.Azdo.ProjectName;
        var own = items.Where(i => i?["fields"]?["System.TeamProject"]?.GetValue<string>() is not { } p || string.Equals(p, project, StringComparison.OrdinalIgnoreCase)).ToList();
        var foreign = items.Except(own).Select(i => i!["id"]!.GetValue<int>()).ToList();
        var result = new JsonObject { ["count"] = own.Count, ["workItems"] = new JsonArray(own.Select(i => i?.DeepClone()).ToArray()) };
        if (foreign.Count > 0)
        {
            result["note"] = $"Nicht in {t.ProjectAlias ?? project}, ausgelassen: {string.Join(", ", foreign)}";
        }

        return Output(result);
    }

    private static async Task<string> GetLinked(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var filter = t.Args.String("relationType") is { Length: > 0 } r ? Relations.Resolve(r) : null;
        var item = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/wit/workitems/{id}?$expand=relations&api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);

        var links = new List<(string Rel, int Target)>();
        foreach (var rel in item?["relations"]?.AsArray() ?? [])
        {
            var type = rel?["rel"]?.GetValue<string>();
            var target = Relations.TargetId(rel?["url"]?.GetValue<string>());
            if (type is null || target is null || (filter is not null && !type.Equals(filter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            links.Add((type, target.Value));
        }

        var fetched = links.Count == 0 ? [] : await FetchBatch(t, links.Select(l => l.Target).Distinct().ToList(), SummaryFields, false).ConfigureAwait(false);
        var byId = fetched.OfType<JsonObject>().ToDictionary(o => o["id"]!.GetValue<int>());
        var result = new JsonArray();
        foreach (var (rel, target) in links)
        {
            var entry = new JsonObject { ["relation"] = Relations.Friendly(rel), ["relationType"] = rel, ["id"] = target };
            if (byId.TryGetValue(target, out var wi))
            {
                entry["fields"] = wi["fields"]?.DeepClone();
            }

            result.Add(entry);
        }

        return Output(new JsonObject { ["id"] = id, ["count"] = result.Count, ["linked"] = result });
    }

    private static async Task<string> GetRevisions(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var top = t.Args.IntOr("top", 50, 1, 200);
        var revs = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/wit/workitems/{id}/revisions?$top={top}&api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        var list = new JsonArray();
        foreach (var r in revs?["value"]?.AsArray() ?? [])
        {
            list.Add(new JsonObject
            {
                ["rev"] = r?["rev"]?.DeepClone(),
                ["changedDate"] = r?["fields"]?["System.ChangedDate"]?.DeepClone(),
                ["changedBy"] = Identity(r?["fields"]?["System.ChangedBy"]),
                ["fields"] = r?["fields"]?.DeepClone(),
            });
        }

        return Output(new JsonObject { ["id"] = id, ["count"] = list.Count, ["revisions"] = list });
    }

    private static async Task<string> FromQueryResult(ToolInvocation t, JsonNode? result, int limit, IReadOnlyList<string>? defaultFields = null)
    {
        var ids = new List<int>();
        if (result?["workItems"] is JsonArray flat)
        {
            ids.AddRange(flat.Select(w => w?["id"]?.GetValue<int>()).OfType<int>());
        }
        else if (result?["workItemRelations"] is JsonArray tree)
        {
            foreach (var rel in tree)
            {
                if (rel?["source"]?["id"]?.GetValue<int>() is { } s && !ids.Contains(s))
                {
                    ids.Add(s);
                }

                if (rel?["target"]?["id"]?.GetValue<int>() is { } x && !ids.Contains(x))
                {
                    ids.Add(x);
                }
            }
        }

        var fields = t.Args.StringList("fields");
        var selected = ids.Take(limit).ToList();
        var items = selected.Count == 0 ? [] : await FetchBatch(t, selected, fields.Count > 0 ? fields : defaultFields, false).ConfigureAwait(false);
        var output = new JsonObject { ["count"] = ids.Count, ["returned"] = items.Count, ["workItems"] = items };
        if (ids.Count > selected.Count)
        {
            output["note"] = $"{ids.Count - selected.Count} weitere Work Items ausgelassen (Limit {limit}). Abfrage eingrenzen oder top erhöhen.";
        }

        return Output(output);
    }

    internal static async Task<JsonArray> FetchBatch(ToolInvocation t, IReadOnlyList<int> ids, IReadOnlyList<string>? fields, bool expandRelations)
    {
        var all = new JsonArray();
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var body = new JsonObject { ["ids"] = new JsonArray(chunk.Select(i => (JsonNode)i).ToArray()) };
            if (expandRelations)
            {
                body["$expand"] = "relations";
            }
            else if (fields is { Count: > 0 })
            {
                body["fields"] = new JsonArray(fields.Select(f => (JsonNode)f).ToArray());
            }

            body["errorPolicy"] = "omit";
            var res = await t.Azdo.PostJsonAsync($"{t.Azdo.ProjectUrl}/_apis/wit/workitemsbatch?api-version={AzdoClient.ApiVersion}", body, t.CancellationToken).ConfigureAwait(false);
            foreach (var wi in res?["value"]?.AsArray() ?? [])
            {
                if (wi is not null)
                {
                    all.Add(Simplify(wi, t.Azdo));
                }
            }
        }

        return all;
    }

    internal static JsonObject Simplify(JsonNode? item, AzdoClient client)
    {
        if (item is null)
        {
            return new JsonObject();
        }

        var id = item["id"]?.GetValue<int>() ?? 0;
        var o = new JsonObject
        {
            ["id"] = id,
            ["rev"] = item["rev"]?.DeepClone(),
            ["fields"] = item["fields"]?.DeepClone(),
            ["url"] = WebUrl(client, id),
        };

        if (item["relations"] is JsonArray rels)
        {
            var list = new JsonArray();
            foreach (var r in rels)
            {
                var type = r?["rel"]?.GetValue<string>() ?? "";
                list.Add(new JsonObject
                {
                    ["relation"] = Relations.Friendly(type),
                    ["relationType"] = type,
                    ["targetId"] = Relations.TargetId(r?["url"]?.GetValue<string>()),
                    ["url"] = r?["url"]?.DeepClone(),
                    ["comment"] = r?["attributes"]?["comment"]?.DeepClone(),
                    ["name"] = r?["attributes"]?["name"]?.DeepClone(),
                });
            }

            o["relations"] = list;
        }

        return o;
    }

    internal static string WebUrl(AzdoClient client, int id) => $"{client.ProjectUrl}/_workitems/edit/{id}";

    internal static string Output(JsonNode node) => node.ToJsonString(Json.ToolOutput);

    private static JsonNode? Identity(JsonNode? node) =>
        node is JsonObject o ? o["displayName"]?.DeepClone() : node?.DeepClone();

    internal static string Escape(string value) => value.Replace("'", "''");
}

internal static class Relations
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parent"] = "System.LinkTypes.Hierarchy-Reverse",
        ["child"] = "System.LinkTypes.Hierarchy-Forward",
        ["related"] = "System.LinkTypes.Related",
        ["predecessor"] = "System.LinkTypes.Dependency-Reverse",
        ["successor"] = "System.LinkTypes.Dependency-Forward",
        ["duplicate"] = "System.LinkTypes.Duplicate-Forward",
        ["duplicate-of"] = "System.LinkTypes.Duplicate-Reverse",
        ["tests"] = "Microsoft.VSTS.Common.TestedBy-Reverse",
        ["tested-by"] = "Microsoft.VSTS.Common.TestedBy-Forward",
    };

    public static string Resolve(string relation) =>
        Map.TryGetValue(relation.Trim(), out var r) ? r : relation.Trim();

    public static string Friendly(string referenceName) =>
        Map.FirstOrDefault(kv => kv.Value.Equals(referenceName, StringComparison.OrdinalIgnoreCase)).Key ?? referenceName;

    public static int? TargetId(string? url)
    {
        if (url is null || !url.Contains("/workItems/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(url[(url.LastIndexOf('/') + 1)..], out var id) ? id : null;
    }
}
