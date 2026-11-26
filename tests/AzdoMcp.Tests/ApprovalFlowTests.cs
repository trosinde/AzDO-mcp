using System.Net;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Core.Tools;
using AzdoMcp.Manager.Core.Ipc;

namespace AzdoMcp.Tests;

/// <summary>Executor + real ManagerChannel + ManagerHost over the in-memory pipe.</summary>
public sealed class ApprovalFlowTests : IAsyncDisposable
{
    private readonly InMemoryPipeHub _hub = new();
    private readonly ManagerHost _host;
    private readonly ManagerChannel _channel;
    private readonly Harness _h = new("SHOP");
    private readonly ToolExecutor _executor;

    public ApprovalFlowTests()
    {
        _host = new ManagerHost(_hub);
        _host.Start();
        _channel = new ManagerChannel(_hub, approvalTimeout: TimeSpan.FromMilliseconds(400), retryInterval: TimeSpan.FromMilliseconds(100));
        _channel.Start();
        _executor = new ToolExecutor(_h.Store, new AzdoClientFactory(_h.Http, TimeSpan.FromSeconds(5)), _h.Audit, _h.Permissions, _channel);

        _h.Http.On("_apis/wit/workitems/48211?fields", """{"id":48211,"fields":{"System.State":"Active","Microsoft.VSTS.Common.Priority":2}}""", method: HttpMethod.Get);
        _h.Http.On("_apis/wit/workitems/48211?api-version", """{"id":48211,"rev":5}""", method: HttpMethod.Patch);
    }

    private Task<ToolCallResult> Update() => _executor.ExecuteAsync("update_work_item", Args(new
    {
        id = 48211,
        fields = new Dictionary<string, object> { ["System.State"] = "Resolved", ["Microsoft.VSTS.Common.Priority"] = 1 },
    }), "Claude Code", TestContext.Current.CancellationToken);

    private static Dictionary<string, System.Text.Json.JsonElement> Args(object o) =>
        System.Text.Json.JsonSerializer.SerializeToElement(o).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    private async Task<ApprovalRequest> NextRequest()
    {
        await Eventually.True(() => _host.PendingApprovals.Count == 1);
        return _host.PendingApprovals[0];
    }

    [Fact]
    public async Task Approved_call_is_executed_with_summary_and_pending_record()
    {
        var call = Update();
        var req = await NextRequest();
        Assert.Equal("Claude Code möchte #48211 in SHOP ändern", req.Title);
        Assert.Equal("update_work_item: Status Active → Resolved, Priorität 2 → 1", req.Summary);
        Assert.Contains(_h.Records(), r => r.Result == "wartet auf Freigabe" && r.RequestId == req.RequestId);
        Assert.DoesNotContain(_h.Http.Requests, r => r.Method == HttpMethod.Patch);

        Assert.True(await _host.RespondAsync(req.RequestId, approved: true));
        var result = await call;
        Assert.False(result.IsError, result.Text);
        Assert.Contains(_h.Http.Requests, r => r.Method == HttpMethod.Patch);
        var final = _h.Records().Last();
        Assert.Equal(AuditOutcome.Ok, final.Outcome);
        Assert.Equal(req.RequestId, final.RequestId);
        Assert.Empty(_host.PendingApprovals);
    }

