using System.Net;
using System.Text.Json.Nodes;
using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Tools;

namespace AzdoMcp.Tests;

public class ToolCatalogTests
{
    [Fact]
    public void Catalogue_has_31_tools_in_seven_fixed_groups()
    {
        var expected = new Dictionary<ToolGroup, string[]>
        {
            [ToolGroup.WorkItemsRead] = ["get_work_item", "query_by_id", "execute_wiql", "query_work_items", "get_work_items_with_relations", "get_linked_work_items", "get_work_item_revisions", "trace_links"],
            [ToolGroup.WorkItemsWrite] = ["create_work_item", "update_work_item", "add_work_item_comment", "update_work_item_comment", "add_work_item_relation", "add_work_item_attachment"],
            [ToolGroup.Git] = ["get_repositories", "get_commits", "get_branches", "get_pull_requests", "get_pull_request", "analyze_pull_requests"],
            [ToolGroup.Pipelines] = ["get_pipelines", "get_builds", "get_releases"],
            [ToolGroup.Test] = ["get_test_plans", "get_test_runs", "get_test_results", "analyze_test_results"],
            [ToolGroup.Planning] = ["get_iterations", "get_iteration_capacity"],
            [ToolGroup.System] = ["list_projects", "ping"],
        };

        Assert.Equal(31, ToolCatalog.All.Count);
        Assert.Equal(31, ToolCatalog.All.Select(t => t.Name).Distinct().Count());
        foreach (var (group, names) in expected)
        {
            Assert.Equal(names.Order(), ToolCatalog.All.Where(t => t.Group == group).Select(t => t.Name).Order());
        }

        Assert.All(ToolCatalog.All, t => Assert.Equal(t.Group == ToolGroup.WorkItemsWrite, t.IsWrite));
    }

    [Fact]
    public void Every_connection_tool_accepts_project()
    {
        Assert.All(ToolCatalog.All.Where(t => t.NeedsConnection), t => Assert.NotNull(t.InputSchema["properties"]?["project"]));
    }
}

public class ToolExecutorTests
{
    private const string WorkItem = """{"id":123,"rev":4,"fields":{"System.Title":"Fix it","System.State":"Active","System.WorkItemType":"Bug"}}""";

    [Fact]
    public async Task Get_work_item_returns_fields_and_logs()
    {
        using var h = new Harness("SHOP");
        h.Http.On("/SHOP/_apis/wit/workitems/123", WorkItem);
        var r = await h.Call("get_work_item", new { id = 123, project = "SHOP" });

        Assert.False(r.IsError, r.Text);
        var json = JsonNode.Parse(r.Text)!;
        Assert.Equal(123, json["id"]!.GetValue<int>());
        Assert.Equal("Fix it", json["fields"]!["System.Title"]!.GetValue<string>());

        var rec = h.Records().Single();
        Assert.Equal("Claude Code", rec.Client);
        Assert.Equal("get_work_item", rec.Tool);
        Assert.Equal("SHOP", rec.Connection);
        Assert.Equal(AuditOutcome.Ok, rec.Outcome);
        Assert.Equal("OK", rec.Result);
        Assert.Equal(123, rec.WorkItemId);
    }

    [Fact]
    public async Task Project_is_case_insensitive()
    {
        using var h = new Harness("default", "SHOP");
        h.Http.On("/SHOP/_apis/wit/workitems/123", WorkItem);
        var r = await h.Call("get_work_item", new { id = 123, project = "shop" });
        Assert.False(r.IsError, r.Text);
    }

    [Fact]
    public async Task Without_project_default_connection_is_used()
    {
        using var h = new Harness("default", "SHOP");
        h.Http.On("/default/_apis/wit/workitems/1", WorkItem.Replace("123", "1"));
        var r = await h.Call("get_work_item", new { id = 1 });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("/default/", h.Http.Calls.Single().Url);
    }

    [Fact]
    public async Task Unknown_project_lists_known_names()
    {
        using var h = new Harness("default", "SHOP");
        var r = await h.Call("get_work_item", new { id = 1, project = "NOPE" });
        Assert.True(r.IsError);
        Assert.Contains("NOPE", r.Text);
        Assert.Contains("DEFAULT, SHOP", r.Text);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task No_connection_configured()
    {
        using var h = new Harness();
        var r = await h.Call("get_work_item", new { id = 1 });
        Assert.True(r.IsError);
        Assert.Contains("Kein Projekt eingerichtet", r.Text);
    }

    [Fact]
    public async Task Expired_pat_is_error_401_marks_connection_and_server_keeps_working()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workitems/1", "{}", HttpStatusCode.Unauthorized);
        var r = await h.Call("get_work_item", new { id = 1 });
        Assert.True(r.IsError);
        Assert.Contains("401", r.Text);
        Assert.NotNull(h.Conn("SHOP").LastAuthFailure);
        Assert.Equal("401 PAT abgelaufen", h.Records().Single().Result);

        h.Http.On("_apis/wit/workitems/2", WorkItem);
        var ok = await h.Call("get_work_item", new { id = 2 });
        Assert.False(ok.IsError);
        Assert.Null(h.Conn("SHOP").LastAuthFailure);
    }

