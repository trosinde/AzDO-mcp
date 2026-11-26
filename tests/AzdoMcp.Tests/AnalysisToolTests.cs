using System.Net;
using System.Text.Json.Nodes;
using AzdoMcp.Core;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;

namespace AzdoMcp.Tests;

/// <summary>Placeholder JSON builders for the Git, Pipelines and Test endpoints.</summary>
internal static class Az
{
    public static JsonObject Person(string name, string login, string id) => new()
    {
        ["displayName"] = name,
        ["uniqueName"] = login,
        ["id"] = id,
        ["imageUrl"] = "https://azdo.example.local/avatar/" + id,
        ["descriptor"] = "win.secretdescriptor",
        ["_links"] = new JsonObject { ["avatar"] = new JsonObject { ["href"] = "https://azdo.example.local/a" } },
    };

    public static readonly JsonObject Max = Person("Max Muster", "EXAMPLE\\max", "11111111-1111-1111-1111-111111111111");
    public static readonly JsonObject Erika = Person("Erika Muster", "EXAMPLE\\erika", "22222222-2222-2222-2222-222222222222");

    public static JsonObject Pr(int id, string status, string created, string? closed = null, JsonObject? author = null, string title = "Change", string repo = "app",
        params (JsonObject Who, int Vote)[] reviewers)
    {
        var o = new JsonObject
        {
            ["pullRequestId"] = id,
            ["title"] = title,
            ["status"] = status,
            ["creationDate"] = created,
            ["createdBy"] = (author ?? Max).DeepClone(),
            ["sourceRefName"] = "refs/heads/feature/" + id,
            ["targetRefName"] = "refs/heads/main",
            ["repository"] = new JsonObject { ["id"] = "repo-" + repo, ["name"] = repo, ["project"] = new JsonObject { ["name"] = "SHOP" } },
            ["reviewers"] = new JsonArray(reviewers.Select(r =>
            {
                var p = r.Who.DeepClone().AsObject();
                p["vote"] = r.Vote;
                return (JsonNode)p;
            }).ToArray()),
        };
        if (closed is not null)
        {
            o["closedDate"] = closed;
        }

        return o;
    }

    public static string List(params JsonNode[] items) => new JsonObject { ["value"] = new JsonArray(items.Select(i => i.DeepClone()).ToArray()), ["count"] = items.Length }.ToJsonString();

    public static JsonObject TextThread(int id, string status, string? file, params (JsonObject Who, string Date, string Text)[] comments) => new()
    {
        ["id"] = id,
        ["status"] = status,
        ["threadContext"] = file is null ? null : new JsonObject { ["filePath"] = file, ["rightFileStart"] = new JsonObject { ["line"] = 10 }, ["rightFileEnd"] = new JsonObject { ["line"] = 12 } },
        ["comments"] = new JsonArray(comments.Select((c, i) => (JsonNode)new JsonObject
        {
            ["id"] = i + 1,
            ["parentCommentId"] = i == 0 ? 0 : 1,
            ["author"] = c.Who.DeepClone(),
            ["publishedDate"] = c.Date,
            ["content"] = c.Text,
            ["commentType"] = "text",
        }).ToArray()),
    };

    public static JsonObject VoteThread(int id, JsonObject who, string date, int vote) => new()
    {
        ["id"] = id,
        ["publishedDate"] = date,
        ["properties"] = new JsonObject
        {
            ["CodeReviewThreadType"] = new JsonObject { ["$value"] = "VoteUpdate" },
            ["CodeReviewVotedByIdentity"] = new JsonObject { ["$value"] = "1" },
            ["CodeReviewVoteResult"] = new JsonObject { ["$value"] = vote.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        },
        ["identities"] = new JsonObject { ["1"] = who.DeepClone() },
        ["comments"] = new JsonArray(new JsonObject { ["id"] = 1, ["author"] = who.DeepClone(), ["content"] = "voted", ["commentType"] = "system", ["publishedDate"] = date }),
    };

    public static JsonObject Run(int id, string started, int? buildId = null, int? releaseId = null, string name = "Nightly") => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["state"] = "Completed",
        ["isAutomated"] = true,
        ["startedDate"] = started,
        ["completedDate"] = started,
        ["totalTests"] = 3,
        ["passedTests"] = 2,
        ["build"] = buildId is null ? null : new JsonObject { ["id"] = buildId.ToString(), ["name"] = $"2026110{buildId % 10}.1" },
        ["release"] = releaseId is null ? null : new JsonObject { ["id"] = releaseId, ["name"] = "Release-" + releaseId, ["environmentName"] = "Sanity", ["environmentId"] = 7 },
        ["plan"] = new JsonObject { ["id"] = "2691", ["name"] = "Plan SW1" },
    };

