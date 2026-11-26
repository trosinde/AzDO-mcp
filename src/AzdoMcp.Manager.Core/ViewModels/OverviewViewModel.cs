using System.Collections.ObjectModel;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Manager.Core.Clients;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

/// <summary>One line of "Verbindung / Projekt": a connection (IsProject = false) or a project below it.</summary>
public sealed record OverviewRow(
    bool IsProject,
    string ConnectionId,
    string Title,
    string? Subtitle,
    bool IsDefault,
    Pill? Expiry,
    Pill? Status,
    IReadOnlyList<RightsRow> Rights,
    string? StatusText,
    string? ProjectKey = null,
    string? StatusTooltip = null);

public sealed record ClientRow(ClientKind Kind, string Name, string Detail, Pill Status);

/// <summary>Page "Übersicht".</summary>
public sealed class OverviewViewModel : ObservableObject
{
    public const string Footnote1 = "Je Bereich Lesen | Schreiben: Grün = ja · Rot = nein · Grau = n/a (nicht prüfbar ohne echtes Schreiben) oder ? (noch nicht geprüft). WI = Work Items, Iter = Iterationen.";

    public const string Footnote2 = "Effektives Recht = PAT-Scope (beim Erstellen des PAT freigegebene Bereiche, gilt für die ganze Collection) kombiniert mit deinen Projektrechten in Azure DevOps; ein „nein“ auf einer Seite sperrt.";

    private readonly ManagerContext _ctx;
    private string _serverStatus = "";
    private string _serverDetail = "";
    private string _serverToolTip = "";
    private Pill _serverPill = new("", null);
    private string _connectionsHint = "";
    private StatusLevel? _connectionsHintLevel;
    private int _callsToday;
    private int _writesToday;
    private int _errors24h;
    private string _errorsHint = "";
    private int _pendingCount;
    private string _pendingHint = "";
    private int _legacyCount;
    private bool _legacyDismissed;
    private LegacyConfig? _legacy;
    private string _pauseButtonText = "Stoppen";
    private int _connectionCount;
    private int _projectCount;

