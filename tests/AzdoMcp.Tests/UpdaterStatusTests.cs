using AzdoMcp.Core.Ipc;
using AzdoMcp.Manager.Core.Ipc;
using AzdoMcp.Manager.Core.Services;
using AzdoMcp.Manager.Core.ViewModels;

namespace AzdoMcp.Tests;

internal sealed class FakeUpdateBackend : IUpdateBackend
{
    public bool IsInstalled { get; set; } = true;

    public string? Available { get; set; }

    public Exception? Fail { get; set; }

    public int Applied { get; private set; }

    public Task<string?> CheckAsync(CancellationToken ct) =>
        Fail is not null ? Task.FromException<string?>(Fail) : Task.FromResult(Available);

    public Task ApplyAsync(Action<int>? progress, CancellationToken ct)
    {
        Applied++;
        return Task.CompletedTask;
    }
}

public class UpdaterStatusTests
{
    [Fact]
    public void Portable_never_offers_updates()
    {
        var s = new UpdateService(new FakeUpdateBackend { Available = "2.0.1" }, isInstalled: false);
        Assert.Equal("Updates: aus", s.StatusText);
        Assert.False(s.CanApply);
    }

    [Fact]
    public void Installed_without_source()
    {
        Assert.Equal("Updates: nicht konfiguriert", new UpdateService(null, isInstalled: true).StatusText);
    }

    [Fact]
    public async Task Up_to_date_and_available()
    {
        var backend = new FakeUpdateBackend();
        var s = new UpdateService(backend, isInstalled: true);
        await s.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Updates: aktuell", s.StatusText);

        backend.Available = "2.0.1";
        await s.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Update verfügbar (2.0.1)", s.StatusText);
        Assert.True(s.CanApply);
    }

    [Fact]
    public async Task Apply_only_after_check_and_asks_servers_to_exit_first()
    {
        var backend = new FakeUpdateBackend { Available = "2.0.1" };
        var order = new List<string>();
        var s = new UpdateService(backend, true, () =>
        {
            order.Add("exit-servers");
            return Task.CompletedTask;
        });

        await s.ApplyAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, backend.Applied);

        await s.CheckAsync(TestContext.Current.CancellationToken);
        await s.ApplyAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.Applied);
        Assert.Equal(["exit-servers"], order);
    }

    [Fact]
    public async Task Servers_still_running_abort_the_update()
    {
        var backend = new FakeUpdateBackend { Available = "2.0.1" };
        var s = new UpdateService(backend, true, () => throw new InvalidOperationException("1 MCP-Server laufen noch"));
        await s.CheckAsync(TestContext.Current.CancellationToken);
        await s.ApplyAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Failed, s.State);
        Assert.Equal(0, backend.Applied);
        Assert.Contains("laufen noch", s.Error);
    }

    [Fact]
    public async Task Check_error_is_shown()
    {
        var s = new UpdateService(new FakeUpdateBackend { Fail = new HttpRequestException("offline") }, true);
        await s.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Updates: Fehler", s.StatusText);
    }

    [Fact]
    public async Task Footer_follows_update_service_and_update_needs_confirmation()
    {
        var backend = new FakeUpdateBackend { Available = "2.0.1" };
        await using var m = new ManagerHarness();
        var ctx = new ManagerContext
        {
            Paths = m.Ctx.Paths, Connections = m.Ctx.Connections, Permissions = m.Ctx.Permissions, Audit = m.Ctx.Audit,
            Host = m.Host, Tester = m.Ctx.Tester, Discovery = m.Ctx.Discovery, Clients = m.Ctx.Clients, Autostart = m.Ctx.Autostart, Reminders = m.Ctx.Reminders,
            Environment = m.Env, Dialogs = m.Dialogs, LegacyCandidates = m.Ctx.LegacyCandidates,
            Updates = new UpdateService(backend, true),
        };
        var main = new MainViewModel(ctx);
        Assert.Equal("v2.0.0 · Updates: aktuell", main.FooterVersion);

        await ctx.Updates.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v2.0.0 · Update verfügbar (2.0.1)", main.FooterVersion);
        Assert.True(main.CanUpdate);

        m.Dialogs.ConfirmAnswer = false;
        await main.UpdateCommand.ExecuteAsync();
        Assert.Equal(0, backend.Applied);

        m.Dialogs.ConfirmAnswer = true;
        await main.UpdateCommand.ExecuteAsync();
        Assert.Equal(1, backend.Applied);
    }

    [Fact]
    public async Task Exit_request_reaches_servers_and_host_waits()
    {
        var hub = new InMemoryPipeHub();
        await using var host = new ManagerHost(hub);
        host.Start();
        await using var server = new ManagerChannel(hub, retryInterval: TimeSpan.FromMilliseconds(50));
        var exited = false;
        server.ExitRequested += () =>
        {
            exited = true;
            _ = server.DisposeAsync().AsTask(); // a real server ends its process here
        };
        server.Start();
        await Eventually.True(() => host.Instances.Count == 1);

        var left = await host.RequestServersExitAsync(TimeSpan.FromSeconds(5));
        Assert.True(exited);
        Assert.Equal(0, left);
    }
}