    public static JsonObject Result(int id, int runId, int testCase, string outcome, string started, int config = 1, int? buildId = null, params int[] bugs) => new()
    {
        ["id"] = id,
        ["testRun"] = new JsonObject { ["id"] = runId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        ["testCase"] = new JsonObject { ["id"] = testCase.ToString(System.Globalization.CultureInfo.InvariantCulture), ["name"] = "Case " + testCase },
        ["testCaseTitle"] = "Case " + testCase,
        ["configuration"] = new JsonObject { ["id"] = config.ToString(System.Globalization.CultureInfo.InvariantCulture), ["name"] = "Config " + config },
        ["automatedTestName"] = "Tests.Case" + testCase,
        ["outcome"] = outcome,
        ["durationInMs"] = 120.0,
        ["startedDate"] = started,
        ["build"] = buildId is null ? null : new JsonObject { ["id"] = buildId.ToString(), ["name"] = $"2026110{buildId % 10}.1" },
        ["errorMessage"] = outcome == "Failed" ? new string('x', 1500) : null,
        ["associatedBugs"] = new JsonArray(bugs.Select(b => (JsonNode)new JsonObject { ["id"] = b }).ToArray()),
        ["owner"] = Max.DeepClone(),
    };

    public static JsonNode Json(ToolCallResult r)
    {
        Assert.False(r.IsError, r.Text);
        return JsonNode.Parse(r.Text)!;
    }
}

public class SharedHelperTests
{
    [Fact]
    public void Vote_values_map_to_text()
    {
        Assert.Equal("approved", Shape.VoteText(10));
        Assert.Equal("approved with suggestions", Shape.VoteText(5));
        Assert.Equal("no vote", Shape.VoteText(0));
        Assert.Equal("waiting", Shape.VoteText(-5));
        Assert.Equal("rejected", Shape.VoteText(-10));
    }

    [Fact]
    public void Person_has_no_identity_noise()
    {
        var p = Shape.Person(Az.Max)!;
        Assert.Equal("Max Muster", p["name"]!.GetValue<string>());
        Assert.Equal("EXAMPLE\\max", p["login"]!.GetValue<string>());
        Assert.Equal(2, p.Count);
    }

    [Fact]
    public void Windows_cover_period_without_gaps_newest_first()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(20);
        var w = Paging.Windows(from, to).ToList();
        // First window 1 day (newest runs rarely need a whole week), then 7, 7 and the rest.
        Assert.Equal(4, w.Count);
        Assert.Equal(1, (w[0].To - w[0].From).TotalDays);
        Assert.Equal(to, w[0].To);
        Assert.Equal(from, w[^1].From);
        for (var i = 1; i < w.Count; i++)
        {
            Assert.Equal(w[i - 1].From, w[i].To);
        }

        Assert.All(w, x => Assert.True((x.To - x.From).TotalDays <= 7));
    }

    [Fact]
    public void Median_and_p90()
    {
        Assert.Equal(2.5, Aggregate.Median([1, 2, 3, 4]));
        Assert.Equal(3, Aggregate.Median([5, 1, 3]));
        Assert.Equal(9, Aggregate.Percentile([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 90));
        Assert.Null(Aggregate.Median([]));
    }

    [Fact]
    public void Unknown_metric_lists_allowed()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => Aggregate.Metrics(["count", "speed"], AnalysisTools.PrMetrics, ["count"]));
        Assert.Contains("speed", ex.Message);
        Assert.Contains("cycleTime", ex.Message);
    }

    [Fact]
    public void Bad_pattern_is_rejected()
    {
        Assert.Throws<ToolArgumentException>(() => Aggregate.Pattern("([", "refPattern"));
        Assert.Throws<ToolArgumentException>(() => Aggregate.Pattern(new string('a', 201), "refPattern"));
    }

    [Fact]
    public void Release_host_on_services()
    {
        var services = new Connection { CollectionUrl = "https://dev.azure.com/example", ServerType = ServerType.Services };
        Assert.Equal("https://vsrm.dev.azure.com/example", PipelineTools.ReleaseBase(services));
        var old = new Connection { CollectionUrl = "https://example.visualstudio.com", ServerType = ServerType.Services };
        Assert.Equal("https://example.vsrm.visualstudio.com", PipelineTools.ReleaseBase(old));
        var server = new Connection { CollectionUrl = Fake.Collection, ServerType = ServerType.Server };
        Assert.Equal(Fake.Collection, PipelineTools.ReleaseBase(server));
    }
}

