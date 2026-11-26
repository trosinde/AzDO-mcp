using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

internal static class PipelineTools
{
    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("get_pipelines", ToolGroup.Pipelines,
            "Listet Build-Pipelines und Release-Definitionen (ID, Name, Art, Pfad). Filter: name (enthält), kind (build oder release).",
            Schema.Create().Str("name", "Name enthält").Str("kind", "build oder release (Standard: beide)", false, "build", "release").Project().Build(),
            GetPipelines);

        yield return new("get_builds", ToolGroup.Pipelines,
            "Builds, neueste zuerst, mit Nummer, Pipeline, Branch, Tags, Status und Ergebnis. Filter: pipelineId, branch, tags, result (succeeded, partiallySucceeded, failed, canceled), from/to (Standard letzte 30 Tage).",
            Schema.Create().Int("pipelineId", "Build-Pipeline (Definition)").Str("branch", "Branch, z. B. main").StrArray("tags", "Nur Builds mit allen diesen Tags")
                .Str("result", "succeeded, partiallySucceeded, failed oder canceled", false, "succeeded", "partiallySucceeded", "failed", "canceled")
                .Str("from", "Ab Datum (ISO 8601)").Str("to", "Bis Datum").Int("top", "Maximale Anzahl (Standard 100, höchstens 1000)").Project().Build(),
            GetBuilds);