    [Fact]
    public async Task Denied_call_is_not_executed()
    {
        var call = Update();
        var req = await NextRequest();
        await _host.RespondAsync(req.RequestId, approved: false);
        var result = await call;
        Assert.True(result.IsError);
        Assert.Contains("Vom Benutzer abgelehnt", result.Text);
        Assert.DoesNotContain(_h.Http.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal("abgelehnt", _h.Records().Last().Result);
    }

    [Fact]
    public async Task No_answer_is_denied_after_timeout_and_withdrawn()
    {
        var closed = new List<string>();
        _host.ApprovalClosed += (_, id) => closed.Add(id);
        var call = Update();
        var req = await NextRequest();
        var result = await call;
        Assert.True(result.IsError);
        Assert.Contains("Keine Antwort – abgelehnt", result.Text);
        await Eventually.True(() => _host.PendingApprovals.Count == 0);
        Assert.Contains(req.RequestId, closed);
        Assert.Equal(req.ExpiresAt - req.CreatedAt, TimeSpan.FromMilliseconds(400));
        Assert.DoesNotContain(_h.Http.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Default_timeout_is_two_minutes()
    {
        await using var c = new ManagerChannel(_hub);
        Assert.Equal(TimeSpan.FromMinutes(2), c.ApprovalTimeout);
    }

    [Fact]
    public async Task Manager_not_running_denies_immediately()
    {
        var hub = new InMemoryPipeHub { Available = false };
        await using var channel = new ManagerChannel(hub, retryInterval: TimeSpan.FromMilliseconds(100));
        channel.Start();
        var executor = new ToolExecutor(_h.Store, new AzdoClientFactory(_h.Http), _h.Audit, _h.Permissions, channel);

        var started = DateTime.UtcNow;
        var r = await executor.ExecuteAsync("create_work_item", Args(new { type = "Task", title = "x" }), "Claude Code", TestContext.Current.CancellationToken);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.True(r.IsError);
        Assert.Contains("AzDO MCP Manager nicht gestartet – Freigabe nicht möglich", r.Text);
        Assert.DoesNotContain(_h.Http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Allowed_calls_work_without_manager()
    {
        var executor = new ToolExecutor(_h.Store, new AzdoClientFactory(_h.Http), _h.Audit, _h.Permissions, new NoManagerChannel());
        _h.Http.On("_apis/wit/workitems/1", """{"id":1,"fields":{}}""");
        var r = await executor.ExecuteAsync("get_work_item", Args(new { id = 1 }), "Claude Code", TestContext.Current.CancellationToken);
        Assert.False(r.IsError, r.Text);
    }

    [Fact]
    public async Task Manager_ending_mid_request_denies()
    {
        var call = Update();
        await NextRequest();
        await _host.DisposeAsync();
        var result = await call;
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Pause_rejects_calls_until_resume()
    {
        await Eventually.True(() => _channel.IsConnected);
        await _host.PauseAsync();
        await Eventually.True(() => _channel.IsPaused);

        _h.Http.On("_apis/wit/workitems/1", """{"id":1,"fields":{}}""");
        var paused = await _executor.ExecuteAsync("get_work_item", Args(new { id = 1 }), "Claude Code", TestContext.Current.CancellationToken);
        Assert.True(paused.IsError);
        Assert.Contains("Vom Benutzer pausiert", paused.Text);
        Assert.DoesNotContain(_h.Http.Requests, r => r.Url.Contains("/workitems/1?"));
        Assert.Equal("pausiert", _h.Records().Last().Result);

        await _host.ResumeAsync();
        await Eventually.True(() => !_channel.IsPaused);
        Assert.False((await _executor.ExecuteAsync("get_work_item", Args(new { id = 1 }), "Claude Code", TestContext.Current.CancellationToken)).IsError);
    }

    [Fact]
    public async Task New_server_starts_paused_when_manager_is_paused()
    {
        await Eventually.True(() => _channel.IsConnected);
        await _host.PauseAsync();
        await using var late = new ManagerChannel(_hub, retryInterval: TimeSpan.FromMilliseconds(100));
        late.Start();
        await Eventually.True(() => late.IsPaused);
    }

    [Fact]
    public async Task Reload_reaches_servers()
    {
        var reloaded = 0;
        _channel.ReloadRequested += () => Interlocked.Increment(ref reloaded);
        await Eventually.True(() => _channel.IsConnected);
        await _host.ReloadAsync();
        await Eventually.True(() => Volatile.Read(ref reloaded) == 1);
    }

    [Fact]
    public async Task Create_title_and_comment_summary()
    {
        _h.Manager.Outcome = ApprovalOutcome.Denied;
        await _h.Call("create_work_item", new { type = "Bug", title = "Absturz beim Start" });
        await _h.Call("add_work_item_comment", new { id = 7, text = "Bitte prüfen" });
        Assert.Equal("Claude Code möchte ein Bug in SHOP anlegen", _h.Manager.Requests[0].Title);
        Assert.Contains("Absturz beim Start", _h.Manager.Requests[0].Summary);
        Assert.Equal("Claude Code möchte #7 in SHOP ändern", _h.Manager.Requests[1].Title);
        Assert.Contains("Bitte prüfen", _h.Manager.Requests[1].Summary);
        Assert.DoesNotContain("Bitte prüfen", File.ReadAllText(Directory.EnumerateFiles(_h.Dir.AppPaths.LogsDirectory).Single()));
    }

    [Fact]
    public async Task Summary_without_old_values_when_read_fails()
    {
        _h.Http.On("_apis/wit/workitems/9?fields", "{}", HttpStatusCode.Forbidden, HttpMethod.Get);
        _h.Manager.Outcome = ApprovalOutcome.Denied;
        await _h.Call("update_work_item", new { id = 9, state = "Closed" });
        Assert.Equal("update_work_item: Status → Closed", _h.Manager.Requests.Single().Summary);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _host.DisposeAsync();
        _h.Dispose();
    }
}
