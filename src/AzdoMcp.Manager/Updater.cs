using AzdoMcp.Core;
using AzdoMcp.Manager.Core.Services;
using Velopack;
using Velopack.Sources;

namespace AzdoMcp.Manager;

/// <summary>Velopack behind <see cref="IUpdateBackend"/>. The feed comes from update.json (none built in).</summary>
public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    private VelopackUpdateBackend(UpdateManager manager) => _manager = manager;

    public bool IsInstalled => _manager.IsInstalled && !_manager.IsPortable;

    /// <summary>Null when no source is configured.</summary>
    public static VelopackUpdateBackend? Create(UpdateSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Source))
        {
            return null;
        }

        var source = settings.Source.Trim();
        IUpdateSource feed = source.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            ? new GithubSource("https://github.com/" + source["github:".Length..].Trim('/'), null, false)
            : source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new SimpleWebSource(source)
                : new SimpleFileSource(new DirectoryInfo(source));
        return new VelopackUpdateBackend(new UpdateManager(feed));
    }

    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public async Task ApplyAsync(Action<int>? progress, CancellationToken ct)
    {
        if (_pending is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pending, progress, ct);
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease, ["--minimized"]);
    }
}

public static class Updater
{
    /// <summary>True when Velopack installed this copy (not the portable zip, not a dev build).</summary>
    public static bool IsInstalledByVelopack()
    {
        try
        {
            var m = new UpdateManager("https://localhost.invalid/");
            return m.IsInstalled && !m.IsPortable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static UpdateService Create(AppPaths paths, Func<Task> beforeApply)
    {
        var installed = IsInstalledByVelopack();
        var backend = installed ? VelopackUpdateBackend.Create(UpdateSettings.Load(paths)) : null;
        return new UpdateService(backend, installed, beforeApply);
    }
}