        yield return new("get_releases", ToolGroup.Pipelines,
            "Releases, neueste zuerst, mit Definition und Stages (Name, Status). Filter: definitionId, from/to (Standard letzte 30 Tage).",
            Schema.Create().Int("definitionId", "Release-Definition").Str("from", "Ab Datum (ISO 8601)").Str("to", "Bis Datum")
                .Int("top", "Maximale Anzahl (Standard 100, höchstens 1000)").Project().Build(),
            GetReleases);
    }

    /// <summary>Release Management lives on vsrm.* for Azure DevOps Services and on the collection itself on-prem.</summary>
    internal static string ReleaseBase(Connection c)
    {
        var url = c.CollectionBase.TrimEnd('/');
        if (c.ServerType != ServerType.Services || !Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            return url;
        }

        if (u.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{u.Scheme}://vsrm.dev.azure.com{u.AbsolutePath.TrimEnd('/')}";
        }

        if (u.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase) && !u.Host.Contains(".vsrm.", StringComparison.OrdinalIgnoreCase))
        {
            var org = u.Host[..u.Host.IndexOf('.', StringComparison.Ordinal)];
            return $"{u.Scheme}://{org}.vsrm.visualstudio.com{u.AbsolutePath.TrimEnd('/')}";
        }

        return url;
    }

    private static string ReleaseProjectUrl(ToolInvocation t) => $"{ReleaseBase(t.Azdo.Connection)}/{Uri.EscapeDataString(t.Azdo.ProjectName!)}";

    private static async Task<string> GetPipelines(ToolInvocation t)
    {
        var name = t.Args.String("name") is { Length: > 0 } n ? n : null;
        var kind = t.Args.String("kind");
        var list = new JsonArray();
        var notes = new List<string>();
        var processed = 0;
        if (kind is null or "" or "build")
        {
            var res = await Paging.TokenAsync(t.Azdo,
                token => $"{t.Azdo.ProjectUrl}/_apis/build/definitions?$top=500&api-version={AzdoClient.ApiVersion}" + (token is null ? "" : "&continuationToken=" + Paging.Q(token)),
                5000, d => Contains(d, name), t.CancellationToken).ConfigureAwait(false);
            processed += res.Processed;
            foreach (var d in res.Items)
            {
                list.Add(new JsonObject { ["id"] = Shape.Int(d["id"]), ["name"] = Shape.Str(d["name"]), ["kind"] = "build", ["path"] = Shape.Str(d["path"]) });
            }
        }

        if (kind is null or "" or "release")
        {
            try
            {
                var res = await Paging.TokenAsync(t.Azdo,
                    token => $"{ReleaseProjectUrl(t)}/_apis/release/definitions?$top=500&api-version={AzdoClient.ApiVersion}" + (token is null ? "" : "&continuationToken=" + Paging.Q(token)),
                    5000, d => Contains(d, name), t.CancellationToken).ConfigureAwait(false);
                processed += res.Processed;
                foreach (var d in res.Items)
                {
                    list.Add(new JsonObject { ["id"] = Shape.Int(d["id"]), ["name"] = Shape.Str(d["name"]), ["kind"] = "release", ["path"] = Shape.Str(d["path"]) });
                }
            }
            catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotAvailable or AzdoErrorKind.NotFound)
            {
                notes.Add("Release-Definitionen: auf diesem Server nicht verfügbar");
            }
        }

        return Output(Shape.Envelope("pipelines", list, processed, false, notes));
    }

    private static Walk Contains(JsonNode d, string? name) =>
        name is null || (Shape.Str(d["name"]) ?? "").Contains(name, StringComparison.OrdinalIgnoreCase) ? Walk.Keep : Walk.Skip;

    private static async Task<string> GetBuilds(ToolInvocation t)
    {
        var a = t.Args;
        var to = Shape.DateArg(a, "to") ?? t.Time.GetUtcNow();
        var from = Shape.DateArg(a, "from") ?? to.AddDays(-TestTools.DefaultDays);
        var load = await LoadBuildsAsync(t, a.Int("pipelineId"), a.String("branch") is { Length: > 0 } b ? b : null, a.StringList("tags"),
            a.String("result") is { Length: > 0 } r ? r : null, from, to, a.IntOr("top", 100, 1, 1000)).ConfigureAwait(false);
        return Output(Shape.Envelope("builds", Shape.Array(load.Items.Select(x => (JsonNode?)CompactBuild(x))), load.Processed, load.Truncated));
    }

    internal static Task<Collected> LoadBuildsAsync(ToolInvocation t, int? pipelineId, string? branch, IReadOnlyList<string>? tags, string? result, DateTimeOffset from, DateTimeOffset to, int top)
    {
        var q = new List<string>
        {
            "minTime=" + Paging.Q(Shape.Iso(from)!),
            "maxTime=" + Paging.Q(Shape.Iso(to)!),
            "queryOrder=finishTimeDescending",
        };
        if (pipelineId is { } p)
        {
            q.Add("definitions=" + p);
        }

        if (branch is { } br)
        {
            q.Add("branchName=" + Paging.Q("refs/heads/" + Shape.Branch(br)));
        }

        if (tags is { Count: > 0 })
        {
            q.Add("tagFilters=" + Paging.Q(string.Join(',', tags)));
        }

        if (result is { } r)
        {
            q.Add("resultFilter=" + Paging.Q(r));
        }

        var query = string.Join('&', q);
        return Paging.TokenAsync(t.Azdo,
            token => $"{t.Azdo.ProjectUrl}/_apis/build/builds?{query}&$top={Math.Min(top, 500)}&api-version={AzdoClient.ApiVersion}" + (token is null ? "" : "&continuationToken=" + Paging.Q(token)),
            top, null, t.CancellationToken);
    }

    internal static JsonObject CompactBuild(JsonNode b) => new()
    {
        ["id"] = Shape.Int(b["id"]),
        ["number"] = Shape.Str(b["buildNumber"]),
        ["pipeline"] = new JsonObject { ["id"] = Shape.Int(b["definition"]?["id"]), ["name"] = Shape.Str(b["definition"]?["name"]) },
        ["branch"] = Shape.Branch(Shape.Str(b["sourceBranch"])),
        ["tags"] = Shape.Strings((b["tags"]?.AsArray() ?? []).Select(Shape.Str).OfType<string>()),
        ["status"] = Shape.Str(b["status"]),
        ["result"] = Shape.Str(b["result"]),
        ["reason"] = Shape.Str(b["reason"]),
        ["requestedFor"] = Shape.Person(b["requestedFor"]),
        ["started"] = Shape.Iso(Shape.Date(b["startTime"])),
        ["finished"] = Shape.Iso(Shape.Date(b["finishTime"])),
        ["webUrl"] = Shape.Str(b["_links"]?["web"]?["href"]),
    };

    private static async Task<string> GetReleases(ToolInvocation t)
    {
        var a = t.Args;
        var to = Shape.DateArg(a, "to") ?? t.Time.GetUtcNow();
        var from = Shape.DateArg(a, "from") ?? to.AddDays(-TestTools.DefaultDays);
        var q = new List<string>
        {
            "minCreatedTime=" + Paging.Q(Shape.Iso(from)!),
            "maxCreatedTime=" + Paging.Q(Shape.Iso(to)!),
            "$expand=environments",
            "queryOrder=descending",
        };
        if (a.Int("definitionId") is { } d)
        {
            q.Add("definitionId=" + d);
        }

        var top = a.IntOr("top", 100, 1, 1000);
        var query = string.Join('&', q);
        Collected res;
        try
        {
            res = await Paging.TokenAsync(t.Azdo,
                token => $"{ReleaseProjectUrl(t)}/_apis/release/releases?{query}&$top={Math.Min(top, 100)}&api-version={AzdoClient.ApiVersion}" + (token is null ? "" : "&continuationToken=" + Paging.Q(token)),
                top, null, t.CancellationToken).ConfigureAwait(false);
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotFound)
        {
            throw new AzdoApiException(AzdoErrorKind.NotAvailable, ex.StatusCode, "Auf diesem Server nicht verfügbar.");
        }

        var list = Shape.Array(res.Items.Select(r => (JsonNode?)new JsonObject
        {
            ["id"] = Shape.Int(r["id"]),
            ["name"] = Shape.Str(r["name"]),
            ["definition"] = new JsonObject { ["id"] = Shape.Int(r["releaseDefinition"]?["id"]), ["name"] = Shape.Str(r["releaseDefinition"]?["name"]) },
            ["status"] = Shape.Str(r["status"]),
            ["created"] = Shape.Iso(Shape.Date(r["createdOn"])),
            ["createdBy"] = Shape.Person(r["createdBy"]),
            ["stages"] = Shape.Array((r["environments"]?.AsArray() ?? []).Select(e => (JsonNode?)new JsonObject
            {
                ["id"] = Shape.Int(e?["id"]),
                ["name"] = Shape.Str(e?["name"]),
                ["status"] = Shape.Str(e?["status"]),
            })),
            ["webUrl"] = Shape.Str(r["_links"]?["web"]?["href"]),
        }));
        return Output(Shape.Envelope("releases", list, res.Processed, res.Truncated));
    }
}