    public OverviewViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        RestartCommand = new AsyncCommand(() => _ctx.Host.ReloadAsync());
        PauseCommand = new AsyncCommand(TogglePauseAsync);
        ImportCommand = new RelayCommand(Import);
        LaterCommand = new RelayCommand(() =>
        {
            _legacyDismissed = true;
            OnPropertyChanged(nameof(ShowLegacyBanner));
        });
        AddConnectionCommand = new RelayCommand(() => _ctx.Navigate("wizard", null));
        RescanCommand = new RelayCommand(p => _ctx.Navigate("rescan", (p as OverviewRow)?.ConnectionId));
        Refresh();
    }

    public ObservableCollection<OverviewRow> Rows { get; } = [];

    public ObservableCollection<ClientRow> Clients { get; } = [];

    public AsyncCommand RestartCommand { get; }

    public AsyncCommand PauseCommand { get; }

    public RelayCommand ImportCommand { get; }

    public RelayCommand LaterCommand { get; }

    public RelayCommand AddConnectionCommand { get; }

    public RelayCommand RescanCommand { get; }

    public string Footnote1Text => Footnote1;

    public string Footnote2Text => Footnote2;

    public string ServerStatus { get => _serverStatus; private set => Set(ref _serverStatus, value); }

    public Pill ServerPill { get => _serverPill; private set => Set(ref _serverPill, value); }

    public string ServerDetail { get => _serverDetail; private set => Set(ref _serverDetail, value); }

    public string ServerToolTip { get => _serverToolTip; private set => Set(ref _serverToolTip, value); }

    public string PauseButtonText { get => _pauseButtonText; private set => Set(ref _pauseButtonText, value); }

    public int ConnectionCount { get => _connectionCount; private set => Set(ref _connectionCount, value); }

    public int ProjectCount { get => _projectCount; private set => Set(ref _projectCount, value); }

    public string ProjectCountText => ProjectCount == 1 ? "· 1 Projekt" : $"· {ProjectCount} Projekte";

    public string ConnectionsHint { get => _connectionsHint; private set => Set(ref _connectionsHint, value); }

    public StatusLevel? ConnectionsHintLevel { get => _connectionsHintLevel; private set => Set(ref _connectionsHintLevel, value); }

    public int CallsToday { get => _callsToday; private set => Set(ref _callsToday, value); }

    public int WritesToday { get => _writesToday; private set => Set(ref _writesToday, value); }

    public string WritesTodayText => $"davon {WritesToday} schreibend";

    public int Errors24h { get => _errors24h; private set => Set(ref _errors24h, value); }

    public string ErrorsHint { get => _errorsHint; private set => Set(ref _errorsHint, value); }

    public int PendingCount { get => _pendingCount; private set => Set(ref _pendingCount, value); }

    public string PendingHint { get => _pendingHint; private set => Set(ref _pendingHint, value); }

    public int LegacyCount { get => _legacyCount; private set => Set(ref _legacyCount, value); }

    public bool ShowLegacyBanner => LegacyCount > 0 && !_legacyDismissed;

    public string LegacyText => LegacyCount == 1 ? "1 PAT im Klartext gefunden" : $"{LegacyCount} PATs im Klartext gefunden";

    public void Refresh()
    {
        var now = _ctx.Time.GetUtcNow();
        var instances = _ctx.Host.Instances;

        ServerStatus = _ctx.Host.IsPaused ? "MCP-Server pausiert" : "MCP-Server läuft";
        ServerPill = new Pill(ServerStatus, _ctx.Host.IsPaused ? StatusLevel.Warning : StatusLevel.Ok);
        ServerDetail = "stdio · Freigaben über Named Pipe (nur dieser Benutzer)";
        ServerToolTip = instances.Count switch { 0 => "kein Client verbunden", 1 => "1 Client aktiv", _ => $"{instances.Count} Clients aktiv" };
        PauseButtonText = _ctx.Host.IsPaused ? "Fortsetzen" : "Stoppen";

        var connections = _ctx.SafeConnections();
        PermissionSettings settings;
        try
        {
            settings = _ctx.Permissions.Get();
        }
        catch (ConfigurationException)
        {
            settings = new PermissionSettings();
        }

        Rows.Clear();
        foreach (var c in connections)
        {
            var s = _ctx.StatusOf(c);
            var expiry = c.PatExpires is null ? new Pill("unbekannt", null) : new Pill(s.ExpiryText, ExpiryLevel(c));
            var statusText = s.Level switch { StatusLevel.Ok => "OK", StatusLevel.Warning => "erneuern", _ => s.StatusText };
            Rows.Add(new OverviewRow(false, c.Id, c.Name, "/ " + CollectionName(c), false, expiry, new Pill(statusText, s.Level), [],
                c.Projects.Count == 0 ? "keine Projekte" : null));
            foreach (var p in c.Projects)
            {
                var target = new ProjectRef(c, p);
                var status = ProjectAccessStatus.For(target, settings);
                Rows.Add(new OverviewRow(true, c.Id, p.Alias, p.Alias.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ? null : p.Name, p.IsDefault, null, null,
                    RightPills.Rows(target.Effective), status.Text, p.Key, status.Tooltip));
            }
        }

        ConnectionCount = connections.Count;
        ProjectCount = connections.Sum(c => c.Projects.Count);
        OnPropertyChanged(nameof(ProjectCountText));
        var broken = connections.Count(c => _ctx.StatusOf(c).Level == StatusLevel.Error);
        var soon = connections.Count(c => c.PatExpires is not null && ExpiryLevel(c) == StatusLevel.Warning);
        (ConnectionsHint, ConnectionsHintLevel) = (broken, soon) switch
        {
            ( > 0, _) => (broken == 1 ? "1 Verbindung gestört" : $"{broken} Verbindungen gestört", StatusLevel.Error),
            (_, > 0) => (soon == 1 ? "1 PAT läuft bald ab" : $"{soon} PATs laufen bald ab", StatusLevel.Warning),
            _ when connections.Count == 0 => ("noch keine Verbindung", (StatusLevel?)null),
            _ => ("alle PATs gültig", StatusLevel.Ok),
        };

        var records = LogViewModel.Collapse(_ctx.Audit.Read(now.AddHours(-24), now));
        var today = _ctx.Today;
        var todays = records.Where(r => DateOnly.FromDateTime(r.Time.ToLocalTime().DateTime) == today && r.Outcome != AuditOutcome.Pending).ToList();
        CallsToday = todays.Count;
        WritesToday = todays.Count(r => r.IsWrite);
        OnPropertyChanged(nameof(WritesTodayText));
        var errors = records.Where(r => r.Outcome == AuditOutcome.Error).ToList();
        Errors24h = errors.Count;
        var top = errors.GroupBy(r => r.Result.Split(' ')[0]).OrderByDescending(g => g.Count()).Take(2).Select(g => g.Key).ToList();
        ErrorsHint = top.Count == 0 ? "keine" : "meist " + string.Join(" / ", top);

        var pending = _ctx.Host.PendingApprovals;
        PendingCount = pending.Count;
        PendingHint = pending.Count == 0 ? "keine" : $"{pending[0].Tool} prüfen";

        Clients.Clear();
        foreach (var state in _ctx.Clients.DetectAll())
        {
            var name = state.Kind == ClientKind.VsCode ? "VS Code / Copilot" : state.Name;
            var active = instances.Where(i => i.ClientName == name).OrderByDescending(i => i.LastCall ?? i.ConnectedAt).FirstOrDefault();
            Clients.Add(active is not null
                ? new ClientRow(state.Kind, name, "stdio · " + RelativeTime.Since(active.LastCall ?? active.ConnectedAt, now), new Pill("aktiv", StatusLevel.Ok))
                : state.IsConfigured
                    ? new ClientRow(state.Kind, name, "eingerichtet · nicht verbunden", new Pill("eingerichtet", StatusLevel.Ok))
                    : new ClientRow(state.Kind, name, "nicht eingerichtet", new Pill("offen", null)));
        }

        _legacy = LegacyImporter.Detect(_ctx.LegacyCandidates);
        LegacyCount = _legacy?.Entries.Count ?? 0;
        OnPropertyChanged(nameof(ShowLegacyBanner));
        OnPropertyChanged(nameof(LegacyText));
    }

    /// <summary>"schreibt direkt", "schreibt mit Freigabe", "nur Lesen", "kein Zugriff", "nicht mehr sichtbar".</summary>
    public static string ProjectStatus(ProjectRef p, PermissionSettings settings) => ProjectAccessStatus.For(p, settings).Text;

    private static string CollectionName(Connection c) =>
        Uri.TryCreate(c.CollectionUrl, UriKind.Absolute, out var u) && u.AbsolutePath.Trim('/') is { Length: > 0 } path ? path.Split('/')[^1] : c.Host;

    private StatusLevel? ExpiryLevel(Connection c) => c.PatExpires is null
        ? null
        : ExpiryClassifier.Classify(new Connection { PatExpires = c.PatExpires, ReminderDays = c.ReminderDays }, _ctx.Today).Level;

    private async Task TogglePauseAsync()
    {
        if (_ctx.Host.IsPaused)
        {
            await _ctx.Host.ResumeAsync();
        }
        else
        {
            await _ctx.Host.PauseAsync();
        }

        Refresh();
    }

    private void Import()
    {
        if (_legacy is null)
        {
            return;
        }

        try
        {
            var result = new LegacyImporter(_ctx.Connections).Import(_legacy);
            _ctx.Dialogs.Info("Import", string.Join("\n", result.Messages));
            _ctx.NotifyConfigurationChanged();
        }
        catch (Exception ex) when (ex is ConnectionValidationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _ctx.Dialogs.Error("Import fehlgeschlagen", $"Nichts importiert, config.json bleibt unverändert.\n{ex.Message}");
        }

        Refresh();
    }
}
