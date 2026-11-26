using System.Text.Json.Nodes;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

/// <summary>Layer 2: filter → groupBy → metrics over the same loaders the get_* tools use.</summary>
internal static class AnalysisTools
{
    private const int PrLimit = 2000;
    private const int PrExpandCap = 500;
    private const int ListMax = 50;

    public static readonly string[] PrGroups = ["none", "author", "reviewer", "repository", "targetBranch", "month", "week", "threadStatus", "file"];

    public static readonly string[] PrMetrics =
    [
        "count", "completed", "abandoned", "cycleTime", "iterations", "files", "comments", "commentsPerPr", "commentsPerFile",
        "threadStatus", "reviewersPerPr", "firstResponse", "approvalRate", "rejectionRate",
    ];

    private static readonly string[] PrDefaults = ["count", "completed", "abandoned", "cycleTime"];

    public static readonly string[] TestGroups = ["none", "build", "release", "stage", "run", "outcome", "testCase", "computerName", "day", "week"];

    public static readonly string[] TestMetrics = ["total", "passed", "failed", "notExecuted", "passRate", "duration", "failingSince", "newFailures"];

    private static readonly string[] TestDefaults = ["total", "passed", "failed", "notExecuted", "passRate"];

    public static IEnumerable<ToolDefinition> PullRequests()
    {
        yield return new("analyze_pull_requests", ToolGroup.Git,
            "Kennzahlen zu Pull Requests, gruppiert. groupBy: " + string.Join(", ", PrGroups) + ". metrics: " + string.Join(", ", PrMetrics)
            + ". Filter wie get_pull_requests; exclude: { titlePattern, authors } z. B. für Test-PRs und Bots. Personen werden über ihr Login gruppiert; Kommentare zählen nur Text-Kommentare.",
            GitTools.PrSchema()
                .Str("groupBy", "Gruppierung (Standard none)", false, PrGroups)
                .StrArray("metrics", "Kennzahlen (Standard: count, completed, abandoned, cycleTime)")
                .Obj("exclude", "{ \"titlePattern\": \"^\\\\[TEST\\\\]\", \"authors\": [\"Build Bot\"] }")
                .Project().Build(),
            AnalyzePullRequests);
    }

    public static IEnumerable<ToolDefinition> TestResults()
    {
        yield return new("analyze_test_results", ToolGroup.Test,
            "Kennzahlen zu Testergebnissen, gruppiert. Auswahl: runId, buildId, releaseId, planId, releaseDefinitionId, pipelineId, branch, tags, from/to (Standard letzte 30 Tage). latest (Standard testCase+configuration) zählt je Gruppe nur das neueste Ergebnis je Testfall und Konfiguration. groupBy: "
            + string.Join(", ", TestGroups) + ". metrics: " + string.Join(", ", TestMetrics) + ". failed = Failed + Aborted.",
            TestTools.RunSchema().Int("runId", "Nur dieser Testlauf")
                .Str("latest", "testCase+configuration (Standard) oder none", false, "testCase+configuration", "none")
                .Str("groupBy", "Gruppierung (Standard none)", false, TestGroups)
                .StrArray("metrics", "Kennzahlen (Standard: total, passed, failed, notExecuted, passRate)")
                .Project().Build(),
            AnalyzeTestResults);
    }

    // ---------------------------------------------------------------- pull requests

    private sealed record PrRow(string Key, string? Name, JsonObject Pr, JsonObject? Reviewer, List<JsonObject> Threads, int Files);

