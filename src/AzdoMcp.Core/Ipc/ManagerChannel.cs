using System.Collections.Concurrent;

namespace AzdoMcp.Core.Ipc;

/// <summary>What the tool executor needs from the Manager.</summary>
public interface IManagerChannel
{
    bool IsPaused { get; }

    void SetClientName(string clientName);

    void NotifyCall();

    Task<ApprovalOutcome> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct);

    TimeSpan ApprovalTimeout { get; }
}

/// <summary>Used when no Manager integration is wanted: every approval is unavailable.</summary>
public sealed class NoManagerChannel : IManagerChannel
{
    public bool IsPaused => false;

    public TimeSpan ApprovalTimeout => ManagerChannel.DefaultApprovalTimeout;

    public void SetClientName(string clientName)
    {
    }

    public void NotifyCall()
    {
    }

    public Task<ApprovalOutcome> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct) =>
        Task.FromResult(ApprovalOutcome.ManagerUnavailable);
}

/// <summary>
/// Server side of the pipe: connects lazily to the Manager, re-registers when it starts later,
/// forwards approval requests and receives pause/resume/reload.
/// </summary>
public sealed class ManagerChannel : IManagerChannel, IAsyncDisposable
{
    public static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(5);

    private readonly IPipeClientTransport _transport;
    private readonly TimeSpan _retry;
    private readonly TimeProvider _time;
    private readonly string _version;
    private readonly DateTimeOffset _startedAt;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private TaskCompletionSource<IpcWriter> _connected = NewConnectedSource();
    private IpcWriter? _writer;
    private string _clientName = "unbekannt";
    private DateTimeOffset? _lastCall;
    private Task? _loop;
    private volatile bool _paused;

    public ManagerChannel(IPipeClientTransport transport, TimeSpan? approvalTimeout = null, TimeSpan? retryInterval = null, TimeProvider? time = null, string version = "")
    {
        _transport = transport;
        ApprovalTimeout = approvalTimeout ?? DefaultApprovalTimeout;
        _retry = retryInterval ?? DefaultRetryInterval;
        _time = time ?? TimeProvider.System;
        _version = version;
        _startedAt = _time.GetUtcNow();
    }

    public TimeSpan ApprovalTimeout { get; }

    public bool IsPaused => _paused;

    public bool IsConnected => _writer is not null;

    /// <summary>Raised when the Manager asks to reload configuration ("Neu starten").</summary>
    public event Action? ReloadRequested;

    /// <summary>Raised when the Manager asks this server to exit (before an update).</summary>
    public event Action? ExitRequested;

    /// <summary>Raised when pause state or connection changes.</summary>
    public event Action? StateChanged;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    public void SetClientName(string clientName)
    {
        if (_clientName == clientName)
        {
            return;
        }

        _clientName = clientName;
        _ = SendSafeAsync(RegisterMessage());
    }

    public void NotifyCall()
    {
        _lastCall = _time.GetUtcNow();
        _ = SendSafeAsync(new IpcMessage { Type = MessageTypes.Heartbeat, ProcessId = Environment.ProcessId, LastCall = _lastCall });
    }

    public async Task<ApprovalOutcome> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        var writer = await WaitForConnectionAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        if (writer is null)
        {
            return ApprovalOutcome.ManagerUnavailable;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.RequestId] = tcs;
        try
        {
            await writer.SendAsync(new IpcMessage { Type = MessageTypes.ApproveRequest, RequestId = request.RequestId, Request = request }, ct).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ApprovalTimeout);
            try
            {
                return await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false) ? ApprovalOutcome.Approved : ApprovalOutcome.Denied;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await SendSafeAsync(new IpcMessage { Type = MessageTypes.ApproveCancel, RequestId = request.RequestId }).ConfigureAwait(false);
                return ApprovalOutcome.TimedOut;
            }
            catch (IOException)
            {
                return ApprovalOutcome.ManagerUnavailable;
            }
        }
        catch (IOException)
        {
            return ApprovalOutcome.ManagerUnavailable;
        }
        finally
        {
            _pending.TryRemove(request.RequestId, out _);
        }
    }

    private async Task<IpcWriter?> WaitForConnectionAsync(TimeSpan wait, CancellationToken ct)
    {
        if (_writer is { } w)
        {
            return w;
        }

        Task<IpcWriter> connected;
        lock (_gate)
        {
            connected = _connected.Task;
        }

        _wake.Release();
        try
        {
            return await connected.WaitAsync(wait, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream? stream = null;
            try
            {
                stream = await _transport.ConnectAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stream is null)
            {
                try
                {
                    await _wake.WaitAsync(_retry, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            await using (stream.ConfigureAwait(false))
            {
                await SessionAsync(stream, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task SessionAsync(Stream stream, CancellationToken ct)
    {
        var writer = new IpcWriter(stream);
        try
        {
            _writer = writer;
            var register = writer.SendAsync(RegisterMessage(), ct);
            lock (_gate)
            {
                _connected.TrySetResult(writer);
            }

            StateChanged?.Invoke();
            _ = register.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            while (!ct.IsCancellationRequested)
            {
                var msg = await IpcFraming.ReadAsync(stream, ct).ConfigureAwait(false);
                if (msg is null)
                {
                    break;
                }

                Handle(msg);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _writer = null;
            lock (_gate)
            {
                if (_connected.Task.IsCompleted)
                {
                    _connected = NewConnectedSource();
                }
            }

            // Manager gone: pending approvals cannot be answered any more.
            foreach (var tcs in _pending.Values)
            {
                tcs.TrySetException(new IOException("Manager beendet"));
            }

            if (_paused)
            {
                _paused = false;
            }

            StateChanged?.Invoke();
        }
    }

    private void Handle(IpcMessage msg)
    {
        switch (msg.Type)
        {
            case MessageTypes.ApproveResponse when msg.RequestId is not null:
                if (_pending.TryGetValue(msg.RequestId, out var tcs))
                {
                    tcs.TrySetResult(msg.Approved == true);
                }

                break;
            case MessageTypes.Pause:
                _paused = true;
                StateChanged?.Invoke();
                break;
            case MessageTypes.Resume:
                _paused = false;
                StateChanged?.Invoke();
                break;
            case MessageTypes.State:
                _paused = msg.Paused == true;
                StateChanged?.Invoke();
                break;
            case MessageTypes.Reload:
                ReloadRequested?.Invoke();
                break;
            case MessageTypes.Exit:
                ExitRequested?.Invoke();
                break;
        }
    }

    private IpcMessage RegisterMessage() => new()
    {
        Type = MessageTypes.Register,
        ClientName = _clientName,
        ProcessId = Environment.ProcessId,
        StartedAt = _startedAt,
        LastCall = _lastCall,
        Version = _version,
    };

    private async Task SendSafeAsync(IpcMessage message)
    {
        if (_writer is not { } w)
        {
            return;
        }

        try
        {
            await w.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static TaskCompletionSource<IpcWriter> NewConnectedSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }
}
