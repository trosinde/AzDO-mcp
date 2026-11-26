using System.Collections.ObjectModel;
using AzdoMcp.Core;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Discovery;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

public enum WizardStepKind
{
    Connection,
    Projects,
    Project,
    Summary,
}

public sealed record WizardStepInfo(string Title, bool IsCurrent, bool IsDone);

/// <summary>A row of step "Projekte".</summary>
public sealed class DiscoveryRow : ObservableObject
{
    private bool _isSelected;

    public DiscoveryRow(DiscoveredProject project, bool isConfigured, bool preselect)
    {
        Project = project;
        IsConfigured = isConfigured;
        _isSelected = preselect && !isConfigured;
        Rights = RightPills.Rows(project.Effective);
    }

    public DiscoveredProject Project { get; }

    public string Name => Project.Name;

    public bool IsConfigured { get; }

    public bool CanSelect => !IsConfigured;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value && CanSelect); }

    public IReadOnlyList<RightsRow> Rights { get; }

    public Pill Status => IsConfigured ? new Pill("bereits eingerichtet", null) : new Pill("neu", StatusLevel.Ok);

    /// <summary>Preselect when at least one area is not denied for reading.</summary>
    public static bool HasAnyRead(DiscoveredProject p) => Access.Areas.Any(a => p.Effective[a].Read != Check.No);
}

/// <summary>Mode of one tool group inside a project step.</summary>
public sealed class GroupModeChoice : ObservableObject
{
    private PermissionMode _mode;

    public GroupModeChoice(ToolGroup group, PermissionMode inherited, string? lockReason)
    {
        Group = group;
        Inherited = inherited;
        LockReason = lockReason;
        _mode = lockReason is null ? inherited : PermissionMode.Blocked;
    }

    public ToolGroup Group { get; }

    public string Title => Group.Display();

    /// <summary>The value the project would inherit; only differences are stored.</summary>
    public PermissionMode Inherited { get; }

    /// <summary>"kein Zugriff" / "nur Lesen" when the effective right does not allow the group.</summary>
    public string? LockReason { get; }

    public bool IsLocked => LockReason is not null;

    public bool IsEditable => !IsLocked;