    private static async Task<string> AnalyzePullRequests(ToolInvocation t)
    {
        var groupBy = Aggregate.Choice(t.Args.String("groupBy"), PrGroups, "groupBy");
        var metrics = Aggregate.Metrics(t.Args.StringList("metrics"), PrMetrics, PrDefaults);
        var exclude = t.Args.Object("exclude");
        var titleRx = Aggregate.Pattern(Shape.Str(exclude?["titlePattern"]), "exclude.titlePattern");
        var excludedAuthors = (exclude?["authors"] as JsonArray ?? []).Select(Shape.Str).OfType<string>().ToList();

        var include = PrInclude.None;
        bool Needs(params string[] m) => m.Any(x => metrics.Contains(x));
        if (groupBy == "reviewer" || Needs("firstResponse", "approvalRate", "rejectionRate", "reviewersPerPr"))
        {
            include |= PrInclude.Reviewers;
        }

        if (groupBy is "threadStatus" or "file" || Needs("comments", "commentsPerPr", "commentsPerFile", "threadStatus"))
        {
            include |= PrInclude.Threads;
        }

        if (Needs("iterations"))
        {
            include |= PrInclude.Iterations;
        }

        if (groupBy == "file" || Needs("files", "commentsPerFile"))
        {
            include |= PrInclude.Files;
        }

        var load = await GitTools.LoadAsync(t, GitTools.Filter(t.Args), include, PrLimit, includeSystem: false, includeCap: PrExpandCap).ConfigureAwait(false);
        var excluded = 0;
        var prs = new List<JsonObject>();
        foreach (var pr in load.Items)
        {
            var author = pr["author"];
            if ((titleRx is not null && Aggregate.IsMatch(titleRx, Shape.Str(pr["title"]), "exclude.titlePattern"))
                || excludedAuthors.Any(a => string.Equals(a, Shape.Str(author?["login"]), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, Shape.Str(author?["name"]), StringComparison.OrdinalIgnoreCase)))
            {
                excluded++;
                continue;
            }

            prs.Add(pr);
        }

        var rows = prs.SelectMany(pr => PrRows(pr, groupBy)).ToList();
        var groups = rows.GroupBy(r => r.Key)
            .Select(g => (Key: g.Key, Rows: g.ToList()))
            .OrderByDescending(g => g.Rows.Select(r => Shape.Int(r.Pr["id"])).Distinct().Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (groupBy is "month" or "week")
        {
            groups = groups.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        }

        var output = new JsonArray();
        foreach (var (key, list) in groups.Take(Aggregate.MaxRows))
        {
            var row = new JsonObject { ["key"] = key };
            if (MostFrequent(list.Select(r => r.Name)) is { } name && name != key)
            {
                row["name"] = name;
            }

            AddPrMetrics(row, list, metrics, groupBy);
            output.Add(row);
        }

        var total = new JsonObject { ["key"] = "total" };
        AddPrMetrics(total, rows, metrics, groupBy);
        var notes = load.Notes.ToList();
        if (groups.Count > Aggregate.MaxRows)
        {
            notes.Add($"{groups.Count - Aggregate.MaxRows} weitere Gruppen nicht gezeigt");
        }

        return Output(Result(groupBy, metrics, output, total, load.Processed, load.Truncated, notes, excluded, prs.Count));
    }

    private static IEnumerable<PrRow> PrRows(JsonObject pr, string groupBy)
    {
        var threads = (pr["threads"] as JsonArray ?? []).OfType<JsonObject>().Where(th => Shape.Str(th["kind"]) == "text").ToList();
        var files = (pr["files"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var created = Shape.Date(pr["created"]);
        switch (groupBy)
        {
            case "reviewer":
                foreach (var r in (pr["reviewers"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var login = Shape.Str(r["reviewer"]?["login"]) ?? "?";
                    yield return new(login, Shape.Str(r["reviewer"]?["name"]), pr, r, threads, files.Count);
                }

                yield break;
            case "threadStatus":
                foreach (var th in threads)
                {
                    yield return new(Shape.Str(th["status"]) ?? "unknown", null, pr, null, [th], files.Count);
                }

                yield break;
            case "file":
                var paths = files.Select(f => Shape.Str(f["path"])).OfType<string>()
                    .Concat(threads.Select(th => Shape.Str(th["file"])).OfType<string>())
                    .Distinct(StringComparer.Ordinal);
                foreach (var path in paths)
                {
                    yield return new(path, null, pr, null, threads.Where(th => Shape.Str(th["file"]) == path).ToList(), 1);
                }

                yield break;
            default:
                var (key, name) = groupBy switch
                {
                    "author" => (Shape.Str(pr["author"]?["login"]) ?? "?", Shape.Str(pr["author"]?["name"])),
                    "repository" => (Shape.Str(pr["repository"]) ?? "?", null),
                    "targetBranch" => (Shape.Str(pr["targetBranch"]) ?? "?", null),
                    "month" => (created is { } m ? Aggregate.Month(m) : "?", null),
                    "week" => (created is { } w ? Aggregate.Week(w) : "?", null),
                    _ => ("all", (string?)null),
                };
                yield return new(key, name, pr, null, threads, files.Count);
                yield break;
        }
    }

    private static void AddPrMetrics(JsonObject row, List<PrRow> rows, IReadOnlyList<string> metrics, string groupBy)
    {
        var distinct = rows.GroupBy(r => Shape.Int(r.Pr["id"])).Select(g => g.First()).ToList();
        var byReviewer = groupBy == "reviewer";

        int Comments() => rows.Sum(r => r.Threads.Sum(th => (th["comments"] as JsonArray ?? [])
            .Count(c => !byReviewer || string.Equals(Shape.Str(c?["author"]?["login"]), Shape.Str(r.Reviewer?["reviewer"]?["login"]), StringComparison.OrdinalIgnoreCase))));

        int FileCount() => groupBy == "file" ? rows.Count : distinct.Sum(r => r.Files);

        IEnumerable<(int Max, int Min, bool Any)> Votes() => byReviewer
            ? rows.Select(r => Shape.Int(r.Reviewer?["vote"]) ?? 0).Select(v => (v, v, v != 0))
            : distinct.Select(r => (r.Pr["reviewers"] as JsonArray ?? []).Select(x => Shape.Int(x?["vote"]) ?? 0).ToList())
                .Select(v => (v.DefaultIfEmpty(0).Max(), v.DefaultIfEmpty(0).Min(), v.Any(x => x != 0)));

        foreach (var m in metrics)
        {
            switch (m)
            {
                case "count":
                    row["count"] = distinct.Count;
                    break;
                case "completed":
                    row["completed"] = distinct.Count(r => Shape.Str(r.Pr["status"]) == "completed");
                    break;
                case "abandoned":
                    row["abandoned"] = distinct.Count(r => Shape.Str(r.Pr["status"]) == "abandoned");
                    break;
                case "cycleTime":
                    row["cycleTimeHours"] = Aggregate.Stats(distinct.Select(r => r.Pr["cycleTimeHours"]).OfType<JsonValue>().Select(v => v.GetValue<double>()));
                    break;
                case "iterations":
                    row["iterationsAvg"] = Aggregate.Avg(distinct.Select(r => (double)(r.Pr["iterations"] as JsonArray ?? []).Count));
                    break;
                case "files":
                    row["filesAvg"] = Aggregate.Avg(distinct.Select(r => (double)r.Files));
                    break;
                case "comments":
                    row["comments"] = Comments();
                    break;
                case "commentsPerPr":
                    row["commentsPerPr"] = Aggregate.Ratio(Comments(), distinct.Count);
                    break;
                case "commentsPerFile":
                    row["commentsPerFile"] = Aggregate.Ratio(Comments(), FileCount());
                    break;
                case "threadStatus":
                    var statuses = new JsonObject();
                    foreach (var g in rows.SelectMany(r => r.Threads).DistinctBy(th => (Shape.Int(th["id"]), th.Parent))
                                 .GroupBy(th => Shape.Str(th["status"]) ?? "unknown").OrderByDescending(g => g.Count()))
                    {
                        statuses[g.Key] = g.Count();
                    }

                    row["threadStatus"] = statuses;
                    break;
                case "reviewersPerPr":
                    row["reviewersPerPr"] = Aggregate.Avg(distinct.Select(r => (double)(r.Pr["reviewers"] as JsonArray ?? []).Count));
                    break;
                case "firstResponse":
                    var hours = byReviewer
                        ? rows.Select(r => r.Reviewer?["firstResponseHours"]).OfType<JsonValue>().Select(v => v.GetValue<double>())
                        : distinct.Select(r => (r.Pr["reviewers"] as JsonArray ?? []).Select(x => x?["firstResponseHours"]).OfType<JsonValue>().Select(v => v.GetValue<double>()).DefaultIfEmpty(double.NaN).Min())
                            .Where(h => !double.IsNaN(h));
                    row["firstResponseHours"] = Aggregate.Stats(hours);
                    break;
                case "approvalRate":
                    var va = Votes().ToList();
                    row["approvalRate"] = Aggregate.Rate(va.Count(v => v.Any && v.Max >= 5), va.Count(v => v.Any));
                    break;
                case "rejectionRate":
                    var vr = Votes().ToList();
                    row["rejectionRate"] = Aggregate.Rate(vr.Count(v => v.Any && v.Min <= -5), vr.Count(v => v.Any));
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- test results

    private static async Task<string> AnalyzeTestResults(ToolInvocation t)
    {
        var groupBy = Aggregate.Choice(t.Args.String("groupBy"), TestGroups, "groupBy");
        var metrics = Aggregate.Metrics(t.Args.StringList("metrics"), TestMetrics, TestDefaults);
        var latest = t.Args.String("latest") is not "none";

        var load = t.Args.Int("runId") is { } runId
            ? await TestTools.LoadResultsAsync(t, [runId], null, latest: false, t.CancellationToken).ConfigureAwait(false)
            : await TestTools.LoadResultsAsync(t, TestTools.RunFilterFrom(t), null, latest: false).ConfigureAwait(false);

        var notes = load.Notes.ToList();
        if (groupBy == "stage")
        {
            await StageNamesAsync(t, load.Results, notes).ConfigureAwait(false);
        }

        // Results arrive newest first; "latest" applies inside each group.
        var groups = load.Results.Select(r => (Id: TestKey(r, groupBy), Result: r))
            .GroupBy(x => x.Id.Key, StringComparer.Ordinal)
            .Select(g => (Key: g.Key, Order: g.Select(x => x.Id.Order).Min(StringComparer.Ordinal) ?? "", Results: (latest ? TestTools.LatestOnly(g.Select(x => x.Result)) : g.Select(x => x.Result)).ToList()))
            .ToList();
        var timeOrdered = groupBy is "build" or "release" or "stage" or "run" or "day" or "week";
        groups = timeOrdered
            ? groups.OrderBy(g => g.Order).ThenBy(g => g.Key, StringComparer.Ordinal).ToList()
            : groups.OrderByDescending(g => g.Results.Count).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();

        var output = new JsonArray();
        Dictionary<string, string?>? previous = null;
        var history = new List<(string Key, Dictionary<string, string?> Outcomes)>();
        foreach (var (key, _, results) in groups)
        {
            var outcomes = results.GroupBy(TestTools.LatestKey).ToDictionary(g => g.Key, g => Shape.Str(g.First()["outcome"]));
            var row = new JsonObject { ["key"] = key };
            AddTestMetrics(row, results, metrics, timeOrdered ? previous : null, timeOrdered ? history : null);
            output.Add(row);
            history.Add((key, outcomes));
            previous = outcomes;
        }

        if (output.Count > Aggregate.MaxRows)
        {
            // Keep the newest rows for time-ordered groups.
            var keep = timeOrdered ? output.Skip(output.Count - Aggregate.MaxRows) : output.Take(Aggregate.MaxRows);
            output = Shape.Array(keep.Select(n => n?.DeepClone()));
        }

        var all = latest ? TestTools.LatestOnly(load.Results).ToList() : load.Results;
        var total = new JsonObject { ["key"] = "total" };
        AddTestMetrics(total, all, metrics.Where(m => m is not ("newFailures" or "failingSince")).ToList(), null, null);
        var result = Result(groupBy, metrics, output, total, load.Processed, load.Truncated, notes, 0, load.Results.Count);
        result["latest"] = latest ? "testCase+configuration" : "none";
        return Output(result);
    }

    private static (string Key, string Order) TestKey(JsonObject r, string groupBy)
    {
        var started = Shape.Date(r["started"]);
        return groupBy switch
        {
            "build" => (Shape.Str(r["build"]?["number"]) ?? "(kein Build)", (Shape.Int(r["build"]?["id"]) ?? 0).ToString("D10", System.Globalization.CultureInfo.InvariantCulture)),
            "release" => (Shape.Str(r["release"]?["name"]) ?? "(kein Release)", (Shape.Int(r["release"]?["id"]) ?? 0).ToString("D10", System.Globalization.CultureInfo.InvariantCulture)),
            "stage" => (Shape.Str(r["release"]?["stage"]) ?? (Shape.Int(r["release"]?["stageDefinitionId"]) is { } sd ? $"Stage {sd}" : "(keine Stage)"),
                (Shape.Int(r["release"]?["stageDefinitionId"]) ?? 0).ToString("D10", System.Globalization.CultureInfo.InvariantCulture)),
            "run" => ((Shape.Int(r["runId"]) ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), (Shape.Int(r["runId"]) ?? 0).ToString("D10", System.Globalization.CultureInfo.InvariantCulture)),
            "outcome" => (Shape.Str(r["outcome"]) ?? "Unknown", ""),
            "testCase" => (Shape.Str(r["testCase"]?["title"]) ?? Shape.Str(r["automatedTestName"]) ?? "?", ""),
            "computerName" => (Shape.Str(r["computerName"]) is { Length: > 0 } c ? c : "(unbekannt)", ""),
            "day" => (started is { } d ? Aggregate.Day(d) : "?", started is { } d2 ? Aggregate.Day(d2) : ""),
            "week" => (started is { } w ? Aggregate.Week(w) : "?", started is { } w2 ? Aggregate.Week(w2) : ""),
            _ => ("all", ""),
        };
    }

    /// <summary>Results only carry the stage ID; the release knows its stage names.</summary>
    private static async Task StageNamesAsync(ToolInvocation t, List<JsonObject> results, List<string> notes)
    {
        var releases = results.Where(r => Shape.Str(r["release"]?["stage"]) is null)
            .Select(r => Shape.Int(r["release"]?["id"])).OfType<int>().Distinct().ToList();
        if (releases.Count == 0)
        {
            return;
        }

        if (!t.CanRead(Connections.AccessArea.Pipelines))
        {
            notes.Add("Stage-Namen: Pipelines Lesen fehlt – Stages nur mit ID");
            return;
        }

        var names = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        await Expand.ForEachAsync(releases.Take(50), async (id, ct) =>
        {
            try
            {
                var rel = await t.Azdo.GetAsync($"{PipelineTools.ReleaseBase(t.Azdo.Connection)}/{Uri.EscapeDataString(t.Azdo.ProjectName!)}/_apis/release/releases/{id}?api-version={Azdo.AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
                foreach (var e in rel?["environments"]?.AsArray() ?? [])
                {
                    if (Shape.Int(e?["id"]) is { } envId && Shape.Str(e?["name"]) is { } name)
                    {
                        names[envId] = name;
                    }
                }
            }
            catch (Azdo.AzdoApiException)
            {
                // Release deleted or not reachable: keep the ID.
            }
        }, t.CancellationToken).ConfigureAwait(false);
        if (releases.Count > 50)
        {
            notes.Add($"Stage-Namen für {releases.Count - 50} Releases nicht geladen");
        }

        foreach (var r in results)
        {
            if (r["release"] is JsonObject rel && Shape.Str(rel["stage"]) is null && Shape.Int(rel["stageId"]) is { } sid && names.TryGetValue(sid, out var n))
            {
                rel["stage"] = n;
            }
        }
    }

    internal static bool IsFailed(string? outcome) => outcome is "Failed" or "Aborted";

    private static void AddTestMetrics(JsonObject row, List<JsonObject> results, IReadOnlyList<string> metrics,
        Dictionary<string, string?>? previous, List<(string Key, Dictionary<string, string?> Outcomes)>? history)
    {
        var passed = results.Count(r => Shape.Str(r["outcome"]) == "Passed");
        var failed = results.Count(r => IsFailed(Shape.Str(r["outcome"])));
        foreach (var m in metrics)
        {
            switch (m)
            {
                case "total":
                    row["total"] = results.Count;
                    break;
                case "passed":
                    row["passed"] = passed;
                    break;
                case "failed":
                    row["failed"] = failed;
                    break;
                case "notExecuted":
                    row["notExecuted"] = results.Count(r => Shape.Str(r["outcome"]) == "NotExecuted");
                    break;
                case "passRate":
                    row["passRate"] = Aggregate.Rate(passed, passed + failed);
                    break;
                case "duration":
                    row["durationMsAvg"] = Aggregate.Avg(results.Select(r => r["durationMs"]).OfType<JsonValue>().Select(v => v.GetValue<double>()));
                    break;
                case "newFailures" when previous is not null:
                    row["newFailures"] = Shape.Array(results
                        .Where(r => IsFailed(Shape.Str(r["outcome"])) && previous.TryGetValue(TestTools.LatestKey(r), out var before) && before == "Passed")
                        .Take(ListMax).Select(Test));
                    break;
                case "newFailures":
                    row["newFailures"] = new JsonArray();
                    break;
                case "failingSince":
                    row["failingSince"] = Shape.Array(results.Where(r => IsFailed(Shape.Str(r["outcome"]))).Take(ListMax).Select(r =>
                    {
                        var o = Test(r)!.AsObject();
                        o["since"] = Shape.Str(r["failingSince"]) ?? Streak(TestTools.LatestKey(r), history);
                        return (JsonNode?)o;
                    }));
                    break;
            }
        }
    }

    /// <summary>First group of the current failure streak, from the groups before this one.</summary>
    private static string? Streak(string key, List<(string Key, Dictionary<string, string?> Outcomes)>? history)
    {
        if (history is null)
        {
            return null;
        }

        string? since = null;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (!history[i].Outcomes.TryGetValue(key, out var o) || !IsFailed(o))
            {
                break;
            }

            since = history[i].Key;
        }

        return since ?? "(diese Gruppe)";
    }

    private static JsonNode? Test(JsonObject r) => new JsonObject
    {
        ["testCase"] = r["testCase"]?.DeepClone(),
        ["automatedTestName"] = Shape.Str(r["automatedTestName"]),
        ["configuration"] = Shape.Str(r["configuration"]?["name"]),
    };

    // ---------------------------------------------------------------- shared

    private static string? MostFrequent(IEnumerable<string?> names) =>
        names.OfType<string>().GroupBy(n => n).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();

    private static JsonObject Result(string groupBy, IReadOnlyList<string> metrics, JsonArray rows, JsonObject total, int processed, bool truncated, IReadOnlyCollection<string> notes, int excluded, int counted)
    {
        var o = new JsonObject
        {
            ["groupBy"] = groupBy,
            ["metrics"] = Shape.Strings(metrics),
            ["counted"] = counted,
            ["excluded"] = excluded,
            ["processed"] = processed,
            ["truncated"] = truncated,
            ["rows"] = rows,
            ["total"] = total,
        };
        if (notes.Count > 0)
        {
            o["notes"] = Shape.Strings(notes);
        }

        return o;
    }
}
