using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

[Flags]
internal enum PrInclude
{
    None = 0,
    Reviewers = 1,
    Threads = 2,
    Iterations = 4,
    Files = 8,
    WorkItems = 16,
    All = Reviewers | Threads | Iterations | Files | WorkItems,
}

internal sealed record PrFilter(string Status, string? Repository, string? TargetBranch, string? Author, string? Reviewer, DateTimeOffset? From, DateTimeOffset? To);

internal sealed record PrLoad(List<JsonObject> Items, int Processed, bool Truncated, List<string> Notes);

internal static class GitTools
{
    public const int IncludeCap = 200;
    private const int CommentMax = 4000;

    /// <summary>PRs created this long before "from" are not considered for close-date periods.</summary>
    private static readonly TimeSpan CloseLookback = TimeSpan.FromDays(180);

    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("get_repositories", ToolGroup.Git, "Listet die Git-Repositories des Projekts.",
            Schema.Create().Project().Build(), GetRepositories);

        yield return new("get_commits", ToolGroup.Git, "Listet Commits eines Repositories (optional Branch, Autor, Zeitraum).",
            Schema.Create().Str("repository", "Name oder ID des Repositories", required: true).Str("branch", "Branch, z. B. main")
                .Str("author", "Autor (Name oder E-Mail)").Str("fromDate", "Ab Datum (ISO 8601)").Str("toDate", "Bis Datum (ISO 8601)")
                .Int("top", "Maximale Anzahl (Standard 50)").Project().Build(),
            GetCommits);

        yield return new("get_branches", ToolGroup.Git,
            "Branches eines Repositories mit letztem Commit, ahead/behind gegenüber dem Default-Branch und aktivem PR. Filter: Präfix, staleDays (seit N Tagen kein Commit), withoutPullRequest.",
            Schema.Create().Str("repository", "Name oder ID des Repositories", required: true).Str("prefix", "Nur Branches, die so beginnen, z. B. feature/")
                .Int("staleDays", "Nur Branches ohne Commit seit so vielen Tagen").Bool("withoutPullRequest", "Nur Branches ohne aktiven Pull Request")
                .Project().Build(),
            GetBranches);

        yield return new("get_pull_requests", ToolGroup.Git,
            "Pull Requests als Daten für Auswertungen. Filter: status, repository, targetBranch, author, reviewer, from/to (Schließdatum bei completed/abandoned, sonst Erstelldatum). include: reviewers (Stimme, votedAt, firstResponseHours), threads (Kommentare mit Datei/Zeile), iterations, files, workItems.",
            PrSchema().Int("top", "Maximale Anzahl (Standard 100, höchstens 1000)").Project().Build(),
            GetPullRequests);

