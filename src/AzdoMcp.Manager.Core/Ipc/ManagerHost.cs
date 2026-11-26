using System.Collections.Concurrent;
using System.IO.Pipes;
using AzdoMcp.Core.Ipc;

namespace AzdoMcp.Manager.Core.Ipc;

public interface IPipeServerTransport
{
    /// <summary>Waits for the next server instance to connect.</summary>
    Task<Stream> AcceptAsync(CancellationToken ct);
}

/// <summary>
/// Named pipe for the current user only (<see cref="PipeOptions.CurrentUserOnly"/> makes .NET set an
/// ACL that admits only the creating user).
/// </summary>
public sealed class NamedPipeServerTransport(string pipeName) : IPipeServerTransport
{
    /// <summary>Without buffers a write blocks until the peer reads, which deadlocks the handshake.</summary>
    private const int BufferSize = 64 * 1024;

    public string PipeName { get; } = pipeName;

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, BufferSize, BufferSize);
        try
        {
            await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>A running azdo-mcp.exe as seen by the Manager ("aktive Clients").</summary>
public sealed record ServerInstance(string SessionId, string ClientName, int ProcessId, DateTimeOffset StartedAt, DateTimeOffset ConnectedAt, DateTimeOffset? LastCall, string? Version);

/// <summary>
/// Manager side of the pipe: accepts server instances, keeps the active-client list and the queue
/// of pending approvals, and broadcasts pause / resume / reload.
/// </summary>
public sealed class ManagerHost : IAsyncDisposable
{
    private readonly IPipeServerTransport _transport;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, (ApprovalRequest Request, Session Session)> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _accept;
    private volatile bool _paused;

    public ManagerHost(IPipeServerTransport transport, TimeProvider? time = null)
    {
        _transport = transport;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Instances, clients, pause state or the approval queue changed. Raised on a worker thread.</summary>
    public event EventHandler? Changed;

    /// <summary>A new approval request arrived.</summary>
    public event EventHandler<ApprovalRequest>? ApprovalRequested;

    /// <summary>A request left the queue (answered, timed out or its server ended).</summary>
    public event EventHandler<string>? ApprovalClosed;

    /// <summary>A second Manager start asked this instance to show its window.</summary>
    public event EventHandler? ShowRequested;

    public bool IsPaused => _paused;

    public IReadOnlyList<ServerInstance> Instances =>
        _sessions.Values.Where(s => s.Registered).Select(s => s.ToInstance()).OrderBy(i => i.ConnectedAt).ToList();

    public IReadOnlyList<ApprovalRequest> PendingApprovals =>
        _pending.Values.Select(p => p.Request).OrderBy(r => r.CreatedAt).ToList();

    public void Start() => _accept ??= Task.Run(() => AcceptLoopAsync(_stop.Token));

    /// <summary>Answers a pending request. Returns false when it is no longer pending.</summary>
    public async Task<bool> RespondAsync(string requestId, bool approved)
    {
        if (!_pending.TryRemove(requestId, out var entry))
        {
            return false;
        }

        try
        {
            await entry.Session.Writer.SendAsync(new IpcMessage { Type = MessageTypes.ApproveResponse, RequestId = requestId, Approved = approved }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }

        ApprovalClosed?.Invoke(this, requestId);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>"Stoppen": every server rejects calls until <see cref="ResumeAsync"/>.</summary>
    public Task PauseAsync()
    {
        _paused = true;
        return BroadcastAsync(new IpcMessage { Type = MessageTypes.Pause });
    }

    public Task ResumeAsync()
    {
        _paused = false;
        return BroadcastAsync(new IpcMessage { Type = MessageTypes.Resume });
    }

    /// <summary>
    /// Asks every server to exit and waits until they are gone or the timeout passes.
    /// Returns the number still connected.
    /// </summary>
    public async Task<int> RequestServersExitAsync(TimeSpan timeout)
    {
        await BroadcastAsync(new IpcMessage { Type = MessageTypes.Exit }).ConfigureAwait(false);
        var until = _time.GetUtcNow() + timeout;
        while (Instances.Count > 0 && _time.GetUtcNow() < until)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        return Instances.Count;
    }

    /// <summary>"Neu starten": servers re-read configuration and refresh their tool list.</summary>
    public Task ReloadAsync() => BroadcastAsync(new IpcMessage { Type = MessageTypes.Reload });

    private async Task BroadcastAsync(IpcMessage message)
    {
        foreach (var s in _sessions.Values)
        {
            try
            {
                await s.Writer.SendAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await _transport.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => SessionAsync(stream, ct), ct);
        }
    }

    private async Task SessionAsync(Stream stream, CancellationToken ct)
    {
        var session = new Session(Guid.NewGuid().ToString("N"), new IpcWriter(stream), _time.GetUtcNow());
        _sessions[session.Id] = session;
        try
        {
            // Send without waiting: the peer may be writing its registration at the same moment.
            _ = SendQuietAsync(session, new IpcMessage { Type = MessageTypes.State, Paused = _paused });
            while (!ct.IsCancellationRequested)
            {
                var msg = await IpcFraming.ReadAsync(stream, ct).ConfigureAwait(false);
                if (msg is null)
                {
                    break;
                }

                Handle(session, msg);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            foreach (var (id, entry) in _pending.Where(p => p.Value.Session == session).ToList())
            {
                if (_pending.TryRemove(id, out _))
                {
                    ApprovalClosed?.Invoke(this, id);
                }
            }

            await stream.DisposeAsync().ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Handle(Session session, IpcMessage msg)
    {
        switch (msg.Type)
        {
            case MessageTypes.Register:
                session.Registered = true;
                session.ClientName = msg.ClientName ?? "unbekannt";
                session.ProcessId = msg.ProcessId ?? 0;
                session.StartedAt = msg.StartedAt ?? session.ConnectedAt;
                session.LastCall = msg.LastCall ?? session.LastCall;
                session.Version = msg.Version;
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case MessageTypes.Heartbeat:
                session.LastCall = msg.LastCall ?? _time.GetUtcNow();
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case MessageTypes.ApproveRequest when msg.Request is not null:
                _pending[msg.Request.RequestId] = (msg.Request, session);
                ApprovalRequested?.Invoke(this, msg.Request);
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case MessageTypes.ApproveCancel when msg.RequestId is not null:
                if (_pending.TryRemove(msg.RequestId, out _))
                {
                    ApprovalClosed?.Invoke(this, msg.RequestId);
                    Changed?.Invoke(this, EventArgs.Empty);
                }

                break;
            case MessageTypes.Show:
                ShowRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private static async Task SendQuietAsync(Session session, IpcMessage message)
    {
        try
        {
            await session.Writer.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (_accept is not null)
        {
            try
            {
                await _accept.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private sealed class Session(string id, IpcWriter writer, DateTimeOffset connectedAt)
    {
        public string Id { get; } = id;

        public IpcWriter Writer { get; } = writer;

        public DateTimeOffset ConnectedAt { get; } = connectedAt;

        public bool Registered { get; set; }

        public string ClientName { get; set; } = "unbekannt";

        public int ProcessId { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset? LastCall { get; set; }

        public string? Version { get; set; }

        public ServerInstance ToInstance() => new(Id, ClientName, ProcessId, StartedAt, ConnectedAt, LastCall, Version);
    }
}