public class GitAnalysisToolTests
{
    [Fact]
    public async Task Branches_ahead_behind_and_stale_without_pr()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/repositories/app/stats/branches", Az.List(
            new JsonObject { ["name"] = "main", ["isBaseVersion"] = true, ["aheadCount"] = 0, ["behindCount"] = 0, ["commit"] = new JsonObject { ["commitId"] = "a1", ["committer"] = new JsonObject { ["date"] = "2026-11-10T00:00:00Z" }, ["author"] = new JsonObject { ["name"] = "Max Muster" } } },
            new JsonObject { ["name"] = "feature/a", ["aheadCount"] = 3, ["behindCount"] = 5, ["commit"] = new JsonObject { ["commitId"] = "b2", ["committer"] = new JsonObject { ["date"] = "2026-01-01T00:00:00Z" } } },
            new JsonObject { ["name"] = "feature/b", ["aheadCount"] = 1, ["behindCount"] = 0, ["commit"] = new JsonObject { ["commitId"] = "c3", ["committer"] = new JsonObject { ["date"] = "2026-01-01T00:00:00Z" } } }));
        h.Http.On("/repositories/app/pullrequests", Az.List(Az.Pr(7, "active", "2026-11-01T00:00:00Z") is var pr && (pr["sourceRefName"] = "refs/heads/feature/b") is not null ? pr : pr));

        var all = Az.Json(await h.Call("get_branches", new { repository = "app" }));
        var a = all["branches"]!.AsArray().Single(b => b!["name"]!.GetValue<string>() == "feature/a")!;
        Assert.Equal(3, a["ahead"]!.GetValue<int>());
        Assert.Equal(5, a["behind"]!.GetValue<int>());
        Assert.Equal(7, all["branches"]!.AsArray().Single(b => b!["name"]!.GetValue<string>() == "feature/b")!["activePullRequest"]!.GetValue<int>());

        var stale = Az.Json(await h.Call("get_branches", new { repository = "app", staleDays = 30, withoutPullRequest = true }));
        Assert.Equal(["feature/a"], stale["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Pull_requests_completed_in_period_across_pages()
    {
        using var h = new Harness("SHOP");
        // 150 completed PRs, newest first; page size of the tool is 100.
        var prs = Enumerable.Range(1, 150).Select(i =>
        {
            var created = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-i);
            return (JsonNode)Az.Pr(1000 - i, "completed", created.ToString("o"), created.AddDays(1).ToString("o"));
        }).ToList();
        h.Http.OnRaw("/_apis/git/pullrequests?", req =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query);
            var skip = int.Parse(q["$skip"]!);
            var top = int.Parse(q["$top"]!);
            return FakeHttp.Json(new JsonObject { ["value"] = new JsonArray(prs.Skip(skip).Take(top).Select(p => p!.DeepClone()).ToArray()) }.ToJsonString());
        });

        var r = Az.Json(await h.Call("get_pull_requests", new { status = "completed", from = "2026-03-01", to = "2026-03-31T23:59:59Z", top = 1000 }));
        var ids = r["pullRequests"]!.AsArray().Select(p => p!["id"]!.GetValue<int>()).ToList();
        // Closed in March: created Feb 28 – Mar 30 → i = 63..93
        Assert.Equal(31, ids.Count);
        Assert.Contains(h.Http.Requests, q => q.Url.Contains("$skip=100"));
        Assert.All(r["pullRequests"]!.AsArray(), p => Assert.StartsWith("2026-03", p!["closed"]!.GetValue<string>()));
        Assert.False(r["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Pull_requests_truncated_and_compact()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/git/pullrequests?", Az.List(
            Az.Pr(3, "completed", "2026-11-01T00:00:00Z", "2026-11-01T05:00:00Z", reviewers: (Az.Erika, 10)),
            Az.Pr(2, "active", "2026-10-01T00:00:00Z"),
            Az.Pr(1, "active", "2026-09-01T00:00:00Z")));
        var r = await h.Call("get_pull_requests", new { top = 2 });
        var json = Az.Json(r);
        Assert.True(json["truncated"]!.GetValue<bool>());
        Assert.Equal(2, json["count"]!.GetValue<int>());
        Assert.Equal(5, json["pullRequests"]![0]!["cycleTimeHours"]!.GetValue<double>());
        Assert.DoesNotContain("avatar", r.Text);
        Assert.DoesNotContain("descriptor", r.Text);
        Assert.DoesNotContain("imageUrl", r.Text);
    }

    [Fact]
    public async Task Reviewers_vote_time_threads_files_and_work_items()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/git/pullrequests?", Az.List(Az.Pr(5, "completed", "2026-11-01T08:00:00Z", "2026-11-02T08:00:00Z", reviewers: (Az.Erika, 10))));
        h.Http.On("/pullRequests/5/threads", Az.List(
            Az.VoteThread(1, Az.Erika, "2026-11-01T10:00:00Z", 10),
            Az.TextThread(2, "fixed", "/src/a.cs", (Az.Erika, "2026-11-01T12:00:00Z", "Bitte umbenennen"), (Az.Max, "2026-11-01T13:00:00Z", "Erledigt")),
            Az.TextThread(3, "active", null, (Az.Erika, "2026-11-01T14:00:00Z", "Frage"))));
        h.Http.On("/pullRequests/5/iterations?", Az.List(new JsonObject { ["id"] = 1, ["createdDate"] = "2026-11-01T08:00:00Z", ["author"] = Az.Max.DeepClone() }, new JsonObject { ["id"] = 3, ["createdDate"] = "2026-11-01T15:00:00Z", ["author"] = Az.Max.DeepClone() }));
        h.Http.On("/pullRequests/5/iterations/3/changes", new JsonObject
        {
            ["changeEntries"] = new JsonArray(
                new JsonObject { ["item"] = new JsonObject { ["path"] = "/src/a.cs" }, ["changeType"] = "edit" },
                new JsonObject { ["item"] = new JsonObject { ["path"] = "/src/b.cs" }, ["changeType"] = "add" },
                new JsonObject { ["item"] = new JsonObject { ["path"] = "/src/a.cs" }, ["changeType"] = "edit" },
                new JsonObject { ["item"] = new JsonObject { ["path"] = "/src", ["isFolder"] = true }, ["changeType"] = "edit" }),
        }.ToJsonString());
        h.Http.On("/pullRequests/5/workitems", Az.List(new JsonObject { ["id"] = "4711" }));

        var pr = Az.Json(await h.Call("get_pull_requests", new { include = new[] { "reviewers", "threads", "iterations", "files", "workItems" } }))["pullRequests"]![0]!;
        var rev = pr["reviewers"]![0]!;
        Assert.Equal("approved", rev["voteText"]!.GetValue<string>());
        Assert.Equal("2026-11-01T10:00:00Z", rev["votedAt"]!.GetValue<string>());
        Assert.Equal(2, rev["firstResponseHours"]!.GetValue<double>());

        Assert.Equal(2, pr["threads"]!.AsArray().Count);
        Assert.Equal(1, pr["systemThreads"]!.GetValue<int>());
        var t2 = pr["threads"]![0]!;
        Assert.Equal("/src/a.cs", t2["file"]!.GetValue<string>());
        Assert.Equal(10, t2["lines"]!["from"]!.GetValue<int>());
        Assert.Equal("Erledigt", t2["comments"]![1]!["text"]!.GetValue<string>());

        Assert.Equal(["/src/a.cs", "/src/b.cs"], pr["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()));
        Assert.Equal(2, pr["iterations"]!.AsArray().Count);
        Assert.Equal([4711], pr["workItems"]!.AsArray().Select(w => w!.GetValue<int>()));
    }

    [Fact]
    public async Task Include_cap_note()
    {
        using var h = new Harness("SHOP");
        var all = Enumerable.Range(1, GitTools.IncludeCap + 5).Select(i => (JsonNode)Az.Pr(i, "active", "2026-11-01T00:00:00Z")).ToList();
        h.Http.OnRaw("/_apis/git/pullrequests?", req =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query);
            return FakeHttp.Json(Az.List(all.Skip(int.Parse(q["$skip"]!)).Take(int.Parse(q["$top"]!)).ToArray()));
        });
        h.Http.On("/threads", Az.List());
        var r = Az.Json(await h.Call("get_pull_requests", new { include = new[] { "threads" }, top = 1000 }));
        Assert.Contains("Details für 5 PRs nicht geladen", r["notes"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(GitTools.IncludeCap, h.Http.Requests.Count(q => q.Url.Contains("/threads")));
    }

    [Fact]
    public async Task Pull_request_by_id_and_unknown_id()
    {
        using var h = new Harness("SHOP");
        var pr = Az.Pr(9, "active", "2026-11-01T00:00:00Z");
        pr["description"] = "Beschreibung";
        h.Http.On("/_apis/git/pullrequests/9?", pr.ToJsonString());
        h.Http.On("/pullRequests/9/", Az.List());
        var ok = Az.Json(await h.Call("get_pull_request", new { id = 9 }));
        Assert.Equal("Beschreibung", ok["description"]!.GetValue<string>());
        Assert.NotNull(ok["threads"]);

        h.Http.On("/_apis/git/pullrequests/10?", "{}", HttpStatusCode.NotFound);
        var missing = await h.Call("get_pull_request", new { id = 10 });
        Assert.True(missing.IsError);
        Assert.Contains("Pull Request 10 gibt es in SHOP nicht", missing.Text);
    }

    private static Harness PrHarness()
    {
        var h = new Harness("SHOP");
        var max2 = Az.Person("M. Muster", "EXAMPLE\\max", "11111111-1111-1111-1111-111111111111");
        h.Http.On("/_apis/git/pullrequests?", Az.List(
            Az.Pr(1, "completed", "2026-10-01T00:00:00Z", "2026-10-01T10:00:00Z", Az.Max, reviewers: (Az.Erika, 10)),
            Az.Pr(2, "completed", "2026-10-02T00:00:00Z", "2026-10-02T20:00:00Z", max2, reviewers: (Az.Erika, -10)),
            Az.Pr(3, "abandoned", "2026-10-03T00:00:00Z", "2026-10-03T01:00:00Z", Az.Erika, title: "[TEST] Bot check")));
        h.Http.On("/pullRequests/1/threads", Az.List(Az.TextThread(1, "fixed", "/a.cs", (Az.Erika, "2026-10-01T02:00:00Z", "1"), (Az.Max, "2026-10-01T03:00:00Z", "2"), (Az.Erika, "2026-10-01T04:00:00Z", "3"))));
        h.Http.On("/pullRequests/2/threads", Az.List(
            Az.TextThread(1, "active", "/b.cs", (Az.Erika, "2026-10-02T01:00:00Z", "1"), (Az.Erika, "2026-10-02T02:00:00Z", "2"), (Az.Erika, "2026-10-02T03:00:00Z", "3"), (Az.Erika, "2026-10-02T04:00:00Z", "4")),
            Az.TextThread(2, "wontFix", "/c.cs", (Az.Erika, "2026-10-02T05:00:00Z", "5"), (Az.Max, "2026-10-02T06:00:00Z", "6"), (Az.Erika, "2026-10-02T07:00:00Z", "7"))));
        h.Http.On("/pullRequests/3/threads", Az.List());
        h.Http.On("/pullRequests/1/iterations?", Az.List(new JsonObject { ["id"] = 1 }));
        h.Http.On("/pullRequests/2/iterations?", Az.List(new JsonObject { ["id"] = 1 }));
        h.Http.On("/pullRequests/3/iterations?", Az.List(new JsonObject { ["id"] = 1 }));
        h.Http.On("/pullRequests/1/iterations/1/changes", """{"changeEntries":[{"item":{"path":"/a.cs"}},{"item":{"path":"/x.cs"}}]}""");
        h.Http.On("/pullRequests/2/iterations/1/changes", """{"changeEntries":[{"item":{"path":"/b.cs"}},{"item":{"path":"/c.cs"}}]}""");
        h.Http.On("/pullRequests/3/iterations/1/changes", """{"changeEntries":[]}""");
        return h;
    }

    [Fact]
    public async Task Developer_ranking_groups_by_login_and_counts_comments_per_file()
    {
        using var h = PrHarness();
        var r = Az.Json(await h.Call("analyze_pull_requests", new
        {
            groupBy = "author",
            metrics = new[] { "count", "cycleTime", "comments", "commentsPerPr", "commentsPerFile" },
            exclude = new { titlePattern = "^\\[TEST\\]" },
        }));
        Assert.Equal(1, r["excluded"]!.GetValue<int>());
        var max = r["rows"]!.AsArray().Single()!;
        Assert.Equal("EXAMPLE\\max", max["key"]!.GetValue<string>());
        Assert.Equal(2, max["count"]!.GetValue<int>());
        Assert.Equal(10, max["comments"]!.GetValue<int>());
        Assert.Equal(5, max["commentsPerPr"]!.GetValue<double>());
        Assert.Equal(2.5, max["commentsPerFile"]!.GetValue<double>());
        Assert.Equal(15, max["cycleTimeHours"]!["avg"]!.GetValue<double>());
    }

    [Fact]
    public async Task Reviewer_rates_and_first_response()
    {
        using var h = PrHarness();
        var r = Az.Json(await h.Call("analyze_pull_requests", new
        {
            groupBy = "reviewer",
            metrics = new[] { "count", "comments", "firstResponse", "approvalRate", "rejectionRate" },
        }));
        var erika = r["rows"]!.AsArray().Single(x => x!["key"]!.GetValue<string>() == "EXAMPLE\\erika")!;
        Assert.Equal(2, erika["count"]!.GetValue<int>());
        Assert.Equal(8, erika["comments"]!.GetValue<int>());
        Assert.Equal(50, erika["approvalRate"]!.GetValue<double>());
        Assert.Equal(50, erika["rejectionRate"]!.GetValue<double>());
        Assert.Equal(1.5, erika["firstResponseHours"]!["avg"]!.GetValue<double>());
    }

    [Fact]
    public async Task Thread_status_counts()
    {
        using var h = PrHarness();
        var r = Az.Json(await h.Call("analyze_pull_requests", new { metrics = new[] { "threadStatus" } }));
        var s = r["total"]!["threadStatus"]!;
        Assert.Equal(1, s["fixed"]!.GetValue<int>());
        Assert.Equal(1, s["active"]!.GetValue<int>());
        Assert.Equal(1, s["wontFix"]!.GetValue<int>());
    }
}