    [Fact]
    public async Task Errors_never_contain_the_pat()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workitems/1", $$"""{"message":"echo {{Fake.Pat}}"}""", HttpStatusCode.InternalServerError);
        var r = await h.Call("get_work_item", new { id = 1 });
        Assert.True(r.IsError);
        Assert.DoesNotContain(Fake.Pat, r.Text);
    }

    [Fact]
    public async Task Missing_argument_is_a_tool_error()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("get_work_item", new { });
        Assert.True(r.IsError);
        Assert.Contains("„id“ fehlt", r.Text);
    }

    [Fact]
    public async Task Unknown_tool()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("drop_database");
        Assert.True(r.IsError);
    }

    [Fact]
    public async Task Wiql_over_limit_returns_first_items_with_note()
    {
        using var h = new Harness("SHOP");
        var ids = string.Join(",", Enumerable.Range(1, 250).Select(i => $$"""{"id":{{i}}}"""));
        h.Http.On("_apis/wit/wiql", $$"""{"workItems":[{{ids}}]}""");
        h.Http.OnRaw("_apis/wit/workitemsbatch", req =>
        {
            var body = JsonNode.Parse(req.Content!.ReadAsStringAsync().Result)!;
            var items = body["ids"]!.AsArray().Select(i => "{\"id\":" + i + ",\"fields\":{}}");
            return FakeHttp.Json($$"""{"value":[{{string.Join(",", items)}}]}""");
        });

        var r = await h.Call("execute_wiql", new { wiql = "SELECT [System.Id] FROM WorkItems" });
        Assert.False(r.IsError, r.Text);
        var json = JsonNode.Parse(r.Text)!;
        Assert.Equal(250, json["count"]!.GetValue<int>());
        Assert.Equal(WorkItemReadTools.DefaultLimit, json["returned"]!.GetValue<int>());
        Assert.Contains("50 weitere", json["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task Query_work_items_builds_escaped_wiql()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/wiql", """{"workItems":[]}""");
        await h.Call("query_work_items", new { type = "Bug", state = "Active", titleContains = "O'Brien", assignedTo = "@me" });
        var body = JsonNode.Parse(h.Http.Calls.Single().Body!)!["query"]!.GetValue<string>();
        Assert.Contains("[System.WorkItemType] = 'Bug'", body);
        Assert.Contains("O''Brien", body);
        Assert.Contains("@Me", body);
    }

    [Fact]
    public async Task Create_work_item_returns_id_and_url()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workitems/$Task", """{"id":4711}""", method: HttpMethod.Post);
        var r = await h.Call("create_work_item", new { type = "Task", title = "Neu", parentId = 1 });
        Assert.False(r.IsError, r.Text);
        var json = JsonNode.Parse(r.Text)!;
        Assert.Equal(4711, json["id"]!.GetValue<int>());
        Assert.EndsWith("/SHOP/_workitems/edit/4711", json["url"]!.GetValue<string>());

        var req = h.Http.Calls.Single();
        Assert.Equal("application/json-patch+json", req.ContentType);
        Assert.Contains("System.LinkTypes.Hierarchy-Reverse", req.Body);
        Assert.True(h.Records().Last().IsWrite);
        Assert.Equal(["wartet auf Freigabe", "OK"], h.Records().Select(x => x.Result));
    }

    [Fact]
    public async Task Update_with_invalid_field_names_the_field()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workitems/5", """{"message":"TF51535: Cannot find field Custom.Nope."}""", HttpStatusCode.BadRequest);
        var r = await h.Call("update_work_item", new { id = 5, fields = new Dictionary<string, object> { ["Custom.Nope"] = 1 } });
        Assert.True(r.IsError);
        Assert.Contains("Custom.Nope", r.Text);
    }

    [Fact]
    public async Task Update_without_changes_is_rejected()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("update_work_item", new { id = 5 });
        Assert.True(r.IsError);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task Attachment_over_60_mb_is_rejected_before_upload()
    {
        using var h = new Harness("SHOP");
        var file = Path.Combine(h.Dir.Path, "big.bin");
        using (var fs = File.Create(file))
        {
            fs.SetLength(61L * 1024 * 1024);
        }

        var r = await h.Call("add_work_item_attachment", new { id = 1, filePath = file });
        Assert.True(r.IsError);
        Assert.Contains("60 MB", r.Text);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task Attachment_upload_and_link()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/attachments", """{"id":"a","url":"https://azdo.example.local/att/a"}""");
        h.Http.On("_apis/wit/workitems/1", """{"id":1}""", method: HttpMethod.Patch);
        var r = await h.Call("add_work_item_attachment", new { id = 1, fileName = "a.txt", contentBase64 = Convert.ToBase64String("hallo"u8.ToArray()) });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("AttachedFile", h.Http.Requests.Last().Body);
    }

    [Fact]
    public async Task Comment_text_is_sent_but_not_logged()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workItems/7/comments", """{"id":99}""");
        var r = await h.Call("add_work_item_comment", new { id = 7, text = "Geheimer Kommentartext" });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("Geheimer Kommentartext", h.Http.Calls.Single().Body);

        var logText = File.ReadAllText(Directory.EnumerateFiles(h.Dir.AppPaths.LogsDirectory).Single());
        Assert.DoesNotContain("Geheimer Kommentartext", logText);
        Assert.All(h.Records(), x => Assert.Equal(7, x.WorkItemId));
    }

    [Fact]
    public async Task Relation_uses_friendly_name()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workitems/1", """{"id":1}""", method: HttpMethod.Patch);
        await h.Call("add_work_item_relation", new { id = 1, targetId = 2, relationType = "related" });
        Assert.Contains("System.LinkTypes.Related", h.Http.Calls.Single().Body);
        Assert.Contains("/_apis/wit/workItems/2", h.Http.Calls.Single().Body);
    }

    [Fact]
    public async Task Linked_items_filtered_by_type()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/wit/workitems/1?$expand=relations", """
            {"id":1,"relations":[
              {"rel":"System.LinkTypes.Hierarchy-Forward","url":"https://azdo.example.local/_apis/wit/workItems/2"},
              {"rel":"System.LinkTypes.Related","url":"https://azdo.example.local/_apis/wit/workItems/3"}]}
            """);
        h.Http.On("_apis/wit/workitemsbatch", """{"value":[{"id":2,"fields":{"System.Title":"Kind"}}]}""");
        var r = await h.Call("get_linked_work_items", new { id = 1, relationType = "child" });
        var json = JsonNode.Parse(r.Text)!;
        Assert.Equal(1, json["count"]!.GetValue<int>());
        Assert.Equal("child", json["linked"]![0]!["relation"]!.GetValue<string>());
        Assert.Equal("Kind", json["linked"]![0]!["fields"]!["System.Title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pull_requests_by_status()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/git/pullrequests", """{"value":[{"pullRequestId":1,"status":"active"}]}""");
        var r = await h.Call("get_pull_requests", new { status = "active" });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("searchCriteria.status=active", h.Http.Calls.Single().Url);
    }

    [Theory]
    [InlineData("get_repositories", "_apis/git/repositories", """{"value":[{"id":"r","name":"repo"}]}""")]
    [InlineData("get_test_runs", "_apis/test/runs", """{"value":[{"id":1,"name":"run"}]}""")]
    [InlineData("get_iterations", "_apis/work/teamsettings/iterations", """{"value":[{"id":"i","name":"Sprint 1"}]}""")]
    public async Task Read_tools_map_lists(string tool, string route, string json)
    {
        using var h = new Harness("SHOP");
        h.Http.On(route, json);
        var r = await h.Call(tool);
        Assert.False(r.IsError, r.Text);
        Assert.Equal(1, JsonNode.Parse(r.Text)!["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task Test_results_and_capacity_and_commits()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/test/Runs/5/results", """{"value":[{"id":1,"outcome":"Failed"}]}""");
        h.Http.On("/capacities", """{"teamMembers":[{"teamMember":{"displayName":"A"}}]}""");
        h.Http.On("/commits", """{"value":[{"commitId":"abc","author":{"name":"A"}}]}""");

        Assert.False((await h.Call("get_test_results", new { runId = 5, outcome = "Failed" })).IsError);
        var cap = await h.Call("get_iteration_capacity", new { iterationId = Guid.NewGuid().ToString() });
        Assert.Equal(1, JsonNode.Parse(cap.Text)!["count"]!.GetValue<int>());
        var commits = await h.Call("get_commits", new { repository = "repo", branch = "main" });
        Assert.Contains("itemVersion.version=main", h.Http.Requests.Last().Url);
        Assert.False(commits.IsError);
    }

    [Fact]
    public async Task Missing_endpoint_on_old_server_reports_not_available()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("get_test_runs"); // no route -> HTML 404
        Assert.True(r.IsError);
        Assert.Contains("Auf diesem Server nicht verfügbar", r.Text);
    }

    [Fact]
    public async Task List_projects_hides_pats_and_needs_no_http()
    {
        using var h = new Harness("default", "SHOP");
        var r = await h.Call("list_projects");
        Assert.False(r.IsError);
        Assert.Contains("SHOP", r.Text);
        Assert.Contains("azdo.example.local", r.Text);
        Assert.DoesNotContain(Fake.Pat, r.Text);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task Ping_reports_user()
    {
        using var h = new Harness("SHOP");
        h.Http.On("_apis/connectionData", """{"authenticatedUser":{"providerDisplayName":"Max Muster"}}""");
        var r = await h.Call("ping");
        Assert.Contains("Max Muster", r.Text);
    }

    [Theory]
    [InlineData("claude-code", "Claude Code")]
    [InlineData("claude-ai", "Claude Desktop")]
    [InlineData("opencode", "OpenCode")]
    [InlineData("Visual Studio Code", "VS Code / Copilot")]
    [InlineData(null, "unbekannt")]
    public void Client_names(string? raw, string expected) => Assert.Equal(expected, ClientNames.Display(raw));
}
