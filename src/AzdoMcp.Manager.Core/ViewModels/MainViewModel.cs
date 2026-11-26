using System.Collections.ObjectModel;
using AzdoMcp.Core.Connections;
using AzdoMcp.Manager.Core.Mvvm;
using AzdoMcp.Manager.Core.Services;

namespace AzdoMcp.Manager.Core.ViewModels;

public sealed record NavItem(string Key, string Title, string Icon);

public sealed record TrayConnectionItem(string Id, string Name, StatusLevel Level, string? Hint);

/// <summary>Main window: navigation, footer and the pages; also feeds the tray menu.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ManagerContext _ctx;
    private NavItem _current;
    private object _page;
    private StatusLevel _overallLevel = StatusLevel.Ok;
    private string _trayStatus = "";
    private bool _autostart;

    public MainViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        ctx.Navigate = (key, arg) => ctx.Dispatcher.Post(() => Navigate(key, arg));
        Overview = new OverviewViewModel(ctx);
        Connections = new ConnectionsViewModel(ctx);
        Clients = new ClientsViewModel(ctx);
        Permissions = new PermissionsViewModel(ctx);
        Log = new LogViewModel(ctx);
        Wizard = new SetupWizardViewModel(ctx);
        Rescan = new ProjectRescanViewModel(ctx, Wizard);

        NavItems =
        [
            new("overview", "Übersicht", "home"),
            new("connections", "Verbindungen & PATs", "key"),
            new("clients", "MCP-Clients", "monitor"),
            new("tools", "Tools & Rechte", "shield"),
            new("log", "Protokoll", "list"),
        ];
        _current = NavItems[0];
        _page = Overview;
        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? (p as NavItem)?.Key ?? "overview"));
        ToggleAutostartCommand = new RelayCommand(() => SetAutostart(!Autostart));
        UpdateCommand = new AsyncCommand(ApplyUpdateAsync, () => ctx.Updates?.CanApply == true,
            ex => ctx.Dialogs.Error("Update", ex.Message));
        if (ctx.Updates is { } updates)
        {
            updates.PropertyChanged += (_, _) => ctx.Dispatcher.Post(() =>
            {
                OnPropertyChanged(nameof(FooterVersion));
                OnPropertyChanged(nameof(CanUpdate));
                UpdateCommand.RaiseCanExecuteChanged();
            });
        }

        ctx.ConfigurationChanged += (_, _) => ctx.Dispatcher.Post(RefreshAll);
        ctx.Host.Changed += (_, _) => ctx.Dispatcher.Post(RefreshLive);
        _autostart = ctx.Autostart.IsEnabled;
        RefreshTray();
    }

    public OverviewViewModel Overview { get; }

    public ConnectionsViewModel Connections { get; }

    public ClientsViewModel Clients { get; }

    public PermissionsViewModel Permissions { get; }

    public SetupWizardViewModel Wizard { get; }

    public ProjectRescanViewModel Rescan { get; }

    public LogViewModel Log { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    public RelayCommand NavigateCommand { get; }

    public RelayCommand ToggleAutostartCommand { get; }

    /// <summary>Click on "Update verfügbar" in the footer.</summary>
    public AsyncCommand UpdateCommand { get; }

    public bool CanUpdate => _ctx.Updates?.CanApply == true;

    public NavItem Current
    {
        get => _current;
        set
        {
            if (value is not null && value != _current)
            {
                Navigate(value.Key);
            }
        }
    }

    public object Page { get => _page; private set => Set(ref _page, value); }

    public string FooterInstall => _ctx.Environment.IsInstalled ? "Benutzer-Installation · ohne Admin" : "Portable · ohne Admin";

    public string FooterVersion => $"v{_ctx.Environment.Version} · {_ctx.Updates?.StatusText ?? _ctx.UpdateStatus}";

    // Tray
    public ObservableCollection<TrayConnectionItem> TrayConnections { get; } = [];

    public StatusLevel OverallLevel { get => _overallLevel; private set => Set(ref _overallLevel, value); }

    /// <summary>"Server läuft · 2 Clients aktiv" or "Server pausiert".</summary>
    public string TrayStatus { get => _trayStatus; private set => Set(ref _trayStatus, value); }

    public int PendingCount => _ctx.Host.PendingApprovals.Count;

    public string PendingMenuText => $"Offene Freigaben ({PendingCount})";

    public bool Autostart { get => _autostart; private set => Set(ref _autostart, value); }

    private async Task ApplyUpdateAsync()
    {
        var updates = _ctx.Updates;
        if (updates is null || !updates.CanApply)
        {
            return;
        }

        if (!_ctx.Dialogs.Confirm("Update verfügbar",
                $"Version {updates.AvailableVersion} installieren?\n\nLaufende MCP-Server werden beendet; die Clients starten sie beim nächsten Aufruf neu. Der Manager startet danach neu."))
        {
            return;
        }

        await updates.ApplyAsync();
        if (updates.State == UpdateState.Failed)
        {
            _ctx.Dialogs.Error("Update fehlgeschlagen", updates.Error ?? "unbekannter Fehler");
        }
    }

    private async Task StartSafe(Func<Task> start)
    {
        try
        {
            await start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or SecretUnreadableException or AzdoMcp.Core.Azdo.AzdoApiException)
        {
            _ctx.Dialogs.Error("Projekte", ex.Message);
            Navigate("connections");
        }
    }

    public void SetAutostart(bool enabled)
    {
        _ctx.Autostart.Set(enabled);
        Autostart = _ctx.Autostart.IsEnabled;
    }

    /// <summary>Switches pages; asks about unsaved changes on the page being left.</summary>
    public bool Navigate(string key, string? connectionId = null)
    {
        if (Page == Connections && key != "connections" && !Connections.ConfirmLeave())
        {
            return false;
        }

        if (Page == Permissions && key != "tools" && !Permissions.ConfirmLeave())
        {
            return false;
        }

        // Wizard and rescan belong to "Verbindungen & PATs" in the navigation.
        var navKey = key is "wizard" or "wizard-continue" or "rescan" ? "connections" : key;
        var item = NavItems.FirstOrDefault(n => n.Key == navKey) ?? NavItems[0];
        _current = item;
        OnPropertyChanged(nameof(Current));
        Page = key switch
        {
            "connections" => Connections,
            "clients" => Clients,
            "tools" => Permissions,
            "log" => Log,
            "wizard" or "wizard-continue" => Wizard,
            "rescan" => Rescan,
            _ => Overview,
        };

        if (key == "wizard")
        {
            if (connectionId is null)
            {
                Wizard.StartNew();
            }
            else
            {
                _ = StartSafe(() => Wizard.StartForConnectionAsync(connectionId));
            }
        }
        else if (key == "rescan" && connectionId is not null)
        {
            _ = StartSafe(() => Rescan.StartAsync(connectionId));
        }

        switch (Page)
        {
            case OverviewViewModel o:
                o.Refresh();
                break;
            case ConnectionsViewModel c when connectionId is not null:
                c.Reload(connectionId);
                break;
            case ConnectionsViewModel c when !c.IsDirty:
                c.Reload();
                break;
            case ClientsViewModel cl:
                cl.Refresh();
                break;
            case PermissionsViewModel t when !t.IsDirty:
                t.Reload();
                break;
            case LogViewModel l:
                l.Refresh();
                break;
        }

        return true;
    }

    /// <summary>Opens "Protokoll" with a request selected (Details in the notification).</summary>
    public void ShowApproval(string requestId)
    {
        Navigate("log");
        var r = _ctx.Host.PendingApprovals.FirstOrDefault(p => p.RequestId == requestId);
        if (r is not null)
        {
            Log.SelectedApproval = r;
        }
    }

    /// <summary>Called periodically (every 2 s) and on host events.</summary>
    public void RefreshLive()
    {
        if (Page == Overview)
        {
            Overview.Refresh();
        }
        else if (Page == Log)
        {
            Log.Refresh();
        }

        RefreshTray();
    }

    public void RefreshAll()
    {
        Overview.Refresh();
        if (!Permissions.IsDirty)
        {
            Permissions.Reload();
        }

        if (Page == Connections && !Connections.IsDirty)
        {
            Connections.Reload();
        }

        RefreshTray();
    }

    public void RefreshTray()
    {
        var instances = _ctx.Host.Instances.Count;
        TrayStatus = _ctx.Host.IsPaused
            ? "Server pausiert"
            : instances switch
            {
                0 => "Server bereit · kein Client aktiv",
                1 => "Server läuft · 1 Client aktiv",
                _ => $"Server läuft · {instances} Clients aktiv",
            };

        TrayConnections.Clear();
        var worst = StatusLevel.Ok;
        foreach (var c in _ctx.SafeConnections())
        {
            var s = _ctx.StatusOf(c);
            var days = ExpiryClassifier.DaysLeft(c, _ctx.Today);
            var hint = s.Level switch
            {
                StatusLevel.Warning when days is { } d => d == 1 ? "PAT in 1 Tag" : $"PAT in {d} Tagen",
                StatusLevel.Error when s.ExpiryText == "abgelaufen" => "PAT abgelaufen",
                StatusLevel.Error => s.StatusText,
                _ => null,
            };
            TrayConnections.Add(new TrayConnectionItem(c.Id, c.Name, s.Level, hint));
            worst = (StatusLevel)Math.Max((int)worst, (int)s.Level);
        }

        if (PendingCount > 0 && worst == StatusLevel.Ok)
        {
            worst = StatusLevel.Warning;
        }

        OverallLevel = worst;
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingMenuText));
        OnPropertyChanged(nameof(FooterVersion));
    }
}
