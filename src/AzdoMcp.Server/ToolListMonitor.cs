using AzdoMcp.Core;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AzdoMcp.Server;

/// <summary>
/// Sends notifications/tools/list_changed when the visible tool set changes (permissions or
/// connections edited, or "Neu starten" in the Manager).
/// </summary>
internal sealed class ToolListMonitor : IDisposable
{
    private readonly ToolExecutor _executor;
    private readonly Func<CancellationToken, Task> _notify;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private string _signature;

    public ToolListMonitor(ToolExecutor executor, AppPaths paths, Func<CancellationToken, Task> notify)
    {
        _executor = executor;
        _notify = notify;
        _signature = ToolListing.Signature(executor.VisibleTools());
        _debounce = new Timer(_ => _ = CheckAsync(), null, Timeout.Infinite, Timeout.Infinite);

        if (Directory.Exists(paths.Root))
        {
            _watcher = new FileSystemWatcher(paths.Root, "*.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _watcher.Changed += (_, _) => Trigger();
            _watcher.Created += (_, _) => Trigger();
            _watcher.Renamed += (_, _) => Trigger();
            _watcher.EnableRaisingEvents = true;
        }
    }

    public static ToolListMonitor ForServer(ToolExecutor executor, AppPaths paths, McpServer server) =>
        new(executor, paths, ct => server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, ct));

    /// <summary>Schedules a check (debounced, files are often written twice).</summary>
    public void Trigger() => _debounce.Change(300, Timeout.Infinite);

    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        var now = ToolListing.Signature(_executor.VisibleTools());
        if (now == Interlocked.Exchange(ref _signature, now))
        {
            return false;
        }

        try
        {
            await _notify(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Client gone or not initialised yet; the next tools/list is filtered anyway.
        }

        return true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