        yield return new("get_pull_request", ToolGroup.Git,
            "Ein Pull Request mit Beschreibung, Reviewern und Stimmen, allen Kommentar-Threads, Pushes, geänderten Dateien und verknüpften Work Items.",
            Schema.Create().Int("id", "ID des Pull Requests", required: true).Bool("includeSystem", "Auch Systemeinträge (Stimmen, Pushes, Richtlinien) listen")
                .Project().Build(),
            GetPullRequest);
    }

    internal static Schema PrSchema() =>
        Schema.Create()
            .Str("status", "active, completed, abandoned oder all (Standard all)", false, "active", "completed", "abandoned", "all")
            .Str("repository", "Name oder ID des Repositories (Standard: alle)")
            .Str("targetBranch", "Ziel-Branch, z. B. main")
            .Str("author", "Ersteller (Name, Login oder ID)")
            .Str("reviewer", "Reviewer (Name, Login oder ID)")
            .Str("from", "Ab Datum (ISO 8601)")
            .Str("to", "Bis Datum (ISO 8601)")
            .StrArray("include", "reviewers, threads, iterations, files, workItems")
            .Bool("includeSystem", "Systemeinträge in threads mit listen");

    internal static PrFilter Filter(ToolArgs a) => new(
        a.String("status") is { Length: > 0 } s ? s : "all",
        a.String("repository") is { Length: > 0 } r ? r : null,
        a.String("targetBranch") is { Length: > 0 } b ? b : null,
        a.String("author") is { Length: > 0 } au ? au : null,
        a.String("reviewer") is { Length: > 0 } rv ? rv : null,
        Shape.DateArg(a, "from"),
        Shape.DateArg(a, "to"));

    internal static PrInclude ParseInclude(IEnumerable<string> names)
    {
        var r = PrInclude.None;
        foreach (var n in names)
        {
            r |= n.ToLowerInvariant() switch
            {
                "reviewers" => PrInclude.Reviewers,
                "threads" => PrInclude.Threads,
                "iterations" => PrInclude.Iterations,
                "files" => PrInclude.Files,
                "workitems" => PrInclude.WorkItems,
                "all" => PrInclude.All,
                _ => throw new ToolArgumentException($"Unbekannter Wert „{n}“ für include. Erlaubt: reviewers, threads, iterations, files, workItems."),
            };
        }

        return r;
    }

    private static async Task<string> GetRepositories(ToolInvocation t)
    {
        var res = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/repositories?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        var list = new JsonArray();
        foreach (var r in res?["value"]?.AsArray() ?? [])
        {
            list.Add(new JsonObject
            {
                ["id"] = r?["id"]?.DeepClone(),
                ["name"] = r?["name"]?.DeepClone(),
                ["defaultBranch"] = Shape.Branch(Shape.Str(r?["defaultBranch"])),
                ["webUrl"] = r?["webUrl"]?.DeepClone(),
                ["size"] = r?["size"]?.DeepClone(),
            });
        }

        return Output(new JsonObject { ["count"] = list.Count, ["repositories"] = list });
    }

    private static async Task<string> GetCommits(ToolInvocation t)
    {
        var repo = Uri.EscapeDataString(t.Args.RequiredString("repository"));
        var q = new List<string> { $"searchCriteria.$top={t.Args.IntOr("top", 50, 1, 1000)}" };
        if (t.Args.String("branch") is { Length: > 0 } b)
        {
            q.Add("searchCriteria.itemVersion.version=" + Uri.EscapeDataString(b.Replace("refs/heads/", "")));
        }

        if (t.Args.String("author") is { Length: > 0 } a)
        {
            q.Add("searchCriteria.author=" + Uri.EscapeDataString(a));
        }

        if (t.Args.String("fromDate") is { Length: > 0 } f)
        {
            q.Add("searchCriteria.fromDate=" + Uri.EscapeDataString(f));
        }

        if (t.Args.String("toDate") is { Length: > 0 } to)
        {
            q.Add("searchCriteria.toDate=" + Uri.EscapeDataString(to));
        }

        var res = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/repositories/{repo}/commits?{string.Join('&', q)}&api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        var list = new JsonArray();
        foreach (var c in res?["value"]?.AsArray() ?? [])
        {
            list.Add(new JsonObject
            {
                ["commitId"] = c?["commitId"]?.DeepClone(),
                ["author"] = c?["author"]?["name"]?.DeepClone(),
                ["date"] = c?["author"]?["date"]?.DeepClone(),
                ["comment"] = c?["comment"]?.DeepClone(),
                ["url"] = c?["remoteUrl"]?.DeepClone(),
            });
        }

        return Output(new JsonObject { ["count"] = list.Count, ["commits"] = list });
    }

    private static async Task<string> GetBranches(ToolInvocation t)
    {
        var repo = Uri.EscapeDataString(t.Args.RequiredString("repository"));
        var prefix = t.Args.String("prefix") is { Length: > 0 } p ? Shape.Branch(p)! : null;
        var staleDays = t.Args.Int("staleDays");
        var withoutPr = t.Args.Bool("withoutPullRequest");
        var now = t.Time.GetUtcNow();

        var stats = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/repositories/{repo}/stats/branches?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        var active = await Paging.SkipAsync(t.Azdo,
            (skip, top) => $"{t.Azdo.ProjectUrl}/_apis/git/repositories/{repo}/pullrequests?searchCriteria.status=active&$top={top}&$skip={skip}&api-version={AzdoClient.ApiVersion}",
            100, 1000, null, t.CancellationToken).ConfigureAwait(false);
        var prBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pr in active.Items)
        {
            if (Shape.Branch(Shape.Str(pr["sourceRefName"])) is { } src && Shape.Int(pr["pullRequestId"]) is { } id)
            {
                prBySource.TryAdd(src, id);
            }
        }

        var list = new JsonArray();
        var processed = 0;
        foreach (var b in stats?["value"]?.AsArray() ?? [])
        {
            processed++;
            var name = Shape.Branch(Shape.Str(b?["name"])) ?? "";
            if (prefix is not null && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var commit = b?["commit"];
            var date = Shape.Date(commit?["committer"]?["date"]) ?? Shape.Date(commit?["author"]?["date"]);
            if (staleDays is { } days && date is { } d && (now - d).TotalDays < days)
            {
                continue;
            }

            int? prId = prBySource.TryGetValue(name, out var found) ? found : null;
            if (withoutPr && prId is not null)
            {
                continue;
            }

            list.Add(new JsonObject
            {
                ["name"] = name,
                ["isDefault"] = b?["isBaseVersion"]?.DeepClone() ?? false,
                ["ahead"] = Shape.Int(b?["aheadCount"]),
                ["behind"] = Shape.Int(b?["behindCount"]),
                ["lastCommit"] = new JsonObject
                {
                    ["id"] = Shape.Str(commit?["commitId"]),
                    ["date"] = Shape.Iso(date),
                    ["author"] = Shape.Str(commit?["author"]?["name"]),
                },
                ["daysSinceCommit"] = date is { } dd ? (int)(now - dd).TotalDays : null,
                ["activePullRequest"] = prId,
            });
        }

        return Output(Shape.Envelope("branches", list, processed, false));
    }

    private static async Task<string> GetPullRequests(ToolInvocation t)
    {
        var include = ParseInclude(t.Args.StringList("include"));
        var load = await LoadAsync(t, Filter(t.Args), include, t.Args.IntOr("top", 100, 1, 1000), t.Args.Bool("includeSystem")).ConfigureAwait(false);
        return Output(Shape.Envelope("pullRequests", Shape.Array(load.Items), load.Processed, load.Truncated, load.Notes));
    }

    private static async Task<string> GetPullRequest(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        JsonNode? raw;
        try
        {
            raw = await t.Azdo.GetAsync($"{t.Azdo.ProjectUrl}/_apis/git/pullrequests/{id}?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotFound or AzdoErrorKind.NotAvailable)
        {
            throw new ToolArgumentException($"Pull Request {id} gibt es in {t.ProjectAlias} nicht.");
        }

        var projectName = Shape.Str(raw?["repository"]?["project"]?["name"]);
        if (raw is null || (projectName is not null && !string.Equals(projectName, t.Azdo.ProjectName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ToolArgumentException($"Pull Request {id} gibt es in {t.ProjectAlias} nicht.");
        }

        var notes = new List<string>();
        var pr = Compact(t, raw);
        pr["description"] = Shape.Shorten(Shape.Str(raw["description"]), CommentMax);
        await ExpandAsync(t, raw, pr, PrInclude.All, t.Args.Bool("includeSystem"), notes, t.CancellationToken).ConfigureAwait(false);
        if (notes.Count > 0)
        {
            pr["notes"] = Shape.Strings(notes);
        }

        return Output(pr);
    }

    /// <summary>Loads pull requests with filters, paging and optional details. Shared with analyze_pull_requests.</summary>
    internal static async Task<PrLoad> LoadAsync(ToolInvocation t, PrFilter f, PrInclude include, int top, bool includeSystem, int includeCap = IncludeCap)
    {
        var notes = new List<string>();
        var basePath = f.Repository is { } repo
            ? $"{t.Azdo.ProjectUrl}/_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests"
            : $"{t.Azdo.ProjectUrl}/_apis/git/pullrequests";
        var q = new List<string> { "searchCriteria.status=" + Paging.Q(f.Status) };
        if (f.TargetBranch is { } tb)
        {
            q.Add("searchCriteria.targetRefName=" + Paging.Q("refs/heads/" + Shape.Branch(tb)));
        }

        if (f.Author is { } au && Guid.TryParse(au, out _))
        {
            q.Add("searchCriteria.creatorId=" + au);
        }

        if (f.Reviewer is { } rv && Guid.TryParse(rv, out _))
        {
            q.Add("searchCriteria.reviewerId=" + rv);
        }

        var query = string.Join('&', q);
        var collected = await Paging.SkipAsync(t.Azdo,
            (skip, pageTop) => $"{basePath}?{query}&$top={pageTop}&$skip={skip}&api-version={AzdoClient.ApiVersion}",
            100, top, raw => Decide(raw, f), t.CancellationToken).ConfigureAwait(false);

        var items = collected.Items.Select(raw => (Raw: raw, Pr: Compact(t, raw))).ToList();
        if (include != PrInclude.None)
        {
            var expand = items.Take(includeCap).ToList();
            await Expand.ForEachAsync(expand, (x, ct) => ExpandAsync(t, x.Raw, x.Pr, include, includeSystem, notes, ct), t.CancellationToken).ConfigureAwait(false);
            if (items.Count > includeCap)
            {
                notes.Add($"Details für {items.Count - includeCap} PRs nicht geladen");
            }
        }

        if (include.HasFlag(PrInclude.WorkItems) && !t.CanRead(AccessArea.WorkItems))
        {
            notes.Add(ToolInvocation.LeftOut(AccessArea.WorkItems));
        }

        return new(items.Select(x => x.Pr).ToList(), collected.Processed, collected.Truncated, notes.Distinct().ToList());
    }

    private static Walk Decide(JsonNode raw, PrFilter f)
    {
        var created = Shape.Date(raw["creationDate"]);
        var closed = Shape.Date(raw["closedDate"]);
        var status = Shape.Str(raw["status"]);
        var usesClose = status is "completed" or "abandoned";
        var date = usesClose ? closed ?? created : created;

        // The list is ordered by creation, newest first.
        if (f.From is { } from && created is { } c && c < (usesClose || f.Status != "active" ? from - CloseLookback : from))
        {
            return Walk.Stop;
        }

        if (f.From is { } fr && date is { } d1 && d1 < fr)
        {
            return Walk.Skip;
        }

        if (f.To is { } to && date is { } d2 && d2 > to)
        {
            return Walk.Skip;
        }

        if (f.Author is { } author && !Guid.TryParse(author, out _) && !Shape.Matches(raw["createdBy"], author))
        {
            return Walk.Skip;
        }

        if (f.Reviewer is { } reviewer && !Guid.TryParse(reviewer, out _)
            && !(raw["reviewers"]?.AsArray() ?? []).Any(r => Shape.Matches(r, reviewer)))
        {
            return Walk.Skip;
        }

        return Walk.Keep;
    }

    internal static JsonObject Compact(ToolInvocation t, JsonNode raw)
    {
        var id = Shape.Int(raw["pullRequestId"]);
        var repoName = Shape.Str(raw["repository"]?["name"]);
        var created = Shape.Date(raw["creationDate"]);
        var closed = Shape.Date(raw["closedDate"]);
        var status = Shape.Str(raw["status"]);
        return new JsonObject
        {
            ["id"] = id,
            ["repository"] = repoName,
            ["title"] = Shape.Str(raw["title"]),
            ["author"] = Shape.Person(raw["createdBy"]),
            ["status"] = status,
            ["isDraft"] = raw["isDraft"]?.DeepClone() ?? false,
            ["sourceBranch"] = Shape.Branch(Shape.Str(raw["sourceRefName"])),
            ["targetBranch"] = Shape.Branch(Shape.Str(raw["targetRefName"])),
            ["created"] = Shape.Iso(created),
            ["closed"] = Shape.Iso(closed),
            ["mergeStatus"] = Shape.Str(raw["mergeStatus"]),
            ["mergeStrategy"] = Shape.Str(raw["completionOptions"]?["mergeStrategy"]),
            ["labels"] = Shape.Strings((raw["labels"]?.AsArray() ?? []).Select(l => Shape.Str(l?["name"])).OfType<string>()),
            ["cycleTimeHours"] = status == "completed" && created is { } c && closed is { } d ? Shape.Hours(c, d) : null,
            ["webUrl"] = repoName is null || id is null ? null : $"{t.Azdo.ProjectUrl}/_git/{Uri.EscapeDataString(repoName)}/pullrequest/{id}",
        };
    }

    private static async Task ExpandAsync(ToolInvocation t, JsonNode raw, JsonObject pr, PrInclude include, bool includeSystem, List<string> notes, CancellationToken ct)
    {
        var repoId = Shape.Str(raw["repository"]?["id"]);
        var id = Shape.Int(raw["pullRequestId"]);
        if (repoId is null || id is null)
        {
            return;
        }

        var prBase = $"{t.Azdo.ProjectUrl}/_apis/git/repositories/{repoId}/pullRequests/{id}";
        JsonArray threads = [];
        if (include.HasFlag(PrInclude.Threads) || include.HasFlag(PrInclude.Reviewers))
        {
            var res = await t.Azdo.GetAsync($"{prBase}/threads?api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
            threads = res?["value"]?.AsArray() ?? [];
        }

        if (include.HasFlag(PrInclude.Reviewers))
        {
            pr["reviewers"] = Reviewers(raw, threads);
        }

        if (include.HasFlag(PrInclude.Threads))
        {
            var (listed, system) = Threads(threads, includeSystem);
            pr["threads"] = listed;
            pr["systemThreads"] = system;
        }

        if (include.HasFlag(PrInclude.Iterations) || include.HasFlag(PrInclude.Files))
        {
            var res = await t.Azdo.GetAsync($"{prBase}/iterations?api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
            var its = res?["value"]?.AsArray() ?? [];
            if (include.HasFlag(PrInclude.Iterations))
            {
                pr["iterations"] = Shape.Array(its.Select(i => (JsonNode?)new JsonObject
                {
                    ["number"] = Shape.Int(i?["id"]),
                    ["author"] = Shape.Person(i?["author"]),
                    ["date"] = Shape.Iso(Shape.Date(i?["createdDate"])),
                }));
            }

            if (include.HasFlag(PrInclude.Files) && its.Select(i => Shape.Int(i?["id"])).Max() is { } last)
            {
                var ch = await t.Azdo.GetAsync($"{prBase}/iterations/{last}/changes?$compareTo=0&$top=2000&api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
                var files = (ch?["changeEntries"]?.AsArray() ?? [])
                    .Where(e => e?["item"]?["isFolder"]?.GetValue<bool>() != true && Shape.Str(e?["item"]?["path"]) is not null)
                    .GroupBy(e => Shape.Str(e!["item"]!["path"])!)
                    .Select(g => (JsonNode?)new JsonObject { ["path"] = g.Key, ["changeType"] = Shape.Str(g.First()!["changeType"]) });
                pr["files"] = Shape.Array(files);
            }
        }

        if (include.HasFlag(PrInclude.WorkItems) && t.CanRead(AccessArea.WorkItems))
        {
            var res = await t.Azdo.GetAsync($"{prBase}/workitems?api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
            pr["workItems"] = Shape.Ints((res?["value"]?.AsArray() ?? []).Select(w => Shape.Int(w?["id"])).OfType<int>());
        }
    }

    internal static bool IsSystemThread(JsonNode? thread)
    {
        if (thread?["properties"]?["CodeReviewThreadType"] is not null)
        {
            return true;
        }

        var comments = thread?["comments"]?.AsArray() ?? [];
        return comments.Count > 0 && comments.All(c => Shape.Str(c?["commentType"]) == "system");
    }

    private static (JsonArray Listed, int System) Threads(JsonArray threads, bool includeSystem)
    {
        var listed = new JsonArray();
        var system = 0;
        foreach (var th in threads)
        {
            if (th?["isDeleted"]?.GetValue<bool>() == true)
            {
                continue;
            }

            var isSystem = IsSystemThread(th);
            if (isSystem)
            {
                system++;
                if (!includeSystem)
                {
                    continue;
                }
            }

            var ctx = th?["threadContext"];
            var start = ctx?["rightFileStart"] ?? ctx?["leftFileStart"];
            var end = ctx?["rightFileEnd"] ?? ctx?["leftFileEnd"];
            listed.Add(new JsonObject
            {
                ["id"] = Shape.Int(th?["id"]),
                ["status"] = Shape.Str(th?["status"]) ?? "unknown",
                ["kind"] = isSystem ? "system" : "text",
                ["file"] = Shape.Str(ctx?["filePath"]),
                ["lines"] = start is null ? null : new JsonObject { ["from"] = Shape.Int(start["line"]), ["to"] = Shape.Int(end?["line"]) },
                ["comments"] = Shape.Array((th?["comments"]?.AsArray() ?? [])
                    .Where(c => c?["isDeleted"]?.GetValue<bool>() != true)
                    .Select(c => (JsonNode?)new JsonObject
                    {
                        ["id"] = Shape.Int(c?["id"]),
                        ["parentId"] = Shape.Int(c?["parentCommentId"]) is > 0 and var pid ? pid : null,
                        ["author"] = Shape.Person(c?["author"]),
                        ["published"] = Shape.Iso(Shape.Date(c?["publishedDate"])),
                        ["text"] = Shape.Shorten(Shape.Str(c?["content"]), CommentMax),
                    })),
            });
        }

        return (listed, system);
    }

    private static JsonArray Reviewers(JsonNode raw, JsonArray threads)
    {
        var created = Shape.Date(raw["creationDate"]);
        var list = new JsonArray();
        foreach (var r in raw["reviewers"]?.AsArray() ?? [])
        {
            var rid = Shape.Str(r?["id"]);
            var vote = Shape.Int(r?["vote"]) ?? 0;
            var votedAt = VotedAt(threads, rid);
            // Same set as the listed threads: no deleted threads or comments.
            var firstComment = threads
                .Where(th => !IsSystemThread(th) && th?["isDeleted"]?.GetValue<bool>() != true)
                .SelectMany(th => th?["comments"]?.AsArray() ?? [])
                .Where(c => c?["isDeleted"]?.GetValue<bool>() != true)
                .Where(c => rid is not null && Shape.Str(c?["author"]?["id"]) == rid && Shape.Str(c?["commentType"]) != "system")
                .Select(c => Shape.Date(c?["publishedDate"]))
                .OfType<DateTimeOffset>()
                .DefaultIfEmpty(DateTimeOffset.MaxValue)
                .Min();
            DateTimeOffset? first = new[] { votedAt ?? DateTimeOffset.MaxValue, firstComment }.Min() is var m && m != DateTimeOffset.MaxValue ? m : null;
            list.Add(new JsonObject
            {
                ["reviewer"] = Shape.Person(r),
                ["required"] = r?["isRequired"]?.DeepClone() ?? false,
                ["declined"] = r?["hasDeclined"]?.DeepClone() ?? false,
                ["vote"] = vote,
                ["voteText"] = Shape.VoteText(vote),
                ["votedAt"] = Shape.Iso(votedAt),
                ["firstResponseHours"] = created is { } c && first is { } f ? Math.Max(0, Shape.Hours(c, f)) : null,
            });
        }

        return list;
    }

    /// <summary>Date of the newest vote event of a reviewer (system thread "VoteUpdate").</summary>
    private static DateTimeOffset? VotedAt(JsonArray threads, string? reviewerId)
    {
        if (reviewerId is null)
        {
            return null;
        }

        DateTimeOffset? best = null;
        foreach (var th in threads)
        {
            var props = th?["properties"];
            if (Shape.Str(props?["CodeReviewThreadType"]?["$value"]) != "VoteUpdate")
            {
                continue;
            }

            var voter = Shape.Str(props?["CodeReviewVotedByIdentity"]?["$value"]);
            var voterId = voter is not null && th?["identities"]?[voter] is { } ident ? Shape.Str(ident["id"]) : voter;
            if (!string.Equals(voterId, reviewerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var date = Shape.Date(th?["publishedDate"]) ?? Shape.Date(th?["comments"]?[0]?["publishedDate"]);
            if (date is { } d && (best is null || d > best))
            {
                best = d;
            }
        }

        return best;
    }
}