public class PipelineAndTestToolTests
{
    [Fact]
    public async Task Builds_by_tag_filter_in_request()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/build/builds?", Az.List(new JsonObject
        {
            ["id"] = 77, ["buildNumber"] = "20261102.1", ["definition"] = new JsonObject { ["id"] = 305, ["name"] = "Image" },
            ["sourceBranch"] = "refs/heads/main", ["tags"] = new JsonArray("nightly"), ["result"] = "succeeded", ["requestedFor"] = Az.Max.DeepClone(),
        }));
        var r = Az.Json(await h.Call("get_builds", new { pipelineId = 305, tags = new[] { "nightly" }, from = "2026-11-01" }));
        Assert.Equal("20261102.1", r["builds"]![0]!["number"]!.GetValue<string>());
        Assert.Equal("main", r["builds"]![0]!["branch"]!.GetValue<string>());
        var url = Uri.UnescapeDataString(h.Http.Requests.Single().Url);
        Assert.Contains("definitions=305", url);
        Assert.Contains("tagFilters=nightly", url);
        Assert.Contains("minTime=2026-11-01", url);
    }

    [Fact]
    public async Task Releases_not_available()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/release/releases", "{}", HttpStatusCode.NotFound);
        var r = await h.Call("get_releases", new { });
        Assert.True(r.IsError);
        Assert.Contains("nicht verfügbar", r.Text);
    }

    [Fact]
    public async Task Test_runs_newest_first_with_release_and_long_period_split()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/test/runs?", Az.List(Az.Run(1, "2026-10-01T00:00:00Z", 11), Az.Run(3, "2026-10-20T00:00:00Z", 13, 42), Az.Run(2, "2026-10-10T00:00:00Z", 12)));
        var r = Az.Json(await h.Call("get_test_runs", new { from = "2026-10-01", to = "2026-10-21" }));
        var runs = r["runs"]!.AsArray();
        Assert.Equal([3, 2, 1], runs.Select(x => x!["id"]!.GetValue<int>()));
        Assert.Equal("Release-42", runs[0]!["release"]!["name"]!.GetValue<string>());
        Assert.Equal("Sanity", runs[0]!["release"]!["stage"]!.GetValue<string>());
        // 20 days → windows of 1, 7, 7 and 5 days, each a separate query; duplicates merged.
        Assert.Equal(4, h.Http.Requests.Count(q => q.Url.Contains("minLastUpdatedDate")));
    }

    [Fact]
    public async Task Test_runs_by_release_definition_in_query()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/test/runs?", Az.List());
        await h.Call("get_test_runs", new { releaseDefinitionId = 10, branch = "master" });
        var url = Uri.UnescapeDataString(h.Http.Requests[0].Url);
        Assert.Contains("releaseDefIds=10", url);
        Assert.Contains("branchName=refs/heads/master", url);
    }

    [Fact]
    public async Task Results_latest_per_testcase_and_configuration_with_counts()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/test/runs?", Az.List(Az.Run(1, "2026-11-01T00:00:00Z"), Az.Run(2, "2026-11-02T00:00:00Z")));
        h.Http.On("/test/Runs/1/results", Az.List(Az.Result(10, 1, 501, "Failed", "2026-11-01T00:00:00Z"), Az.Result(11, 1, 502, "Passed", "2026-11-01T00:00:00Z")));
        h.Http.On("/test/Runs/2/results", Az.List(Az.Result(20, 2, 501, "Passed", "2026-11-02T00:00:00Z"), Az.Result(21, 2, 501, "Passed", "2026-11-02T00:00:00Z", config: 2)));
        var r = Az.Json(await h.Call("get_test_results", new { planId = 2691, from = "2026-10-30", to = "2026-11-03", latest = "testCase+configuration" }));
        Assert.Equal(3, r["total"]!.GetValue<int>());
        Assert.Equal(3, r["outcomes"]!["Passed"]!.GetValue<int>());
        Assert.Equal(20, r["results"]!.AsArray().Single(x => x!["testCase"]!["id"]!.GetValue<int>() == 501 && x["configuration"]!["id"]!.GetValue<int>() == 1)!["id"]!.GetValue<long>());
        Assert.Contains(h.Http.Requests, q => Uri.UnescapeDataString(q.Url).Contains("planIds=2691"));
    }

    [Fact]
    public async Task Results_selection_ambiguous()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("get_test_results", new { runId = 1, releaseId = 42 });
        Assert.True(r.IsError);
        Assert.Contains("Genau eines von runId, buildId, releaseId oder planId", r.Text);
    }

    [Fact]
    public async Task Result_has_linked_bug_and_short_error()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/test/Runs/5/results", Az.List(Az.Result(1, 5, 501, "Failed", "2026-11-01T00:00:00Z", 1, null, 812)));
        var res = Az.Json(await h.Call("get_test_results", new { runId = 5 }))["results"]![0]!;
        Assert.Equal([812], res["bugs"]!.AsArray().Select(b => b!.GetValue<int>()));
        Assert.Equal(1001, res["errorMessage"]!.GetValue<string>().Length);
        Assert.Equal("Max Muster", res["owner"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_details_loaded_when_query_has_no_build()
    {
        using var h = new Harness("SHOP");
        var bare = Az.Run(1, "2026-11-02T00:00:00Z");
        bare["build"] = new JsonObject();
        bare.Remove("plan");
        h.Http.On("/_apis/test/runs?", Az.List(bare));
        h.Http.On("/_apis/test/runs/1?", Az.Run(1, "2026-11-02T00:00:00Z", 77).ToJsonString());
        var r = Az.Json(await h.Call("get_test_runs", new { from = "2026-11-01", to = "2026-11-03" }));
        Assert.Equal(77, r["runs"]![0]!["build"]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Stage_names_from_release_and_one_row_per_stage()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/test/runs?", Az.List(Az.Run(1, "2026-11-01T00:00:00Z", 1, 42)));
        var r1 = Az.Result(10, 1, 501, "Passed", "2026-11-01T00:00:00Z");
        r1["releaseReference"] = new JsonObject { ["id"] = 42, ["name"] = "Release-42", ["environmentId"] = 7, ["environmentDefinitionId"] = 3 };
        var r2 = Az.Result(11, 1, 502, "Failed", "2026-11-01T00:00:00Z");
        r2["releaseReference"] = new JsonObject { ["id"] = 42, ["name"] = "Release-42", ["environmentId"] = 8, ["environmentDefinitionId"] = 3 };
        h.Http.On("/test/Runs/1/results", Az.List(r1, r2));
        h.Http.On("/_apis/release/releases/42?", new JsonObject
        {
            ["id"] = 42,
            ["environments"] = new JsonArray(new JsonObject { ["id"] = 7, ["name"] = "[S] Device A" }, new JsonObject { ["id"] = 8, ["name"] = "[S] Device A" }),
        }.ToJsonString());
        var r = Az.Json(await h.Call("analyze_test_results", new { from = "2026-10-30", to = "2026-11-03", groupBy = "stage" }));
        var row = r["rows"]!.AsArray().Single()!;
        Assert.Equal("[S] Device A", row["key"]!.GetValue<string>());
        Assert.Equal(2, row["total"]!.GetValue<int>());
    }

    private static Harness BuildHarness()
    {
        var h = new Harness("SHOP");
        h.Http.On("/_apis/test/runs?", Az.List(Az.Run(1, "2026-11-01T00:00:00Z", 1), Az.Run(2, "2026-11-02T00:00:00Z", 2)));
        h.Http.On("/test/Runs/1/results", Az.List(
            Az.Result(10, 1, 501, "Passed", "2026-11-01T00:00:00Z", buildId: 1), Az.Result(11, 1, 502, "Failed", "2026-11-01T00:00:00Z", buildId: 1),
            Az.Result(12, 1, 502, "Passed", "2026-11-01T01:00:00Z", buildId: 1), Az.Result(13, 1, 503, "Failed", "2026-11-01T00:00:00Z", buildId: 1)));
        h.Http.On("/test/Runs/2/results", Az.List(
            Az.Result(20, 2, 501, "Failed", "2026-11-02T00:00:00Z", buildId: 2), Az.Result(21, 2, 502, "Aborted", "2026-11-02T00:00:00Z", buildId: 2),
            Az.Result(22, 2, 503, "Failed", "2026-11-02T00:00:00Z", buildId: 2), Az.Result(23, 2, 504, "NotExecuted", "2026-11-02T00:00:00Z", buildId: 2)));
        return h;
    }

    [Fact]
    public async Task Results_per_build_latest_counts_aborted_as_failed()
    {
        using var h = BuildHarness();
        var r = Az.Json(await h.Call("analyze_test_results", new { releaseDefinitionId = 10, from = "2026-10-30", to = "2026-11-03", groupBy = "build" }));
        var rows = r["rows"]!.AsArray();
        Assert.Equal(["20261101.1", "20261102.1"], rows.Select(x => x!["key"]!.GetValue<string>()));
        // Build 1: 501 passed, 502 latest passed (re-run), 503 failed.
        Assert.Equal(3, rows[0]!["total"]!.GetValue<int>());
        Assert.Equal(1, rows[0]!["failed"]!.GetValue<int>());
        Assert.Equal(66.7, rows[0]!["passRate"]!.GetValue<double>());
        // Build 2: Failed + Aborted + Failed, one not executed.
        Assert.Equal(3, rows[1]!["failed"]!.GetValue<int>());
        Assert.Equal(1, rows[1]!["notExecuted"]!.GetValue<int>());
        Assert.Equal(0, rows[1]!["passRate"]!.GetValue<double>());
    }

    [Fact]
    public async Task New_failure_between_builds_and_failing_since()
    {
        using var h = BuildHarness();
        var r = Az.Json(await h.Call("analyze_test_results", new { from = "2026-10-30", to = "2026-11-03", groupBy = "build", metrics = new[] { "failed", "newFailures", "failingSince" } }));
        var b2 = r["rows"]![1]!;
        var newFail = b2["newFailures"]!.AsArray().Select(x => x!["testCase"]!["id"]!.GetValue<int>()).Order().ToList();
        Assert.Equal([501, 502], newFail);
        var since503 = b2["failingSince"]!.AsArray().Single(x => x!["testCase"]!["id"]!.GetValue<int>() == 503)!;
        Assert.Equal("20261101.1", since503["since"]!.GetValue<string>());
    }
}

public class TraceToolTests
{
    private static JsonObject Wi(int id, string type, string title, string project = "SHOP", params JsonObject[] relations) => new()
    {
        ["id"] = id,
        ["fields"] = new JsonObject { ["System.WorkItemType"] = type, ["System.Title"] = title, ["System.State"] = "Active", ["System.TeamProject"] = project },
        ["relations"] = new JsonArray(relations.Select(r => (JsonNode)r).ToArray()),
    };

    private static JsonObject Rel(string rel, string url, string? name = null) => new() { ["rel"] = rel, ["url"] = url, ["attributes"] = new JsonObject { ["name"] = name } };

    private static string WiUrl(int id) => $"{Fake.Collection}/_apis/wit/workItems/{id}";

    [Fact]
    public async Task Trace_story_to_prs_and_tests_with_foreign_link_counted()
    {
        using var h = new Harness("SHOP");
        h.SetProject("SHOP", p => p.AzdoId = "proj-1");
        h.Http.OnRaw("/_apis/wit/workitemsbatch", req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            var ids = JsonNode.Parse(body)!["ids"]!.AsArray().Select(i => i!.GetValue<int>()).ToHashSet();
            var all = new[]
            {
                Wi(4711, "User Story", "Story REQ-020",
                    relations: [
                        Rel("ArtifactLink", "vstfs:///Git/PullRequestId/proj-1%2Frepo-app%2F88", "Pull Request"),
                        Rel("ArtifactLink", "vstfs:///Git/PullRequestId/other%2Frepo-x%2F99", "Pull Request"),
                        Rel("Microsoft.VSTS.Common.TestedBy-Forward", WiUrl(501)),
                    ]),
                Wi(501, "Test Case", "Case 501"),
            };
            return FakeHttp.Json(Az.List(all.Where(w => ids.Contains(w["id"]!.GetValue<int>())).ToArray<JsonNode>()));
        });
        h.Http.On("/_apis/git/pullrequests/88?", Az.Pr(88, "completed", "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", title: "Fix REQ-020").ToJsonString());
        h.Http.On("/_apis/test/points", new JsonObject
        {
            ["value"] = new JsonArray(new JsonObject { ["testCase"] = new JsonObject { ["id"] = "501", ["name"] = "Case 501" }, ["outcome"] = "Failed", ["lastTestRun"] = new JsonObject { ["id"] = "5" }, ["lastResult"] = new JsonObject { ["id"] = "1" } }),
        }.ToJsonString());

        var r = Az.Json(await h.Call("trace_links", new { workItems = new[] { 4711 }, include = new[] { "pullRequests", "testCases", "testResults" }, refPattern = "REQ-\\d+" }));
        var summary = r["summaries"]![0]!["summary"]!.GetValue<string>();
        Assert.Contains("1 PRs (1 gemergt)", summary);
        Assert.Contains("1 Tests (1 rot)", summary);
        Assert.Contains("1 Verknüpfung in andere Projekte", summary);
        Assert.DoesNotContain(h.Http.Requests, q => q.Url.Contains("pullrequests/99"));
        var keys = r["nodes"]!.AsArray().Select(n => n!["key"]!.GetValue<string>()).ToList();
        Assert.Contains("ref:REQ-020", keys);
        Assert.Equal(2, r["edges"]!.AsArray().Count(e => e!["source"]!.GetValue<string>() == "text"));
    }

    [Fact]
    public async Task Trace_pr_to_work_items()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/_apis/git/pullrequests/88?", Az.Pr(88, "active", "2026-11-01T00:00:00Z").ToJsonString());
        h.Http.On("/pullRequests/88/workitems", Az.List(new JsonObject { ["id"] = "4711" }));
        h.Http.On("/_apis/wit/workitemsbatch", Az.List(Wi(4711, "User Story", "Story")));
        var r = Az.Json(await h.Call("trace_links", new { pullRequest = 88, depth = 2 }));
        Assert.Contains(r["nodes"]!.AsArray(), n => n!["key"]!.GetValue<string>() == "workItem:4711" && n["title"]!.GetValue<string>() == "Story");
        Assert.Contains("1 Work Items", r["summaries"]![0]!["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task Trace_without_test_right_leaves_test_out()
    {
        using var h = new Harness("SHOP");
        h.SetConn("SHOP", c => c.PatScope = Acc.Of(Acc.W, test: Acc.N));
        h.Http.On("/_apis/wit/workitemsbatch", Az.List(Wi(4711, "User Story", "Story", relations: Rel("Microsoft.VSTS.Common.TestedBy-Forward", WiUrl(501))), Wi(501, "Test Case", "Case")));
        var r = Az.Json(await h.Call("trace_links", new { workItems = new[] { 4711 }, include = new[] { "testCases", "testResults" } }));
        Assert.Contains("Test: kein Lesezugriff – ausgelassen", r["notes"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.DoesNotContain(h.Http.Requests, q => q.Url.Contains("/_apis/test/"));
    }

    [Fact]
    public async Task Trace_needs_exactly_one_start()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("trace_links", new { workItems = new[] { 1 }, pullRequest = 2 });
        Assert.True(r.IsError);
        Assert.Contains("Genau einen Start", r.Text);
    }
}

public class PipelinesAreaTests
{
    [Fact]
    public void Pipelines_missing_reads_unknown()
    {
        var stored = Acc.Of(Acc.W);
        stored.Areas.Remove(AccessArea.Pipelines);
        Assert.Null(stored[AccessArea.Pipelines].Read);
        Assert.Equal(AccessArea.Pipelines, Access.AreaOf(ToolGroup.Pipelines));
        Assert.Contains(AccessArea.Pipelines, Access.Areas);
    }

    [Fact]
    public void Pipelines_group_default_allowed()
    {
        Assert.Equal(PermissionMode.Allowed, PermissionSettings.DefaultFor(ToolGroup.Pipelines));
    }

    [Fact]
    public async Task Pipelines_denied_blocks_pipeline_tools_and_tags_filter()
    {
        using var h = new Harness("SHOP");
        h.SetConn("SHOP", c => c.PatScope = Acc.Of(Acc.W, pipelines: Acc.N));
        var blocked = await h.Call("get_builds", new { });
        Assert.True(blocked.IsError);
        Assert.Contains("Pipelines: kein Zugriff", blocked.Text);

        var tags = await h.Call("get_test_runs", new { tags = new[] { "nightly" } });
        Assert.True(tags.IsError);
        Assert.Contains("braucht Pipelines Lesen", tags.Text);
    }
}
