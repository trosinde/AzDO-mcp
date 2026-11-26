using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

internal sealed record RunFilter(
    DateTimeOffset From,
    DateTimeOffset To,
    int? PlanId = null,
    int? BuildId = null,
    int? ReleaseId = null,
    int? ReleaseDefinitionId = null,
    int? PipelineId = null,
    string? Branch = null,
    string? RunName = null,
    string? State = null,
    IReadOnlyList<string>? Tags = null);

internal sealed record RunLoad(List<JsonNode> Runs, int Processed, bool Truncated, List<string> Notes);

internal sealed record ResultLoad(List<JsonObject> Results, int Processed, bool Truncated, List<string> Notes);

internal static class TestTools
{
    public const int DefaultDays = 30;
    private const int ErrorMax = 1000;
    private const int MaxResultsLoaded = 20000;

    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("get_test_plans", ToolGroup.Test, "Listet die Testpläne des Projekts (ID, Name, Status, Root-Suite).",
            Schema.Create().Project().Build(), GetPlans);

        yield return new("get_test_runs", ToolGroup.Test,
            "Testläufe, neueste zuerst, mit Build (Nummer, Pipeline, Branch) und Release (Name, Stage). Filter: from/to (Standard letzte 30 Tage), planId, buildId, releaseId, releaseDefinitionId, pipelineId, branch, tags, runName, state.",
            RunSchema().Int("top", "Maximale Anzahl (Standard 100, höchstens 1000)").Project().Build(), GetRuns);

