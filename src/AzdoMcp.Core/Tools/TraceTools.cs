using System.Globalization;
using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

/// <summary>trace_links: follows links between work items, pull requests, commits, branches, builds and tests.</summary>
internal static class TraceTools
{
    public const int MaxNodes = 500;

    public static readonly string[] Kinds = ["children", "parent", "pullRequests", "commits", "branches", "builds", "testCases", "testResults", "bugs"];

    private static readonly string[] DefaultKinds = ["pullRequests", "commits", "branches", "builds", "testCases", "testResults", "bugs"];

    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("trace_links", ToolGroup.WorkItemsRead,
            "Verfolgt Verknüpfungen in beide Richtungen: von Work Items (IDs oder WIQL), einem Pull Request, einem Commit oder einem Testlauf zu "
            + string.Join(", ", Kinds) + ". Liefert Knoten, Kanten und eine Zusammenfassung je Start. refPattern (z. B. REQ-\\d+) verbindet zusätzlich Objekte, deren Titel, Commit-Nachricht, Branch- oder Testname denselben Treffer enthalten.",
            Schema.Create().IntArray("workItems", "Start: Work-Item-IDs").Str("wiql", "Start: WIQL-Abfrage (statt workItems)")
                .Int("pullRequest", "Start: ID eines Pull Requests").Str("commit", "Start: Commit-ID (mit repository)").Str("repository", "Repository des Commits")
                .Int("testRun", "Start: ID eines Testlaufs")
                .StrArray("include", "Zu verfolgende Arten (Standard: alle außer children und parent)")
                .Int("depth", "Tiefe 1 oder 2 (Standard 1)").Str("refPattern", "Regulärer Ausdruck für IDs im Text, z. B. REQ-\\d+")
                .Project().Build(),
            Trace);
    }

    private sealed class Graph(ToolInvocation t)
    {
        public Dictionary<string, JsonObject> Nodes { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HashSet<string>> Origins { get; } = new(StringComparer.Ordinal);

        public List<(string From, string To, string Kind, string Source)> Edges { get; } = [];

        public Dictionary<string, int> Foreign { get; } = new(StringComparer.Ordinal);

        public List<string> Starts { get; } = [];

        public HashSet<string> Notes { get; } = [];

        public bool Truncated { get; set; }

        public ToolInvocation T { get; } = t;

        /// <summary>Adds a node (if room) and an edge; returns the key when the node is new.</summary>
        public string? Link(string from, string kind, string id, string edgeKind, JsonObject? data = null)
        {
            var key = $"{kind}:{id}";
            var isNew = !Nodes.ContainsKey(key);
            if (isNew)
            {
                if (Nodes.Count >= MaxNodes)
                {
                    Truncated = true;
                    return null;
                }

                var node = data ?? new JsonObject();
                node["kind"] = kind;
                node["id"] = id;
                Nodes[key] = node;
                Origins[key] = [];
            }

            if (from != key && !Edges.Any(e => e.From == from && e.To == key && e.Kind == edgeKind))
            {
                Edges.Add((from, key, edgeKind, "link"));
            }

            if (Origins.TryGetValue(from, out var o))
            {
                Origins[key].UnionWith(o);
            }

            return isNew ? key : null;
        }

        public void CountForeign(string from)
        {
            foreach (var origin in Origins.TryGetValue(from, out var o) ? o : [])
            {
                Foreign[origin] = Foreign.GetValueOrDefault(origin) + 1;
            }
        }
    }

    private static async Task<string> Trace(ToolInvocation t)
    {
        var a = t.Args;
        var include = a.StringList("include") is { Count: > 0 } inc
            ? inc.Select(k => Kinds.FirstOrDefault(x => x.Equals(k, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolArgumentException($"Unbekannte Art „{k}“. Erlaubt: {string.Join(", ", Kinds)}.")).ToHashSet()
            : DefaultKinds.ToHashSet();
        var depth = a.IntOr("depth", 1, 1, 2);
        var refRx = Aggregate.Pattern(a.String("refPattern"), "refPattern");
        var g = new Graph(t);
        var starts = g.Starts;

        void Start(string kind, string id)
        {
            var key = $"{kind}:{id}";
            g.Nodes[key] = new JsonObject { ["kind"] = kind, ["id"] = id };
            g.Origins[key] = [key];
            starts.Add(key);
        }

        var startCount = new[] { a.Has("workItems") || a.Has("wiql"), a.Has("pullRequest"), a.Has("commit"), a.Has("testRun") }.Count(x => x);
        if (startCount != 1)
        {
            throw new ToolArgumentException("Genau einen Start angeben: workItems/wiql, pullRequest, commit oder testRun.");
        }

        if (a.String("wiql") is { Length: > 0 } wiql)
        {
            var res = await t.Azdo.PostJsonAsync($"{t.Azdo.ProjectUrl}/_apis/wit/wiql?$top=200&api-version={AzdoClient.ApiVersion}", new { query = wiql }, t.CancellationToken).ConfigureAwait(false);
            foreach (var id in (res?["workItems"]?.AsArray() ?? []).Select(w => Shape.Int(w?["id"])).OfType<int>().Take(200))
            {
                Start("workItem", Inv(id));
            }
        }

        foreach (var id in a.IntList("workItems"))
        {
            Start("workItem", Inv(id));
        }

        if (a.Int("pullRequest") is { } pr)
        {
            Start("pullRequest", Inv(pr));
        }

        if (a.String("commit") is { Length: > 0 } sha)
        {
            var repo = a.RequiredString("repository");
            Start("commit", $"{repo}/{sha}");
        }

        if (a.Int("testRun") is { } run)
        {
            Start("testRun", Inv(run));
        }

        // Breadth-first by level: load the level's nodes, then add their neighbours as the next level.
        var level = starts.ToList();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        for (var d = 0; d <= depth && level.Count > 0 && !g.Truncated; d++)
        {
            var expand = d < depth;
            var next = new List<string>();
            var pending = level.Where(loaded.Add).ToList();
            next.AddRange(await WorkItemsAsync(g, pending.Where(k => IsWorkItem(k)).ToList(), include, expand).ConfigureAwait(false));
            foreach (var key in pending.Where(k => k.StartsWith("pullRequest:", StringComparison.Ordinal)))
            {
                next.AddRange(await PullRequestAsync(g, key, include, expand).ConfigureAwait(false));
            }

            foreach (var key in pending.Where(k => k.StartsWith("commit:", StringComparison.Ordinal)))
            {
                next.AddRange(await CommitAsync(g, key, expand).ConfigureAwait(false));
            }

            foreach (var key in pending.Where(k => k.StartsWith("build:", StringComparison.Ordinal)))
            {
                await BuildAsync(g, key).ConfigureAwait(false);
            }

            foreach (var key in pending.Where(k => k.StartsWith("testRun:", StringComparison.Ordinal)))
            {
                next.AddRange(await TestRunAsync(g, key, include, expand).ConfigureAwait(false));
            }

            var testCases = pending.Where(k => k.StartsWith("testCase:", StringComparison.Ordinal)).ToList();
            if (include.Contains("testResults") && testCases.Count > 0)
            {
                await LatestResultsAsync(g, testCases).ConfigureAwait(false);
            }

            level = next.Distinct().ToList();
        }

        if (refRx is not null)
        {
            AddTextRefs(g, refRx);
        }

        return Output(Result(g, starts));
    }

    private static string Inv(int i) => i.ToString(CultureInfo.InvariantCulture);

    private static bool IsWorkItem(string key) =>
        key.StartsWith("workItem:", StringComparison.Ordinal) || key.StartsWith("testCase:", StringComparison.Ordinal) || key.StartsWith("bug:", StringComparison.Ordinal);

    private static string WiKind(string? type) => type switch
    {
        "Test Case" => "testCase",
        "Bug" => "bug",
        _ => "workItem",
    };

    /// <summary>Loads work item nodes (title, state, project check) and their links.</summary>
    private static async Task<List<string>> WorkItemsAsync(Graph g, List<string> keys, HashSet<string> include, bool expand)
    {
        var next = new List<string>();
        if (keys.Count == 0)
        {
            return next;
        }

        var t = g.T;
        if (!t.CanRead(AccessArea.WorkItems))
        {
            g.Notes.Add(ToolInvocation.LeftOut(AccessArea.WorkItems));
            return next;
        }

        var ids = keys.Select(k => int.Parse(k[(k.IndexOf(':', StringComparison.Ordinal) + 1)..], CultureInfo.InvariantCulture)).Distinct().ToList();
        var items = await FetchBatch(t, ids, null, expandRelations: true).ConfigureAwait(false);
        var byId = items.OfType<JsonObject>().ToDictionary(w => Shape.Int(w["id"]) ?? 0);
        var projectId = t.Target?.Project.AzdoId;
        foreach (var original in keys)
        {
            var key = original;
            var id = int.Parse(key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..], CultureInfo.InvariantCulture);
            if (!byId.TryGetValue(id, out var wi))
            {
                g.Nodes[key]["missing"] = true;
                continue;
            }

            var fields = wi["fields"];
            var project = Shape.Str(fields?["System.TeamProject"]);
            if (project is not null && !string.Equals(project, t.Azdo.ProjectName, StringComparison.OrdinalIgnoreCase))
            {
                // Linked item of another project: count only, show nothing of it.
                g.CountForeign(key);
                Remove(g, key);
                continue;
            }

            var node = g.Nodes[key];
            var type = Shape.Str(fields?["System.WorkItemType"]);
            node["type"] = type;
            node["title"] = Shape.Str(fields?["System.Title"]);
            node["state"] = Shape.Str(fields?["System.State"]);
            if (WiKind(type) != node["kind"]!.GetValue<string>())
            {
                // Re-key by the real type (a "workItem" start may be a Bug or Test Case).
                key = Rekey(g, key, WiKind(type), Inv(id));
            }

            if (!expand)
            {
                continue;
            }

            foreach (var rel in (wi["relations"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var relType = Shape.Str(rel["relationType"]) ?? "";
                var url = Shape.Str(rel["url"]) ?? "";
                var target = Shape.Int(rel["targetId"]);
                string? added = null;
                if (relType == "System.LinkTypes.Hierarchy-Forward" && target is { } c && include.Contains("children"))
                {
                    added = g.Link(key, "workItem", Inv(c), "child");
                }
                else if (relType == "System.LinkTypes.Hierarchy-Reverse" && target is { } p && include.Contains("parent"))
                {
                    added = g.Link(key, "workItem", Inv(p), "parent");
                }
                else if (relType == "Microsoft.VSTS.Common.TestedBy-Forward" && target is { } tc && include.Contains("testCases"))
                {
                    added = g.Link(key, "testCase", Inv(tc), "testedBy");
                }
                else if (relType == "Microsoft.VSTS.Common.TestedBy-Reverse" && target is { } req)
                {
                    added = g.Link(key, "workItem", Inv(req), "tests");
                }
                else if (relType == "System.LinkTypes.Related" && target is { } bug && include.Contains("bugs"))
                {
                    added = g.Link(key, "workItem", Inv(bug), "related");
                }
                else if (relType == "ArtifactLink")
                {
                    added = Artifact(g, key, url, include, projectId);
                }

                if (added is not null)
                {
                    next.Add(added);
                }
            }
        }

        return next;
    }

    /// <summary>vstfs:///Git/PullRequestId/{project}%2F{repo}%2F{id}, Git/Commit, Git/Ref, Build/Build.</summary>
    private static string? Artifact(Graph g, string from, string url, HashSet<string> include, string? projectId)
    {
        var u = Uri.UnescapeDataString(url);
        string[] Parts(string prefix) => u[prefix.Length..].Split('/');
        if (u.StartsWith("vstfs:///Git/PullRequestId/", StringComparison.OrdinalIgnoreCase) && include.Contains("pullRequests"))
        {
            var p = Parts("vstfs:///Git/PullRequestId/");
            if (p.Length < 3 || !SameProject(p[0], projectId))
            {
                g.CountForeign(from);
                return null;
            }

            return g.Link(from, "pullRequest", p[2], "pullRequest", new JsonObject { ["repositoryId"] = p[1] });
        }

        if (u.StartsWith("vstfs:///Git/Commit/", StringComparison.OrdinalIgnoreCase) && include.Contains("commits"))
        {
            var p = Parts("vstfs:///Git/Commit/");
            if (p.Length < 3 || !SameProject(p[0], projectId))
            {
                g.CountForeign(from);
                return null;
            }

            return g.Link(from, "commit", $"{p[1]}/{p[2]}", "commit");
        }

        if (u.StartsWith("vstfs:///Git/Ref/", StringComparison.OrdinalIgnoreCase) && include.Contains("branches"))
        {
            var p = Parts("vstfs:///Git/Ref/");
            if (p.Length < 3 || !SameProject(p[0], projectId))
            {
                g.CountForeign(from);
                return null;
            }

            var name = string.Join('/', p.Skip(2));
            name = name.StartsWith("GB", StringComparison.Ordinal) ? name[2..] : name;
            g.Link(from, "branch", $"{p[1]}/{name}", "branch", new JsonObject { ["name"] = name });
            return null;
        }

        if (u.StartsWith("vstfs:///Build/Build/", StringComparison.OrdinalIgnoreCase) && include.Contains("builds"))
        {
            return g.Link(from, "build", u["vstfs:///Build/Build/".Length..], "build");
        }

        return null;
    }

    private static bool SameProject(string id, string? projectId) =>
        string.IsNullOrEmpty(projectId) || string.Equals(id, projectId, StringComparison.OrdinalIgnoreCase);

    private static async Task<List<string>> PullRequestAsync(Graph g, string key, HashSet<string> include, bool expand)
    {
        var t = g.T;
        var next = new List<string>();
        if (!t.CanRead(AccessArea.Code))
        {
            g.Notes.Add(ToolInvocation.LeftOut(AccessArea.Code));
            return next;
        }

        var id = key["pullRequest:".Length..];
        JsonNode? raw;
        try
        {
            raw = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/pullrequests/{id}?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotFound or AzdoErrorKind.NotAvailable)
        {
            g.Nodes[key]["missing"] = true;
            return next;
        }

        var node = g.Nodes[key];
        node["title"] = Shape.Str(raw?["title"]);
        node["state"] = Shape.Str(raw?["status"]);
        node["repository"] = Shape.Str(raw?["repository"]?["name"]);
        node["sourceBranch"] = Shape.Branch(Shape.Str(raw?["sourceRefName"]));
        node["targetBranch"] = Shape.Branch(Shape.Str(raw?["targetRefName"]));
        node.Remove("repositoryId");
        var repoId = Shape.Str(raw?["repository"]?["id"]);
        if (!expand || repoId is null || !t.CanRead(AccessArea.WorkItems))
        {
            return next;
        }

        var wis = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/repositories/{repoId}/pullRequests/{id}/workitems?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        foreach (var w in (wis?["value"]?.AsArray() ?? []).Select(x => Shape.Int(x?["id"])).OfType<int>())
        {
            if (g.Link(key, "workItem", Inv(w), "workItem") is { } added)
            {
                next.Add(added);
            }
        }

        return next;
    }

    private static async Task<List<string>> CommitAsync(Graph g, string key, bool expand)
    {
        var t = g.T;
        var next = new List<string>();
        if (!t.CanRead(AccessArea.Code))
        {
            g.Notes.Add(ToolInvocation.LeftOut(AccessArea.Code));
            return next;
        }

        var parts = key["commit:".Length..].Split('/', 2);
        if (parts.Length != 2)
        {
            return next;
        }

        var res = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/repositories/{Uri.EscapeDataString(parts[0])}/commits?searchCriteria.ids={Uri.EscapeDataString(parts[1])}&searchCriteria.includeWorkItems=true&api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        var c = res?["value"]?[0];
        var node = g.Nodes[key];
        if (c is null)
        {
            node["missing"] = true;
            return next;
        }

        node["title"] = Shape.Shorten(Shape.Str(c["comment"]), 300);
        node["author"] = Shape.Str(c["author"]?["name"]);
        node["date"] = Shape.Iso(Shape.Date(c["author"]?["date"]));
        if (!expand || !t.CanRead(AccessArea.WorkItems))
        {
            return next;
        }

        foreach (var w in (c["workItems"]?.AsArray() ?? []).Select(x => Shape.Int(x?["id"])).OfType<int>())
        {
            if (g.Link(key, "workItem", Inv(w), "workItem") is { } added)
            {
                next.Add(added);
            }
        }

        return next;
    }

    private static async Task BuildAsync(Graph g, string key)
    {
        var t = g.T;
        if (!t.CanRead(AccessArea.Pipelines))
        {
            g.Notes.Add(ToolInvocation.LeftOut(AccessArea.Pipelines));
            return;
        }

        try
        {
            var b = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/build/builds/{key["build:".Length..]}?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
            var node = g.Nodes[key];
            node["title"] = Shape.Str(b?["buildNumber"]);
            node["state"] = Shape.Str(b?["result"]) ?? Shape.Str(b?["status"]);
            node["pipeline"] = Shape.Str(b?["definition"]?["name"]);
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotFound)
        {
            g.Nodes[key]["missing"] = true;
        }
    }

    private static async Task<List<string>> TestRunAsync(Graph g, string key, HashSet<string> include, bool expand)
    {
        var t = g.T;
        var next = new List<string>();
        if (!t.CanRead(AccessArea.Test))
        {
            g.Notes.Add(ToolInvocation.LeftOut(AccessArea.Test));
            return next;
        }

        var runId = int.Parse(key["testRun:".Length..], CultureInfo.InvariantCulture);
        var load = await TestTools.LoadResultsAsync(t, [runId], null, latest: true, t.CancellationToken).ConfigureAwait(false);
        g.Nodes[key]["title"] = $"Testlauf {runId}";
        g.Nodes[key]["state"] = $"{load.Results.Count(r => Shape.Str(r["outcome"]) == "Passed")}/{load.Results.Count} bestanden";
        if (!expand)
        {
            return next;
        }

        foreach (var r in load.Results)
        {
            var resKey = g.Link(key, "testResult", $"{runId}/{Shape.Long(r["id"])}", "result", new JsonObject
            {
                ["title"] = Shape.Str(r["testCase"]?["title"]) ?? Shape.Str(r["automatedTestName"]),
                ["state"] = Shape.Str(r["outcome"]),
                ["automatedTestName"] = Shape.Str(r["automatedTestName"]),
            });
            var from = resKey ?? $"testResult:{runId}/{Shape.Long(r["id"])}";
            if (!g.Nodes.ContainsKey(from))
            {
                continue;
            }

            if (Shape.Int(r["testCase"]?["id"]) is { } tc and > 0 && t.CanRead(AccessArea.WorkItems) && g.Link(from, "testCase", Inv(tc), "testCase") is { } tcKey)
            {
                next.Add(tcKey);
            }

            if (include.Contains("bugs"))
            {
                foreach (var bug in (r["bugs"] as JsonArray ?? []).Select(Shape.Int).OfType<int>())
                {
                    if (g.Link(from, "bug", Inv(bug), "bug") is { } bugKey)
                    {
                        next.Add(bugKey);
                    }
                }
            }
        }

        return next;
    }

    /// <summary>Latest result per test point of the test cases (test point query).</summary>
    private static async Task LatestResultsAsync(Graph g, List<string> testCaseKeys)
    {
        var t = g.T;
        if (!t.CanRead(AccessArea.Test))
        {
            g.Notes.Add(ToolInvocation.LeftOut(AccessArea.Test));
            return;
        }

        var ids = testCaseKeys.Where(g.Nodes.ContainsKey).Select(k => int.Parse(k["testCase:".Length..], CultureInfo.InvariantCulture)).ToList();
        foreach (var chunk in ids.Chunk(100))
        {
            JsonNode? res;
            try
            {
                res = await t.Azdo.PostJsonAsync($"{t.Azdo.ProjectUrl}/_apis/test/points?api-version={AzdoClient.ApiVersion}",
                    new JsonObject { ["pointsFilter"] = new JsonObject { ["testcaseIds"] = Shape.Ints(chunk) } }, t.CancellationToken).ConfigureAwait(false);
            }
            catch (AzdoApiException)
            {
                g.Notes.Add("Letztes Testergebnis nicht ermittelbar");
                return;
            }

            foreach (var p in (res?["points"]?.AsArray() ?? res?["value"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                var tc = Shape.Int(p["testCase"]?["id"]);
                var outcome = Shape.Str(p["outcome"]);
                if (tc is null || outcome is null or "Unspecified" or "None")
                {
                    continue;
                }

                var run = Shape.Int(p["lastTestRun"]?["id"]);
                var result = Shape.Int(p["lastResult"]?["id"]);
                g.Link($"testCase:{Inv(tc.Value)}", "testResult", $"{run}/{result}", "latestResult", new JsonObject
                {
                    ["title"] = Shape.Str(p["testCase"]?["name"]),
                    ["state"] = outcome,
                    ["configuration"] = Shape.Str(p["configuration"]?["name"]),
                });
            }
        }
    }

    private static string Rekey(Graph g, string oldKey, string kind, string id)
    {
        var key = $"{kind}:{id}";
        if (key == oldKey)
        {
            return key;
        }

        var node = g.Nodes[oldKey];
        node["kind"] = kind;
        g.Nodes.Remove(oldKey);
        g.Nodes[key] = node;
        g.Origins[key] = g.Origins[oldKey];
        g.Origins.Remove(oldKey);
        for (var i = 0; i < g.Edges.Count; i++)
        {
            var e = g.Edges[i];
            g.Edges[i] = (e.From == oldKey ? key : e.From, e.To == oldKey ? key : e.To, e.Kind, e.Source);
        }

        for (var i = 0; i < g.Starts.Count; i++)
        {
            if (g.Starts[i] == oldKey)
            {
                g.Starts[i] = key;
            }
        }

        foreach (var set in g.Origins.Values)
        {
            if (set.Remove(oldKey))
            {
                set.Add(key);
            }
        }

        foreach (var (k, v) in g.Foreign.ToList())
        {
            if (k == oldKey)
            {
                g.Foreign.Remove(k);
                g.Foreign[key] = v;
            }
        }

        return key;
    }

    private static void Remove(Graph g, string key)
    {
        g.Nodes.Remove(key);
        g.Edges.RemoveAll(e => e.From == key || e.To == key);
    }

    private static void AddTextRefs(Graph g, System.Text.RegularExpressions.Regex rx)
    {
        foreach (var (key, node) in g.Nodes.ToList())
        {
            var texts = new[] { Shape.Str(node["title"]), Shape.Str(node["automatedTestName"]), Shape.Str(node["sourceBranch"]), Shape.Str(node["name"]) };
            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var text in texts.OfType<string>())
            {
                try
                {
                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
                    {
                        matches.Add(m.Value.ToUpperInvariant());
                    }
                }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
                {
                    throw new ToolArgumentException("„refPattern“ braucht zu lange – Muster vereinfachen.");
                }
            }

            foreach (var m in matches)
            {
                var refKey = $"ref:{m}";
                if (!g.Nodes.ContainsKey(refKey))
                {
                    if (g.Nodes.Count >= MaxNodes)
                    {
                        g.Truncated = true;
                        return;
                    }

                    g.Nodes[refKey] = new JsonObject { ["kind"] = "ref", ["id"] = m };
                    g.Origins[refKey] = [];
                }

                g.Edges.Add((key, refKey, "mentions", "text"));
            }
        }
    }

    private static JsonObject Result(Graph g, List<string> starts)
    {
        var nodes = new JsonArray();
        foreach (var (key, node) in g.Nodes)
        {
            var o = (JsonObject)node.DeepClone();
            o["key"] = key;
            nodes.Add(o);
        }

        var edges = Shape.Array(g.Edges.Select(e => (JsonNode?)new JsonObject { ["from"] = e.From, ["to"] = e.To, ["kind"] = e.Kind, ["source"] = e.Source }));
        var summaries = new JsonArray();
        foreach (var start in starts.Distinct())
        {
            summaries.Add(new JsonObject { ["start"] = start, ["summary"] = Summary(g, start) });
        }

        var o2 = new JsonObject
        {
            ["count"] = nodes.Count,
            ["truncated"] = g.Truncated,
            ["summaries"] = summaries,
            ["nodes"] = nodes,
            ["edges"] = edges,
        };
        if (g.Notes.Count > 0)
        {
            o2["notes"] = Shape.Strings(g.Notes.Order(StringComparer.Ordinal));
        }

        return o2;
    }

    private static string Summary(Graph g, string start)
    {
        if (!g.Nodes.ContainsKey(start))
        {
            return "nicht gefunden oder nicht in diesem Projekt";
        }

        var reach = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            var k = queue.Dequeue();
            foreach (var e in g.Edges.Where(e => e.From == k && e.Source == "link"))
            {
                if (reach.Add(e.To))
                {
                    queue.Enqueue(e.To);
                }
            }
        }

        reach.Remove(start);
        List<JsonObject> Of(string kind) => reach.Where(k => k.StartsWith(kind + ":", StringComparison.Ordinal)).Select(k => g.Nodes[k]).ToList();
        var parts = new List<string>();
        var prs = Of("pullRequest");
        if (prs.Count > 0)
        {
            parts.Add($"{prs.Count} PRs ({prs.Count(p => Shape.Str(p["state"]) == "completed")} gemergt)");
        }

        if (Of("commit").Count is > 0 and var commits)
        {
            parts.Add($"{commits} Commits");
        }

        if (Of("branch").Count is > 0 and var branches)
        {
            parts.Add($"{branches} Branches");
        }

        if (Of("build").Count is > 0 and var builds)
        {
            parts.Add($"{builds} Builds");
        }

        var tests = Of("testCase");
        var results = Of("testResult");
        if (tests.Count > 0 || results.Count > 0)
        {
            var red = results.Count(r => AnalysisTools.IsFailed(Shape.Str(r["state"])));
            parts.Add($"{Math.Max(tests.Count, results.Count)} Tests ({red} rot)");
        }

        if (Of("bug").Count is > 0 and var bugs)
        {
            parts.Add($"{bugs} Bugs");
        }

        if (Of("workItem").Count is > 0 and var wis)
        {
            parts.Add($"{wis} Work Items");
        }

        if (g.Foreign.GetValueOrDefault(start) is > 0 and var foreign)
        {
            parts.Add(foreign == 1 ? "1 Verknüpfung in andere Projekte" : $"{foreign} Verknüpfungen in andere Projekte");
        }

        return parts.Count == 0 ? "keine Verknüpfungen" : string.Join(" · ", parts);
    }
}
