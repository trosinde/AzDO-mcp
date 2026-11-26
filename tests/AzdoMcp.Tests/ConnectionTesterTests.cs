using System.Net;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;

namespace AzdoMcp.Tests;

public class ConnectionTesterTests
{
    private static FakeHttp AllOk() => new FakeHttp()
        .On("_apis/connectionData", """{"authenticatedUser":{"providerDisplayName":"Max Muster"}}""")
        .On("_apis/projects", """{"value":[{"id":"p1","name":"SHOP"},{"id":"p2","name":"OPS"}]}""")
        .On("_apis/wit/wiql", """{"workItems":[]}""")
        .On("_apis/wit/workitems/$Task?validateOnly=true", """{"id":-1}""")
        .On("_apis/git/repositories", """{"value":[]}""")
        .On("_apis/test/runs", """{"value":[]}""")
        .On("_apis/build/builds", """{"value":[]}""")
        .On("_apis/work/teamsettings/iterations", """{"value":[]}""");

    private static Connection Conn() => new() { Name = "tfs", CollectionUrl = Fake.Collection };

    private static Task<ConnectionTestResult> Run(FakeHttp http, Connection? c = null) =>
        new ConnectionTester(new AzdoClientFactory(http)).TestAsync(c ?? Conn(), Fake.Pat, TestContext.Current.CancellationToken);

    [Fact]
    public async Task All_scopes_present_without_selected_project()
    {
        var r = await Run(AllOk());
        Assert.True(r.Succeeded);
        Assert.Equal(["OPS", "SHOP"], r.Projects);
        Assert.Contains(r.Areas, a => a.Text == "Angemeldet als Max Muster");
        Assert.Contains(r.Areas, a => a.Text.StartsWith("Server erreichbar · API"));
        Assert.Equal(Acc.W, r.PatScope![AccessArea.WorkItems]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Code]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Test]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Iterations]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Pipelines]);
    }

    [Fact]
    public async Task Missing_build_scope()
    {
        var r = await Run(AllOk().On("_apis/build/builds", """{"message":"Access denied"}""", HttpStatusCode.Forbidden));
        Assert.Equal(Check.No, r.PatScope![AccessArea.Pipelines].Read);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Test]);
    }

    [Fact]
    public async Task Missing_test_scope_is_none()
    {
        var r = await Run(AllOk().On("_apis/test/runs", """{"message":"Access denied"}""", HttpStatusCode.Forbidden));
        Assert.Equal(Check.No, r.PatScope![AccessArea.Test].Read);
    }

    [Fact]
    public async Task Read_only_pat_when_validate_only_is_forbidden()
    {
        var r = await Run(AllOk().On("validateOnly=true", "{}", HttpStatusCode.Forbidden));
        Assert.Equal(Acc.Ro, r.PatScope![AccessArea.WorkItems]);
    }

    [Fact]
    public async Task Unreachable_server()
    {
        var r = await Run(new FakeHttp { Throw = _ => new HttpRequestException("No such host is known.") });
        Assert.False(r.Reachable);
        Assert.Equal("Server nicht erreichbar", r.Areas.Single().Text);
        Assert.DoesNotContain(r.Areas, a => (a.Hint ?? "").Contains(Fake.Pat));
    }

    [Fact]
    public async Task Expired_pat_reports_401()
    {
        var r = await Run(new FakeHttp().On("_apis/connectionData", "{}", HttpStatusCode.Unauthorized));
        Assert.False(r.Authenticated);
        Assert.Contains(r.Areas, a => a.Code == "401");
    }

    [Fact]
    public async Task Apply_stores_pat_scope_and_blocks_test_tools_for_all_projects()
    {
        using var h = new Harness();
        h.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects = [new ProjectEntry { Name = "SHOP", Alias = "SHOP", AllowWrite = true }, new ProjectEntry { Name = "OPS", Alias = "OPS" }],
        }, Fake.Pat);
        var c = h.Conn("SHOP");
        var r = await Run(AllOk().On("_apis/test/runs", "{}", HttpStatusCode.Forbidden), c);
        ConnectionTester.Apply(h.Store, c, r);

        Assert.NotNull(h.Conn("SHOP").LastTested);
        Assert.Equal(Check.No, h.Proj("SHOP").Effective[AccessArea.Test].Read);
        Assert.Equal(Check.No, h.Proj("OPS").Effective[AccessArea.Test].Read);
        var call = await h.Call("get_test_runs", new { project = "OPS" });
        Assert.True(call.IsError);
        Assert.Contains("403 Scope fehlt", call.Text);
    }

    [Fact]
    public async Task Only_write_request_is_validate_only()
    {
        var http = AllOk();
        await Run(http);
        Assert.All(http.Requests.Where(r => r.Url.Contains("/_apis/wit/workitems/")), w => Assert.Contains("validateOnly=true", w.Url));
        Assert.DoesNotContain(http.Requests, r => r.Method == HttpMethod.Patch || r.Method == HttpMethod.Delete || r.Method == HttpMethod.Put);
    }
}
