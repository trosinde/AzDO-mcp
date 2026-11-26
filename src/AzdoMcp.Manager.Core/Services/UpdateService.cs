using System.Text.Json;
using AzdoMcp.Core;
using AzdoMcp.Core.Infrastructure;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.Services;

/// <summary>Content of update.json in the data root. No feed is built in (no internal URL in the repo).</summary>
public sealed class UpdateSettings
{
    /// <summary>Velopack release folder (https://… or a UNC/local path) or "github:owner/repo".</summary>
    public string? Source { get; set; }

    public bool CheckOnStart { get; set; } = true;

    public static UpdateSettings Load(AppPaths paths)
    {
        var file = Path.Combine(paths.Root, "update.json");
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(file), Json.File) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}

/// <summary>What the Velopack integration provides; faked in tests.</summary>
public interface IUpdateBackend
{
    /// <summary>False when running unpacked (portable zip or developer build).</summary>
    bool IsInstalled { get; }

    /// <summary>Newer version available, or null.</summary>
    Task<string?> CheckAsync(CancellationToken ct);

    /// <summary>Downloads and applies; the process restarts afterwards.</summary>
    Task ApplyAsync(Action<int>? progress, CancellationToken ct);
}

public enum UpdateState
{
    Portable,
    NotConfigured,
    Checking,
    UpToDate,
    Available,
    Applying,
    Failed,
}

/// <summary>Footer status and "Jetzt aktualisieren".</summary>
public sealed class UpdateService : ObservableObject
{
    private readonly IUpdateBackend? _backend;
    private readonly Func<Task>? _beforeApply;
    private UpdateState _state;
    private string? _availableVersion;
    private string? _error;

    /// <param name="backend">Null when no source is configured.</param>
    /// <param name="isInstalled">Whether the app runs from the installed location.</param>
    /// <param name="beforeApply">Asks running servers to exit so their files can be replaced.</param>
    public UpdateService(IUpdateBackend? backend, bool isInstalled, Func<Task>? beforeApply = null)
    {
        _backend = backend;
        _beforeApply = beforeApply;
        _state = !isInstalled ? UpdateState.Portable : backend is null ? UpdateState.NotConfigured : UpdateState.UpToDate;
    }

    public UpdateState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public string? AvailableVersion => _availableVersion;

    public string? Error => _error;

    public bool CanApply => State == UpdateState.Available;

    /// <summary>Text after "v2.0.0 · " in the navigation footer.</summary>
    public string StatusText => State switch
    {
        UpdateState.Portable => "Updates: aus",
        UpdateState.NotConfigured => "Updates: nicht konfiguriert",
        UpdateState.Checking => "Updates: prüfe …",
        UpdateState.UpToDate => "Updates: aktuell",
        UpdateState.Available => $"Update verfügbar ({_availableVersion})",
        UpdateState.Applying => "Update wird installiert …",
        _ => "Updates: Fehler",
    };

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (_backend is null || State is UpdateState.Portable or UpdateState.Applying)
        {
            return;
        }

        State = UpdateState.Checking;
        try
        {
            _availableVersion = await _backend.CheckAsync(ct);
            _error = null;
            State = _availableVersion is null ? UpdateState.UpToDate : UpdateState.Available;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _error = ex.Message;
            State = UpdateState.Failed;
        }
    }

    /// <summary>Only after the user confirmed. Servers are asked to exit first.</summary>
    public async Task ApplyAsync(Action<int>? progress = null, CancellationToken ct = default)
    {
        if (_backend is null || State != UpdateState.Available)
        {
            return;
        }

        State = UpdateState.Applying;
        try
        {
            if (_beforeApply is not null)
            {
                await _beforeApply();
            }

            await _backend.ApplyAsync(progress, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _error = ex.Message;
            State = UpdateState.Failed;
        }
    }
}
