using System.Buffers.Binary;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Manager.Core.Ipc;

namespace AzdoMcp.Tests;

public class PipeFramingTests
{
    [Fact]
    public async Task Round_trip()
    {
        var ms = new MemoryStream();
        var request = new ApprovalRequest { Client = "Claude Code", Tool = "update_work_item", Title = "Claude Code möchte #1 ändern" };
        await new IpcWriter(ms).SendAsync(new IpcMessage { Type = MessageTypes.ApproveRequest, RequestId = request.RequestId, Request = request }, TestContext.Current.CancellationToken);
        await new IpcWriter(ms).SendAsync(new IpcMessage { Type = MessageTypes.Pause }, TestContext.Current.CancellationToken);
        ms.Position = 0;

        var first = await IpcFraming.ReadAsync(ms, TestContext.Current.CancellationToken);
        Assert.Equal(MessageTypes.ApproveRequest, first!.Type);
        Assert.Equal("Claude Code möchte #1 ändern", first.Request!.Title);
        Assert.Equal(MessageTypes.Pause, (await IpcFraming.ReadAsync(ms, TestContext.Current.CancellationToken))!.Type);
        Assert.Null(await IpcFraming.ReadAsync(ms, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Oversized_length_is_rejected()
    {
        var frame = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(frame, IpcFraming.MaxMessageBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => IpcFraming.ReadAsync(new MemoryStream(frame), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Truncated_message_is_an_error()
    {
        var frame = IpcFraming.Encode(new IpcMessage { Type = MessageTypes.Reload });
        await Assert.ThrowsAsync<EndOfStreamException>(() => IpcFraming.ReadAsync(new MemoryStream(frame[..^2]), TestContext.Current.CancellationToken));
    }
}

public sealed class InstanceRegistryTests : IAsyncDisposable
{
    private readonly InMemoryPipeHub _hub = new();
    private readonly ManagerHost _host;
    private readonly List<ManagerChannel> _channels = [];

    public InstanceRegistryTests()
    {
        _host = new ManagerHost(_hub);
    }

    private ManagerChannel Channel(string client)
    {
        var c = new ManagerChannel(_hub, retryInterval: TimeSpan.FromMilliseconds(100));
        c.SetClientName(client);
        c.Start();
        _channels.Add(c);
        return c;
    }

    [Fact]
    public async Task Two_instances_are_listed_with_client_names()
    {
        _host.Start();
        Channel("Claude Code");
        Channel("OpenCode");
        await Eventually.True(() => _host.Instances.Count == 2);
        Assert.Equal(["Claude Code", "OpenCode"], _host.Instances.Select(i => i.ClientName).Order());
        Assert.All(_host.Instances, i => Assert.Equal(Environment.ProcessId, i.ProcessId));
    }

    [Fact]
    public async Task Heartbeat_updates_last_call()
    {
        _host.Start();
        var c = Channel("Claude Code");
        await Eventually.True(() => _host.Instances.Count == 1);
        Assert.Null(_host.Instances[0].LastCall);
        c.NotifyCall();
        await Eventually.True(() => _host.Instances[0].LastCall is not null);
    }

    [Fact]
    public async Task Manager_started_after_client_sees_it_within_10_seconds()
    {
        _hub.Available = false;
        var c = Channel("Claude Code");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(c.IsConnected);

        _hub.Available = true;
        _host.Start();
        await Eventually.True(() => _host.Instances.Count == 1, 10_000);
    }

    [Fact]
    public async Task Ended_instance_is_removed()
    {
        _host.Start();
        var c = Channel("Claude Code");
        await Eventually.True(() => _host.Instances.Count == 1);
        _channels.Remove(c);
        await c.DisposeAsync();
        await Eventually.True(() => _host.Instances.Count == 0);
    }

    [Fact]
    public async Task Named_pipe_round_trip_for_current_user()
    {
        var name = "AzdoMcp-test-" + Guid.NewGuid().ToString("N");
        await using var host = new ManagerHost(new NamedPipeServerTransport(name));
        host.Start();
        await using var c = new ManagerChannel(new NamedPipeClientTransport(name), retryInterval: TimeSpan.FromMilliseconds(100));
        c.SetClientName("Claude Code");
        c.Start();
        await Eventually.True(() => host.Instances.Count == 1);
        Assert.Equal("Claude Code", host.Instances[0].ClientName);
    }

    [Fact]
    public void Pipe_name_contains_user_identity()
    {
        var name = PipeNames.ForCurrentUser();
        Assert.StartsWith("AzdoMcp-", name);
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("AZDOMCP_PIPE") is null)
        {
            Assert.StartsWith("AzdoMcp-S-1-", name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _channels)
        {
            await c.DisposeAsync();
        }

        await _host.DisposeAsync();
    }
}