    public PermissionMode Mode
    {
        get => _mode;
        set
        {
            if (!IsLocked && Set(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsAllowed));
                OnPropertyChanged(nameof(IsAsk));
                OnPropertyChanged(nameof(IsBlocked));
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    public bool IsAllowed { get => Mode == PermissionMode.Allowed; set { if (value) { Mode = PermissionMode.Allowed; } } }

    public bool IsAsk { get => Mode == PermissionMode.Ask; set { if (value) { Mode = PermissionMode.Ask; } } }

    public bool IsBlocked { get => Mode == PermissionMode.Blocked; set { if (value) { Mode = PermissionMode.Blocked; } } }

    public bool IsChanged => !IsLocked && Mode != Inherited;
}

/// <summary>State of one "Projekt k von n" step.</summary>
public sealed class ProjectSetup : ObservableObject
{
    private string _alias;
    private bool _isDefault;
    private bool _allowWrite;
    private string? _aliasError;

    public ProjectSetup(DiscoveredProject project, string alias, IReadOnlyList<GroupModeChoice> groups)
    {
        Project = project;
        _alias = alias;
        Groups = groups;
        Rights = RightPills.Rows(project.Effective);
    }

    public DiscoveredProject Project { get; }

    public string Name => Project.Name;

    public IReadOnlyList<RightsRow> Rights { get; }

    public IReadOnlyList<GroupModeChoice> Groups { get; }

    /// <summary>Kurzname for prompts; checked against the rules while typing.</summary>
    public string Alias { get => _alias; set { if (Set(ref _alias, value)) { AliasError = ShortName.Validate(value); OnPropertyChanged(nameof(AliasHint)); } } }

    /// <summary>Preview of how the Kurzname is used in a prompt.</summary>
    public string AliasHint => $"So sprichst du es an: „offene Bugs in {(string.IsNullOrWhiteSpace(Alias) ? "…" : ShortName.Normalize(Alias))}“";

    public static string RuleText => ShortName.RuleText + "; nicht PR, BUG, ID, TEST, TASK, ALL.";

    public string? AliasError { get => _aliasError; set => Set(ref _aliasError, value); }

    public bool IsDefault { get => _isDefault; set => Set(ref _isDefault, value); }

    /// <summary>Schreibschalter; locked only by a checked "nein" for Work Items Schreiben.</summary>
    public bool AllowWrite { get => _allowWrite; set => Set(ref _allowWrite, value && CanWrite); }

    public bool CanWrite => Project.Effective[AccessArea.WorkItems].Write != Check.No;

    /// <summary>Reason the switch is locked: the project right if it says "nein", otherwise the PAT.</summary>
    public string? WriteLockReason => CanWrite ? null
        : Project.ProjectRights[AccessArea.WorkItems].Write == Check.No ? "kein Projektrecht zum Bearbeiten" : "PAT ohne Schreibrecht";

    public string WriteHint => !CanWrite
        ? $"Nicht möglich: {WriteLockReason}."
        : Project.Effective[AccessArea.WorkItems].Write == Check.Yes
            ? "Aus = Schreib-Tools bleiben gesperrt, auch wenn Schreiben auf „Erlaubt“ steht."
            : ProjectAccessStatus.UntestedHint;

    public IReadOnlyDictionary<ToolGroup, PermissionMode> ChangedModes =>
        Groups.Where(g => g.IsChanged).ToDictionary(g => g.Group, g => g.Mode);

    public string ModesSummary
    {
        get
        {
            var changed = Groups.Where(g => g.IsChanged).Select(g => $"{g.Title}: {g.Mode.Display()}").ToList();
            return changed.Count == 0 ? "Voreinstellung" : string.Join(" · ", changed);
        }
    }
}

/// <summary>
/// Einrichtungs-Wizard: Verbindung → Projekte → one step per selected project → Zusammenfassung.
/// Nothing is saved before "Übernehmen".
/// </summary>
public sealed class SetupWizardViewModel : ObservableObject
{
    private static readonly ToolGroup[] WizardGroups = [ToolGroup.WorkItemsRead, ToolGroup.WorkItemsWrite, ToolGroup.Git, ToolGroup.Pipelines, ToolGroup.Test, ToolGroup.Planning];

    private readonly ManagerContext _ctx;
    private Connection _connection = new();
    private bool _isNewConnection = true;
    private WizardStepKind _step = WizardStepKind.Connection;
    private int _projectIndex;
    private DiscoveryResult? _discovery;
    private string _pat = "";
    private bool _testSucceeded;
    private bool _isBusy;
    private string? _message;
    private string _filter = FilterAll;
    private string _lastTested = "";
    private ServerType _serverType = ServerType.Server;
    private string _name = "";
    private string _collectionUrl = "";
    private DateTime? _patExpires;
    private ReminderOption _reminder = ConnectionsViewModel.ReminderOptions[2];

    public const string FilterAll = "Alle Projekte";
    public const string FilterNew = "Nur neue";
    public const string FilterConfigured = "Bereits eingerichtet";
    public const string DefaultsHint = "Voreinstellung für neue Projekte: Lesen = Erlaubt · Schreiben = Nachfragen · Schreibschalter aus.";

    public SetupWizardViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        TestCommand = new AsyncCommand(TestAsync, () => !IsBusy, Fail);
        NextCommand = new AsyncCommand(NextAsync, () => CanGoNext, Fail);
        BackCommand = new RelayCommand(Back, () => CanGoBack);
        SkipRestCommand = new RelayCommand(SkipRest, () => _step == WizardStepKind.Project);
        ApplyCommand = new AsyncCommand(ApplyAsync, () => _step == WizardStepKind.Summary && !IsBusy, Fail);
        CancelCommand = new RelayCommand(Cancel);
        SelectAllCommand = new RelayCommand(() =>
        {
            foreach (var r in Rows.Where(r => r.CanSelect))
            {
                r.IsSelected = true;
            }

            OnPropertyChanged(nameof(SelectedCount));
        });
        CreatePatCommand = new RelayCommand(() => _ctx.Dialogs.OpenUrl($"{CollectionUrl.Trim().TrimEnd('/')}/_usersSettings/tokens"),
            () => Uri.TryCreate(CollectionUrl, UriKind.Absolute, out _));
    }

    public static IReadOnlyList<string> Filters { get; } = [FilterAll, FilterNew, FilterConfigured];

    // ── navigation ─────────────────────────────────────────────

    public AsyncCommand TestCommand { get; }

    public AsyncCommand NextCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand SkipRestCommand { get; }

    public AsyncCommand ApplyCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand CreatePatCommand { get; }

    public WizardStepKind Step
    {
        get => _step;
        private set
        {
            if (Set(ref _step, value))
            {
                Raise();
            }
        }
    }

    public bool IsConnectionStep => Step == WizardStepKind.Connection;

    public bool IsProjectsStep => Step == WizardStepKind.Projects;

    public bool IsProjectStep => Step == WizardStepKind.Project;

    public bool IsSummaryStep => Step == WizardStepKind.Summary;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                Raise();
            }
        }
    }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public string Title => _isNewConnection ? "Neue Verbindung einrichten" : $"Projekte erkennen · {_connection.Name}";

    public string StepTitle => Step switch
    {
        WizardStepKind.Connection => "Schritt 1 · Verbindung",
        WizardStepKind.Projects => "Projekte auswählen",
        WizardStepKind.Project => $"Projekt {_projectIndex + 1} von {Setups.Count} · {CurrentSetup?.Name}",
        _ => "Zusammenfassung",
    };

    public IReadOnlyList<WizardStepInfo> Steps
    {
        get
        {
            var list = new List<WizardStepInfo>();
            if (_isNewConnection)
            {
                list.Add(new("Verbindung", Step == WizardStepKind.Connection, Step > WizardStepKind.Connection));
            }

            list.Add(new("Projekte", Step == WizardStepKind.Projects, Step > WizardStepKind.Projects));
            for (var i = 0; i < Setups.Count; i++)
            {
                list.Add(new(Setups[i].Name, Step == WizardStepKind.Project && i == _projectIndex,
                    Step == WizardStepKind.Summary || (Step == WizardStepKind.Project && i < _projectIndex)));
            }

            list.Add(new("Zusammenfassung", Step == WizardStepKind.Summary, false));
            return list;
        }
    }

    public bool CanGoBack => !IsBusy && (Step == WizardStepKind.Summary || Step == WizardStepKind.Project || (Step == WizardStepKind.Projects && _isNewConnection));

    public bool CanGoNext => !IsBusy && Step switch
    {
        WizardStepKind.Connection => _testSucceeded,
        WizardStepKind.Projects => true,
        WizardStepKind.Project => true,
        _ => false,
    };

    public string NextText => Step == WizardStepKind.Projects && SelectedCount == 0 ? "Weiter zur Zusammenfassung" : "Weiter";

    // ── step Verbindung ────────────────────────────────────────

    public ServerType ServerType { get => _serverType; set { if (Set(ref _serverType, value)) { InvalidateTest(); OnPropertyChanged(nameof(IsServices)); OnPropertyChanged(nameof(IsServer)); } } }

    public bool IsServices { get => ServerType == ServerType.Services; set { if (value) { ServerType = ServerType.Services; } } }

    public bool IsServer { get => ServerType == ServerType.Server; set { if (value) { ServerType = ServerType.Server; } } }

    public string Name { get => _name; set => Set(ref _name, value); }

    public string CollectionUrl { get => _collectionUrl; set { if (Set(ref _collectionUrl, value)) { InvalidateTest(); CreatePatCommand.RaiseCanExecuteChanged(); } } }

    public string Pat { get => _pat; set { if (Set(ref _pat, value)) { InvalidateTest(); } } }

    public DateTime? PatExpires { get => _patExpires; set => Set(ref _patExpires, value); }

    public ReminderOption Reminder { get => _reminder; set => Set(ref _reminder, value); }

    public ObservableCollection<TestArea> TestResults { get; } = [];

    public ObservableCollection<RightsRow> Scope { get; } = [];

    public string ScopeFootnoteText => ConnectionsViewModel.ScopeFootnote;

    public string LastTested { get => _lastTested; private set => Set(ref _lastTested, value); }

    // ── step Projekte ──────────────────────────────────────────

    public ObservableCollection<DiscoveryRow> Rows { get; } = [];

    public IEnumerable<DiscoveryRow> FilteredRows => Filter switch
    {
        FilterNew => Rows.Where(r => !r.IsConfigured),
        FilterConfigured => Rows.Where(r => r.IsConfigured),
        _ => Rows,
    };

    public string Filter { get => _filter; set { if (Set(ref _filter, value ?? FilterAll)) { OnPropertyChanged(nameof(FilteredRows)); } } }

    public string FilterAllText => $"{FilterAll} ({Rows.Count})";

    public string FilterNewText => $"{FilterNew} ({Rows.Count(r => !r.IsConfigured)})";

    public string FilterConfiguredText => $"{FilterConfigured} ({Rows.Count(r => r.IsConfigured)})";

    public int SelectedCount => Rows.Count(r => r.IsSelected);

    public string DiscoverySummary => _discovery is null
        ? ""
        : $"{_connection.Host} · {_discovery.Projects.Count} Projekte für diesen PAT sichtbar · Rechte je Projekt geprüft {_discovery.At.ToLocalTime():HH:mm}";

    public IReadOnlyList<string> DiscoveryNotes => _discovery?.Notes ?? [];

    public string DefaultsHintText => DefaultsHint;

    // ── project steps ──────────────────────────────────────────

    public ObservableCollection<ProjectSetup> Setups { get; } = [];

    public ProjectSetup? CurrentSetup => Step == WizardStepKind.Project && _projectIndex < Setups.Count ? Setups[_projectIndex] : null;

    public string ApplyText => Setups.Count == 1 ? "Übernehmen (1 Projekt)" : $"Übernehmen ({Setups.Count} Projekte)";

    // ── entry points ───────────────────────────────────────────

    /// <summary>"+ Neue Verbindung".</summary>
    public void StartNew()
    {
        _isNewConnection = true;
        _connection = new Connection();
        _discovery = null;
        Name = "";
        CollectionUrl = "";
        Pat = "";
        PatExpires = null;
        Reminder = ConnectionsViewModel.ReminderOptions[2];
        ServerType = ServerType.Server;
        TestResults.Clear();
        Scope.Clear();
        Rows.Clear();
        Setups.Clear();
        LastTested = "";
        Message = null;
        _testSucceeded = false;
        Step = WizardStepKind.Connection;
        Raise();
    }

    /// <summary>"Projekte erkennen" for an existing connection: starts at "Projekte".</summary>
    public async Task StartForConnectionAsync(string connectionId)
    {
        StartNew();
        _isNewConnection = false;
        _connection = _ctx.Connections.Get(connectionId) ?? throw new InvalidOperationException("Verbindung nicht gefunden.");
        _pat = _ctx.Connections.GetPat(_connection);
        Step = WizardStepKind.Projects;
        await DiscoverAsync();
    }

    /// <summary>From "Neu einlesen": continue with the project steps for the checked new projects.</summary>
    public void StartFromRescan(Connection connection, string pat, DiscoveryResult result, IReadOnlyCollection<string> newProjectIds)
    {
        StartNew();
        _isNewConnection = false;
        _connection = connection.Clone();
        _pat = pat;
        _discovery = result;
        FillRows();
        foreach (var r in Rows)
        {
            r.IsSelected = newProjectIds.Contains(r.Project.AzdoId);
        }

        BuildSetups();
        _projectIndex = 0;
        Step = Setups.Count > 0 ? WizardStepKind.Project : WizardStepKind.Summary;
        Raise();
    }

    // ── actions ────────────────────────────────────────────────

    private async Task TestAsync()
    {
        Message = null;
        TestResults.Clear();
        Scope.Clear();
        var c = DraftConnection();
        if (!Uri.TryCreate(c.CollectionUrl, UriKind.Absolute, out _))
        {
            Message = "Collection-URL muss mit https:// beginnen.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Pat))
        {
            Message = "Personal Access Token fehlt.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _ctx.Tester.TestAsync(c, Pat.Trim());
            foreach (var a in result.Areas)
            {
                TestResults.Add(a);
            }

            foreach (var area in Access.Areas)
            {
                Scope.Add(RightPills.Row(area, result.PatScope?[area] ?? AreaAccess.Unknown));
            }

            _testSucceeded = result.Succeeded;
            LastTested = $"Letzter Test: heute {result.TestedAt.ToLocalTime():HH:mm}";
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = ConnectionStore.SanitizeAlias(c.Host.Split('.')[0]);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task NextAsync()
    {
        Message = null;
        switch (Step)
        {
            case WizardStepKind.Connection:
                var c = DraftConnection();
                try
                {
                    ConnectionStore.Validate(c);
                }
                catch (ConnectionValidationException ex)
                {
                    Message = ex.Message;
                    return;
                }

                if (_ctx.Connections.FindConnection(c.Name) is not null)
                {
                    Message = $"Der Name „{c.Name}“ wird bereits verwendet.";
                    return;
                }

                _connection = c;
                _pat = Pat.Trim();
                Step = WizardStepKind.Projects;
                await DiscoverAsync();
                break;

            case WizardStepKind.Projects:
                BuildSetups();
                _projectIndex = 0;
                Step = Setups.Count > 0 ? WizardStepKind.Project : WizardStepKind.Summary;
                Raise();
                break;

            case WizardStepKind.Project:
                if (!ValidateAlias(CurrentSetup!))
                {
                    return;
                }

                if (_projectIndex + 1 < Setups.Count)
                {
                    _projectIndex++;
                    Raise();
                }
                else
                {
                    Step = WizardStepKind.Summary;
                }

                break;
        }
    }

    private void Back()
    {
        Message = null;
        switch (Step)
        {
            case WizardStepKind.Summary when Setups.Count > 0:
                _projectIndex = Setups.Count - 1;
                Step = WizardStepKind.Project;
                break;
            case WizardStepKind.Summary:
            case WizardStepKind.Project when _projectIndex == 0:
                Step = WizardStepKind.Projects;
                break;
            case WizardStepKind.Project:
                _projectIndex--;
                Raise();
                break;
            case WizardStepKind.Projects when _isNewConnection:
                Step = WizardStepKind.Connection;
                break;
        }
    }

    private void SkipRest()
    {
        for (var i = _projectIndex; i < Setups.Count; i++)
        {
            if (!ValidateAlias(Setups[i]))
            {
                _projectIndex = i;
                Raise();
                return;
            }
        }

        Step = WizardStepKind.Summary;
    }

    private void Cancel()
    {
        _ctx.Navigate("connections", _isNewConnection ? null : _connection.Id);
    }

    private async Task ApplyAsync()
    {
        if (_discovery is null)
        {
            return;
        }

        foreach (var s in Setups)
        {
            if (!ValidateAlias(s))
            {
                _projectIndex = Setups.IndexOf(s);
                Step = WizardStepKind.Project;
                Raise();
                return;
            }
        }

        IsBusy = true;
        try
        {
            var choices = Setups.ToDictionary(s => s.Project.AzdoId,
                s => new ProjectChoice(ShortName.Normalize(s.Alias), s.IsDefault, s.AllowWrite, s.ChangedModes));
            var before = _isNewConnection ? null : _ctx.Connections.Get(_connection.Id);
            var merged = DiscoveryMerger.Apply(before ?? _connection, _discovery, choices);
            var saved = _ctx.Connections.Save(merged.Connection, _isNewConnection ? _pat : null);
            try
            {
                if (merged.ProjectModes.Count > 0)
                {
                    _ctx.Permissions.Update(settings =>
                    {
                        foreach (var (key, modes) in merged.ProjectModes)
                        {
                            var scope = settings.ForProject(key);
                            foreach (var (group, mode) in modes)
                            {
                                scope.Groups[group] = mode;
                            }
                        }
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                // Keep both files consistent: undo the connection change.
                if (before is null)
                {
                    _ctx.Connections.Remove(saved.Id);
                }
                else
                {
                    _ctx.Connections.Save(before, null);
                }

                throw;
            }

            _ctx.NotifyConfigurationChanged();
            var n = Setups.Count;
            _ctx.Dialogs.Info("Projekte eingerichtet", n == 0
                ? $"Rechte für „{saved.Name}“ aktualisiert."
                : $"{n} Projekt(e) für „{saved.Name}“ eingerichtet. Schreiben schaltest du pro Projekt mit dem Schreibschalter frei.");
            _ctx.Navigate("connections", saved.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiscoverAsync()
    {
        IsBusy = true;
        Message = null;
        try
        {
            _discovery = await _ctx.Discovery.DiscoverAsync(_connection, _pat);
            if (!_isNewConnection || _connection.PatScope is null)
            {
                _connection.PatScope = _discovery.PatScope.Clone();
            }

            FillRows();
        }
        catch (AzdoApiException ex)
        {
            Message = SecretScrubber.Scrub(ex.Message, _pat);
        }
        finally
        {
            IsBusy = false;
            Raise();
        }
    }

    private void FillRows()
    {
        Rows.Clear();
        var stored = _isNewConnection ? _connection : _ctx.Connections.Get(_connection.Id) ?? _connection;
        var diff = DiscoveryMerger.Diff(stored, _discovery!);
        foreach (var d in _discovery!.Projects)
        {
            var configured = !diff.New.Contains(d);
            var row = new DiscoveryRow(d, configured, DiscoveryRow.HasAnyRead(d));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DiscoveryRow.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(NextText));
                }
            };
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(FilteredRows));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(DiscoverySummary));
        OnPropertyChanged(nameof(DiscoveryNotes));
        OnPropertyChanged(nameof(FilterAllText));
        OnPropertyChanged(nameof(FilterNewText));
        OnPropertyChanged(nameof(FilterConfiguredText));
    }

    /// <summary>Project steps for the selected rows; keeps choices of projects already set up in this run.</summary>
    private void BuildSetups()
    {
        PermissionSettings settings;
        try
        {
            settings = _ctx.Permissions.Get();
        }
        catch (ConfigurationException)
        {
            settings = new PermissionSettings();
        }

        var keep = Setups.ToDictionary(s => s.Project.AzdoId);
        Setups.Clear();
        var reserved = new List<string>();
        var anyDefault = _ctx.Connections.AllProjects().Any(p => p.Project.IsDefault);
        foreach (var row in Rows.Where(r => r.IsSelected))
        {
            if (keep.TryGetValue(row.Project.AzdoId, out var existing))
            {
                Setups.Add(existing);
                reserved.Add(existing.Alias);
                continue;
            }

            var alias = _ctx.Connections.SuggestAlias(row.Name, reserved);
            reserved.Add(alias);
            var groups = WizardGroups.Select(g =>
            {
                var inherited = PermissionEvaluator.ResolveGroup(settings, g, _connection.Id, null).Mode;
                return new GroupModeChoice(g, inherited, LockReason(row.Project, g));
            }).ToList();
            Setups.Add(new ProjectSetup(row.Project, alias, groups) { IsDefault = !anyDefault && Setups.Count == 0 });
        }

        OnPropertyChanged(nameof(ApplyText));
    }

    private static string? LockReason(DiscoveredProject p, ToolGroup g)
    {
        var access = p.Effective[Access.AreaOf(g)];
        if (access.Read == Check.No)
        {
            return "kein Zugriff";
        }

        return g == ToolGroup.WorkItemsWrite && access.Write == Check.No ? "nur Lesen" : null;
    }

    private bool ValidateAlias(ProjectSetup s)
    {
        var alias = s.Alias.Trim();
        if (ShortName.Validate(alias) is { } invalid)
        {
            s.AliasError = invalid;
            Message = s.AliasError;
            return false;
        }

        var takenByStore = _ctx.Connections.AllProjects().Any(p => p.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
        var takenHere = Setups.Any(o => !ReferenceEquals(o, s) && o.Alias.Trim().Equals(alias, StringComparison.OrdinalIgnoreCase));
        if (takenByStore || takenHere)
        {
            s.AliasError = $"Kurzname „{ShortName.Normalize(alias)}“ wird bereits verwendet";
            Message = s.AliasError;
            return false;
        }

        s.AliasError = null;
        Message = null;
        return true;
    }

    private Connection DraftConnection() => new()
    {
        Id = _connection.Id,
        Name = Name.Trim(),
        ServerType = ServerType,
        CollectionUrl = CollectionUrl.Trim().TrimEnd('/'),
        PatExpires = PatExpires is { } d ? DateOnly.FromDateTime(d) : null,
        ReminderDays = Reminder.Days,
    };

    private void InvalidateTest()
    {
        if (_testSucceeded)
        {
            _testSucceeded = false;
            Raise();
        }
    }

    private void Fail(Exception ex) => Message = SecretScrubber.Scrub(ex.Message, _pat);

    private void Raise()
    {
        foreach (var n in new[]
                 {
                     nameof(IsConnectionStep), nameof(IsProjectsStep), nameof(IsProjectStep), nameof(IsSummaryStep), nameof(StepTitle), nameof(Steps),
                     nameof(CanGoBack), nameof(CanGoNext), nameof(CurrentSetup), nameof(Title), nameof(ApplyText), nameof(NextText),
                 })
        {
            OnPropertyChanged(n);
        }

        NextCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        SkipRestCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        TestCommand.RaiseCanExecuteChanged();
    }
}