        yield return new("get_test_results", ToolGroup.Test,
            "Testergebnisse eines Laufs, Builds, Releases oder Testplans (genau eines davon). Beginnt mit der Anzahl je Ergebnis. latest=testCase+configuration liefert nur das neueste Ergebnis je Testfall und Konfiguration.",
            Schema.Create().Int("runId", "ID des Testlaufs").Int("buildId", "Alle Läufe dieses Builds").Int("releaseId", "Alle Läufe dieses Releases")
                .Int("planId", "Alle Läufe dieses Testplans im Zeitraum")
                .Str("from", "Ab Datum für releaseId/planId (Standard: vor 30 Tagen)").Str("to", "Bis Datum (Standard: jetzt)")
                .Str("outcome", "Nur dieses Ergebnis, z. B. Failed")
                .Str("latest", "none (Standard) oder testCase+configuration", false, "none", "testCase+configuration")
                .Int("top", "Maximale Anzahl (Standard 500, höchstens 5000)").Project().Build(), GetResults);
    }

    internal static Schema RunSchema() =>
        Schema.Create()
            .Str("from", "Ab Datum (ISO 8601, Standard: vor 30 Tagen)").Str("to", "Bis Datum (Standard: jetzt)")
            .Int("planId", "Testplan").Int("buildId", "Build").Int("releaseId", "Release").Int("releaseDefinitionId", "Release-Definition")
            .Int("pipelineId", "Build-Pipeline (Definition)").Str("branch", "Branch des Builds, z. B. main")
            .StrArray("tags", "Nur Läufe von Builds mit diesen Tags (braucht Pipelines Lesen)")
            .Str("runName", "Laufname enthält").Str("state", "z. B. Completed, InProgress");

    internal static RunFilter RunFilterFrom(ToolInvocation t)
    {
        var a = t.Args;
        var to = Shape.DateArg(a, "to") ?? t.Time.GetUtcNow();
        var from = Shape.DateArg(a, "from") ?? to.AddDays(-DefaultDays);
        if (from > to)
        {
            throw new ToolArgumentException("„from“ liegt nach „to“.");
        }

        return new(from, to, a.Int("planId"), a.Int("buildId"), a.Int("releaseId"), a.Int("releaseDefinitionId"), a.Int("pipelineId"),
            a.String("branch") is { Length: > 0 } b ? Shape.Branch(b) : null,
            a.String("runName") is { Length: > 0 } n ? n : null,
            a.String("state") is { Length: > 0 } s ? s : null,
            a.StringList("tags"));
    }

    private static async Task<string> GetPlans(ToolInvocation t)
    {
        var res = await Paging.TokenAsync(t.Azdo,
            token => $"{t.Azdo.ProjectUrl}/_apis/testplan/plans?api-version={AzdoClient.ApiVersion}" + (token is null ? "" : "&continuationToken=" + Paging.Q(token)),
            1000, null, t.CancellationToken).ConfigureAwait(false);
        var list = Shape.Array(res.Items.Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = Shape.Int(p["id"]),
            ["name"] = Shape.Str(p["name"]),
            ["state"] = Shape.Str(p["state"]),
            ["iteration"] = Shape.Str(p["iteration"]),
            ["rootSuite"] = p["rootSuite"] is { } rs ? new JsonObject { ["id"] = Shape.Int(rs["id"]), ["name"] = Shape.Str(rs["name"]) } : null,
            ["updated"] = Shape.Iso(Shape.Date(p["updatedDate"])),
        }));
        return Output(Shape.Envelope("plans", list, res.Processed, res.Truncated));
    }

    private const int DetailCap = 200;

    private static async Task<string> GetRuns(ToolInvocation t)
    {
        var load = await LoadRunsAsync(t, RunFilterFrom(t), t.Args.IntOr("top", 100, 1, 1000)).ConfigureAwait(false);
        var runs = load.Runs.ToArray();
        var notes = load.Notes.ToList();

        // The run query returns no build, release or plan reference; load the run itself for those.
        var missing = Enumerable.Range(0, runs.Length)
            .Where(i => Shape.Int(runs[i]["build"]?["id"]) is null or 0 && Shape.Int(runs[i]["release"]?["id"]) is null or 0 && Shape.Int(runs[i]["plan"]?["id"]) is null or 0)
            .ToList();
        await Expand.ForEachAsync(missing.Take(DetailCap), async (i, ct) =>
        {
            if (Shape.Int(runs[i]["id"]) is { } id)
            {
                try
                {
                    var full = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/test/runs/{id}?includeDetails=true&api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
                    if (full is not null)
                    {
                        runs[i] = full;
                    }
                }
                catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotFound)
                {
                    // Deleted meanwhile: keep the short entry.
                }
            }
        }, t.CancellationToken).ConfigureAwait(false);
        if (missing.Count > DetailCap)
        {
            notes.Add($"Build/Release für {missing.Count - DetailCap} Läufe nicht geladen");
        }

        return Output(Shape.Envelope("runs", Shape.Array(runs.Select(r => (JsonNode?)CompactRun(t, r))), load.Processed, load.Truncated, notes));
    }

    private static async Task<string> GetResults(ToolInvocation t)
    {
        var a = t.Args;
        var chosen = new[] { "runId", "buildId", "releaseId", "planId" }.Where(a.Has).ToList();
        if (chosen.Count != 1)
        {
            throw new ToolArgumentException("Genau eines von runId, buildId, releaseId oder planId angeben.");
        }

        var to = Shape.DateArg(a, "to") ?? t.Time.GetUtcNow();
        var from = Shape.DateArg(a, "from") ?? to.AddDays(-DefaultDays);
        var filter = new RunFilter(from, to, a.Int("planId"), a.Int("buildId"), a.Int("releaseId"));
        var latest = Latest(a.String("latest"));
        var top = a.IntOr("top", 500, 1, 5000);

        var load = a.Int("runId") is { } runId
            ? await LoadResultsAsync(t, [runId], a.String("outcome"), latest, t.CancellationToken).ConfigureAwait(false)
            : await LoadResultsAsync(t, filter, a.String("outcome"), latest).ConfigureAwait(false);

        var counts = Counts(load.Results);
        var shown = load.Results.Take(top).ToList();
        var o = new JsonObject
        {
            ["outcomes"] = counts,
            ["count"] = shown.Count,
            ["total"] = load.Results.Count,
            ["processed"] = load.Processed,
            ["truncated"] = load.Truncated || load.Results.Count > top,
            ["results"] = Shape.Array(shown),
        };
        if (load.Notes.Count > 0)
        {
            o["notes"] = Shape.Strings(load.Notes);
        }

        return Output(o);
    }

    internal static bool Latest(string? value) => value switch
    {
        null or "" or "none" => false,
        "testCase+configuration" => true,
        _ => throw new ToolArgumentException("„latest“ muss none oder testCase+configuration sein."),
    };

    internal static JsonObject Counts(IEnumerable<JsonObject> results)
    {
        var o = new JsonObject();
        foreach (var g in results.GroupBy(r => Shape.Str(r["outcome"]) ?? "Unknown").OrderByDescending(g => g.Count()))
        {
            o[g.Key] = g.Count();
        }

        return o;
    }

    /// <summary>Runs matching the filter, newest first.</summary>
    internal static async Task<RunLoad> LoadRunsAsync(ToolInvocation t, RunFilter f, int top)
    {
        var notes = new List<string>();
        var ct = t.CancellationToken;
        IReadOnlyList<int>? buildIds = f.BuildId is { } b ? [b] : null;
        if (f.Tags is { Count: > 0 })
        {
            if (!t.CanRead(AccessArea.Pipelines))
            {
                throw new ToolArgumentException("Der Filter „tags“ braucht Pipelines Lesen – in diesem Projekt nicht erlaubt.");
            }

            var builds = await PipelineTools.LoadBuildsAsync(t, f.PipelineId, f.Branch, f.Tags, null, f.From, f.To, 1000).ConfigureAwait(false);
            buildIds = builds.Items.Select(x => Shape.Int(x["id"])).OfType<int>().ToList();
            if (buildIds.Count == 0)
            {
                return new([], builds.Processed, false, ["Kein Build mit diesen Tags im Zeitraum."]);
            }
        }

        var runs = new Dictionary<int, JsonNode>();
        var processed = 0;
        var truncated = false;

        if (f.BuildId is not null && f.Tags is not { Count: > 0 } && f.PlanId is null && f.ReleaseId is null)
        {
            // One build: the plain list endpoint takes buildUri and needs no date window.
            var res = await Paging.SkipAsync(t.Azdo,
                (skip, pageTop) => $"{t.Azdo.ProjectUrl}/_apis/test/runs?buildUri={Paging.Q("vstfs:///Build/Build/" + f.BuildId)}&includeRunDetails=true&$top={pageTop}&$skip={skip}&api-version={AzdoClient.ApiVersion}",
                100, 5000, null, ct).ConfigureAwait(false);
            processed = res.Processed;
            foreach (var r in res.Items.Where(r => Keep(r, f)))
            {
                if (Shape.Int(r["id"]) is { } id)
                {
                    runs[id] = r;
                }
            }
        }
        else
        {
            var chunks = buildIds is null ? [null] : buildIds.Chunk(50).Select(c => (int[]?)c).ToList();
            foreach (var (wFrom, wTo) in Paging.Windows(f.From, f.To))
            {
                foreach (var chunk in chunks)
                {
                    var q = new List<string>
                    {
                        "minLastUpdatedDate=" + Paging.Q(Shape.Iso(wFrom)!),
                        "maxLastUpdatedDate=" + Paging.Q(Shape.Iso(wTo)!),
                    };
                    if (f.PlanId is { } p)
                    {
                        q.Add("planIds=" + p);
                    }

                    if (chunk is not null)
                    {
                        q.Add("buildIds=" + string.Join(',', chunk));
                    }

                    if (f.ReleaseId is { } rel)
                    {
                        q.Add("releaseIds=" + rel);
                    }

                    if (f.ReleaseDefinitionId is { } rd)
                    {
                        q.Add("releaseDefIds=" + rd);
                    }

                    if (f.PipelineId is { } pd && chunk is null)
                    {
                        q.Add("buildDefIds=" + pd);
                    }

                    if (f.Branch is { } br && chunk is null)
                    {
                        q.Add("branchName=" + Paging.Q("refs/heads/" + br));
                    }

                    if (f.RunName is { } rn)
                    {
                        q.Add("runTitle=" + Paging.Q(rn));
                    }

                    if (f.State is { } st)
                    {
                        q.Add("state=" + Paging.Q(st));
                    }

                    var query = string.Join('&', q);
                    var res = await Paging.TokenAsync(t.Azdo,
                        token => $"{t.Azdo.ProjectUrl}/_apis/test/runs?{query}&$top=100&api-version={AzdoClient.ApiVersion}" + (token is null ? "" : "&continuationToken=" + Paging.Q(token)),
                        5000, null, ct).ConfigureAwait(false);
                    processed += res.Processed;
                    truncated |= res.Truncated;
                    foreach (var r in res.Items.Where(r => Keep(r, f)))
                    {
                        if (Shape.Int(r["id"]) is { } id)
                        {
                            runs.TryAdd(id, r);
                        }
                    }
                }

                // Windows go back in time; once enough runs are found, older windows cannot be newer.
                if (runs.Count >= top)
                {
                    truncated = true;
                    break;
                }
            }
        }

        var ordered = runs.Values
            .OrderByDescending(r => Shape.Date(r["startedDate"]) ?? Shape.Date(r["createdDate"]) ?? DateTimeOffset.MinValue)
            .ThenByDescending(r => Shape.Int(r["id"]))
            .ToList();
        if (ordered.Count > top)
        {
            truncated = true;
            ordered = ordered.Take(top).ToList();
        }
        else if (ordered.Count < top)
        {
            truncated = false;
        }

        return new(ordered, processed, truncated, notes);
    }

    private static bool Keep(JsonNode run, RunFilter f)
    {
        if (f.RunName is { } rn && !(Shape.Str(run["name"]) ?? "").Contains(rn, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (f.State is { } st && !string.Equals(Shape.Str(run["state"]), st, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (f.ReleaseId is { } rel && Shape.Int(run["release"]?["id"]) is { } runRel && runRel != rel)
        {
            return false;
        }

        if (f.PlanId is { } plan && Shape.Int(run["plan"]?["id"]) is { } runPlan && runPlan != plan)
        {
            return false;
        }

        return true;
    }

    internal static JsonObject CompactRun(ToolInvocation t, JsonNode r)
    {
        var counts = new JsonObject
        {
            ["total"] = Shape.Int(r["totalTests"]),
            ["passed"] = Shape.Int(r["passedTests"]),
            ["notApplicable"] = Shape.Int(r["notApplicableTests"]),
            ["incomplete"] = Shape.Int(r["incompleteTests"]),
            ["unanalyzed"] = Shape.Int(r["unanalyzedTests"]),
        };
        foreach (var s in r["runStatistics"]?.AsArray() ?? [])
        {
            if (Shape.Str(s?["outcome"]) is { } outcome && Shape.Int(s?["count"]) is { } n)
            {
                counts[char.ToLowerInvariant(outcome[0]) + outcome[1..]] = n;
            }
        }

        var bc = r["buildConfiguration"];
        return new JsonObject
        {
            ["id"] = Shape.Int(r["id"]),
            ["name"] = Shape.Str(r["name"])?.Trim(),
            ["state"] = Shape.Str(r["state"]),
            ["isAutomated"] = r["isAutomated"]?.DeepClone(),
            ["started"] = Shape.Iso(Shape.Date(r["startedDate"])),
            ["completed"] = Shape.Iso(Shape.Date(r["completedDate"])),
            ["counts"] = counts,
            ["plan"] = r["plan"] is { } p && Shape.Int(p["id"]) is { } pid and > 0 ? new JsonObject { ["id"] = pid, ["name"] = Shape.Str(p["name"]) } : null,
            ["build"] = r["build"] is { } b && Shape.Int(b["id"]) is { } bid and > 0
                ? new JsonObject
                {
                    ["id"] = bid,
                    ["number"] = Shape.Str(b["name"]) ?? Shape.Str(bc?["number"]),
                    ["pipeline"] = Shape.Int(bc?["buildDefinitionId"]) ?? Shape.Int(r["pipelineReference"]?["pipelineId"]),
                    ["branch"] = Shape.Branch(Shape.Str(bc?["branchName"])),
                }
                : null,
            ["release"] = Release(r["release"]),
            ["webUrl"] = Shape.Str(r["webAccessUrl"]),
        };
    }

    private static JsonObject? Release(JsonNode? rel) =>
        rel is not null && Shape.Int(rel["id"]) is { } id and > 0
            ? new JsonObject
            {
                ["id"] = id,
                ["name"] = Shape.Str(rel["name"]),
                ["definitionId"] = Shape.Int(rel["definitionId"]),
                ["stage"] = Shape.Str(rel["environmentName"]) ?? Shape.Str(rel["environmentDefinitionName"]),
                ["stageId"] = Shape.Int(rel["environmentId"]),
                ["stageDefinitionId"] = Shape.Int(rel["environmentDefinitionId"]),
            }
            : null;

    /// <summary>Results of all runs matching the filter.</summary>
    internal static async Task<ResultLoad> LoadResultsAsync(ToolInvocation t, RunFilter f, string? outcome, bool latest)
    {
        var runs = await LoadRunsAsync(t, f, 1000).ConfigureAwait(false);
        var ids = runs.Runs.Select(r => Shape.Int(r["id"])).OfType<int>().ToList();
        var load = await LoadResultsAsync(t, ids, outcome, latest, t.CancellationToken).ConfigureAwait(false);
        var notes = runs.Notes.Concat(load.Notes).ToList();
        if (runs.Truncated)
        {
            notes.Add("Mehr als 1000 Testläufe – nur die neuesten 1000 ausgewertet.");
        }

        return load with { Notes = notes, Truncated = load.Truncated || runs.Truncated };
    }

    internal static async Task<ResultLoad> LoadResultsAsync(ToolInvocation t, IReadOnlyList<int> runIds, string? outcome, bool latest, CancellationToken ct)
    {
        var all = new List<JsonObject>();
        var processed = 0;
        var truncated = false;
        var gate = new object();
        var outcomeQuery = outcome is { Length: > 0 } ? "&outcomes=" + Paging.Q(outcome) : "";
        await Expand.ForEachAsync(runIds, async (runId, token) =>
        {
            var res = await Paging.SkipAsync(t.Azdo,
                (skip, top) => $"{t.Azdo.ProjectUrl}/_apis/test/Runs/{runId}/results?detailsToInclude=WorkItems&$top={top}&$skip={skip}{outcomeQuery}&api-version={AzdoClient.ApiVersion}",
                200, MaxResultsLoaded, null, token).ConfigureAwait(false);
            var shaped = res.Items.Select(r => CompactResult(r, runId)).ToList();
            lock (gate)
            {
                processed += res.Processed;
                truncated |= res.Truncated;
                all.AddRange(shaped);
            }
        }, ct).ConfigureAwait(false);

        IEnumerable<JsonObject> ordered = all
            .OrderByDescending(r => Shape.Date(r["started"]) ?? DateTimeOffset.MinValue)
            .ThenByDescending(r => Shape.Long(r["id"]));
        if (latest)
        {
            ordered = LatestOnly(ordered);
        }

        var notes = new List<string>();
        if (all.Count >= MaxResultsLoaded)
        {
            truncated = true;
            notes.Add($"Mehr als {MaxResultsLoaded} Ergebnisse – Auswahl eingrenzen.");
        }

        return new(ordered.ToList(), processed, truncated, notes);
    }

    /// <summary>Key for "latest": test case (or automated test name) and configuration.</summary>
    internal static string LatestKey(JsonObject r) =>
        $"{(Shape.Int(r["testCase"]?["id"]) is { } tc and > 0 ? tc.ToString(System.Globalization.CultureInfo.InvariantCulture) : Shape.Str(r["automatedTestName"]) ?? Shape.Str(r["testCase"]?["title"]))}|{Shape.Int(r["configuration"]?["id"])}";

    /// <summary>Input must be ordered newest first; keeps the first result per key.</summary>
    internal static IEnumerable<JsonObject> LatestOnly(IEnumerable<JsonObject> newestFirst)
    {
        var seen = new HashSet<string>();
        foreach (var r in newestFirst)
        {
            if (seen.Add(LatestKey(r)))
            {
                yield return r;
            }
        }
    }

    internal static JsonObject CompactResult(JsonNode r, int runId) => new()
    {
        ["id"] = Shape.Long(r["id"]),
        ["runId"] = Shape.Int(r["testRun"]?["id"]) ?? runId,
        ["testCase"] = new JsonObject { ["id"] = Shape.Int(r["testCase"]?["id"]), ["title"] = Shape.Str(r["testCaseTitle"]) ?? Shape.Str(r["testCase"]?["name"]) },
        ["configuration"] = r["configuration"] is { } c ? new JsonObject { ["id"] = Shape.Int(c["id"]), ["name"] = Shape.Str(c["name"]) } : null,
        ["testPointId"] = Shape.Int(r["testPoint"]?["id"]),
        ["automatedTestName"] = Shape.Str(r["automatedTestName"]),
        ["outcome"] = Shape.Str(r["outcome"]),
        ["priority"] = Shape.Int(r["priority"]),
        ["owner"] = Shape.Person(r["owner"]),
        ["durationMs"] = r["durationInMs"] is { } d ? Math.Round(d.GetValue<double>()) : null,
        ["started"] = Shape.Iso(Shape.Date(r["startedDate"])),
        ["completed"] = Shape.Iso(Shape.Date(r["completedDate"])),
        ["failingSince"] = Shape.Iso(Shape.Date(r["failingSince"]?["date"])),
        ["comment"] = Shape.Shorten(Shape.Str(r["comment"]), ErrorMax),
        ["computerName"] = Shape.Str(r["computerName"]),
        ["build"] = r["build"] is { } b && Shape.Int(b["id"]) is { } bid and > 0 ? new JsonObject { ["id"] = bid, ["number"] = Shape.Str(b["name"]) } : null,
        ["release"] = Release(r["releaseReference"] ?? r["release"]),
        ["errorMessage"] = Shape.Shorten(Shape.Str(r["errorMessage"]), ErrorMax),
        ["bugs"] = Shape.Ints((r["associatedBugs"]?.AsArray() ?? []).Select(x => Shape.Int(x?["id"])).OfType<int>()),
        ["workItems"] = Shape.Ints((r["workItems"]?.AsArray() ?? []).Select(x => Shape.Int(x?["id"])).OfType<int>()),
    };
}
