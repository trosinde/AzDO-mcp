using System.Collections.ObjectModel;
using AzdoMcp.Core.Connections;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

/// <summary>Entry of the connection list; projects are shown indented below their connection.</summary>
public sealed record ConnectionListItem(string Id, string Name, string Detail, Pill Pill, IReadOnlyList<ProjectListItem> Projects);

public sealed record ProjectListItem(string Alias, bool IsDefault, bool Visible);

/// <summary>Editable Kurzname of one project in the connection editor.</summary>
public sealed class ShortNameItem : ObservableObject
{
    private readonly ConnectionsViewModel _owner;
    private string _value;
    private string? _error;

    public ShortNameItem(ConnectionsViewModel owner, ProjectEntry project)
    {
        _owner = owner;
        Key = project.Key;
        ProjectName = project.Name;
        Original = project.Alias;
        _value = project.Alias;
        LegacyHint = ShortName.IsValid(project.Alias) ? null : $"entspricht nicht den Kurznamen-Regeln – Vorschlag: {owner.SuggestFor(project)}";
    }

    public string Key { get; }

    public string ProjectName { get; }

    public string Original { get; }

    public string? LegacyHint { get; }

    public bool IsChanged => !string.Equals(Value.Trim(), Original, StringComparison.Ordinal);

    public string Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value))
            {
                Error = _owner.CheckShortName(this);
                _owner.ShortNameEdited();
            }
        }
    }

    public string? Error { get => _error; set => Set(ref _error, value); }
}

public sealed record ReminderOption(int Days, string Text);


public sealed record DefaultProjectOption(string ConnectionId, string ProjectKey, string Text);

/// <summary>Page "Verbindungen &amp; PATs": one connection = one collection + one PAT; projects via the wizard.</summary>
public sealed class ConnectionsViewModel : ObservableObject
{
    public const string PatMask = "••••••••••••••••••••••••••••";
    public const string PageHint = "Eine Verbindung = eine Collection mit einem PAT. Die Projekte darin richtest du mit «Projekte erkennen» ein.";

    private readonly ManagerContext _ctx;
    private ConnectionListItem? _selectedItem;
    private Connection _original = new();
    private bool _loading;
    private bool _isDirty;
    private ServerType _serverType = ServerType.Server;
    private string _name = "";
    private string _collectionUrl = "";
    private string _newPat = "";
    private bool _isReplacingPat;
    private bool _hasStoredPat;
    private DateTime? _patExpires;
    private ReminderOption _reminder;
    private DefaultProjectOption? _defaultProject;
    private string _lastTested = "Noch nicht getestet";
    private string? _validationMessage;
    private string _projectsSummary = "";
    private string _projectsDetail = "";

    public ConnectionsViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        _reminder = ReminderOptions[2];
        SaveCommand = new RelayCommand(() => Save(), () => IsDirty);
        DiscardCommand = new RelayCommand(Discard, () => IsDirty);
        RemoveCommand = new RelayCommand(Remove, () => _selectedItem is not null);
        NewCommand = new RelayCommand(() =>
        {
            if (ConfirmLeave())
            {
                _ctx.Navigate("wizard", null);
            }
        });
        ReplacePatCommand = new RelayCommand(() =>
        {
            IsReplacingPat = true;
            NewPat = "";
        });
        CreatePatCommand = new RelayCommand(() => _ctx.Dialogs.OpenUrl(TokenUrl), () => Uri.TryCreate(CollectionUrl, UriKind.Absolute, out _));
        TestCommand = new AsyncCommand(TestAsync, () => _selectedItem is not null, ex => _ctx.Dialogs.Error("Verbindungstest", ex.Message));
        DiscoverCommand = new RelayCommand(() => GoTo("wizard"), () => _selectedItem is not null);
        RescanCommand = new RelayCommand(() => GoTo("rescan"), () => _selectedItem is not null && _original.Projects.Count > 0);
        Reload();
    }

    public static IReadOnlyList<ReminderOption> ReminderOptions { get; } =
    [
        new(0, "keine"),
        new(3, "3 Tage vorher"),
        new(7, "7 Tage vorher"),
        new(14, "14 Tage vorher"),
        new(30, "30 Tage vorher"),
    ];

    public ObservableCollection<ConnectionListItem> Items { get; } = [];

    public ObservableCollection<TestArea> TestResults { get; } = [];

    public ObservableCollection<RightsRow> Scope { get; } = [];

    public const string ScopeFootnote = "Schreiben wird nur für Work Items geprüft (Probe ohne Anlegen); für Code, Pipelines, Test und Iterationen ist das ohne echtes Schreiben nicht möglich (n/a).";

    public string ScopeFootnoteText => ScopeFootnote;

    public ObservableCollection<DefaultProjectOption> DefaultProjectOptions { get; } = [];

    public RelayCommand SaveCommand { get; }

    public RelayCommand DiscardCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand NewCommand { get; }

    public RelayCommand ReplacePatCommand { get; }

    public RelayCommand CreatePatCommand { get; }

    public AsyncCommand TestCommand { get; }

    public RelayCommand DiscoverCommand { get; }

    public RelayCommand RescanCommand { get; }

    public string PageHintText => PageHint;

    public bool HasSelection => _selectedItem is not null;

    public ConnectionListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value == _selectedItem || value is null)
            {
                return;
            }

            if (!ConfirmLeave())
            {
                OnPropertyChanged();
                return;
            }

            Select(value.Id);
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                DiscardCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ServerType ServerType { get => _serverType; set => Edit(ref _serverType, value); }

    public bool IsServices { get => ServerType == ServerType.Services; set { if (value) { ServerType = ServerType.Services; OnPropertyChanged(nameof(IsServer)); } } }

    public bool IsServer { get => ServerType == ServerType.Server; set { if (value) { ServerType = ServerType.Server; OnPropertyChanged(nameof(IsServices)); } } }

    public string Name { get => _name; set => Edit(ref _name, value); }

    public string NameHint => "Name der Verbindung in der Liste; die KI spricht Projekte über ihren Kurznamen an, z. B. „offene Bugs in " + (_original.Projects.FirstOrDefault()?.Alias ?? "…") + "“.";

    public const string RenameWarning = "Prompts mit dem alten Kurznamen funktionieren danach nicht mehr.";

    public ObservableCollection<ShortNameItem> ShortNames { get; } = [];

    internal string SuggestFor(ProjectEntry p) =>
        ShortName.Suggest(p.Name, _ctx.Connections.AllProjects().Where(x => x.Project.Key != p.Key).Select(x => x.Alias));

    /// <summary>Live check against the rules and all other projects.</summary>
    internal string? CheckShortName(ShortNameItem item)
    {
        if (!item.IsChanged)
        {
            return null;
        }

        if (ShortName.Validate(item.Value) is { } invalid)
        {
            return invalid;
        }

        var name = ShortName.Normalize(item.Value);
        var others = _ctx.Connections.AllProjects().Where(p => p.Project.Key != item.Key).Select(p => p.Alias)
            .Concat(ShortNames.Where(o => !ReferenceEquals(o, item)).Select(o => o.Value.Trim()));
        return others.Any(o => o.Equals(name, StringComparison.OrdinalIgnoreCase)) ? $"Kurzname „{name}“ wird bereits verwendet" : null;
    }

    internal void ShortNameEdited()
    {
        if (!_loading)
        {
            IsDirty = true;
        }
    }

    public string CollectionUrl
    {
        get => _collectionUrl;
        set
        {
            if (Edit(ref _collectionUrl, value))
            {
                CreatePatCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewPat { get => _newPat; set => Edit(ref _newPat, value); }

    public bool HasStoredPat { get => _hasStoredPat; private set => Set(ref _hasStoredPat, value); }

    public bool IsReplacingPat { get => _isReplacingPat; private set => Set(ref _isReplacingPat, value); }

    public string PatMaskText => PatMask;

    public DateTime? PatExpires { get => _patExpires; set => Edit(ref _patExpires, value); }

    public ReminderOption Reminder { get => _reminder; set => Edit(ref _reminder, value); }

    /// <summary>Standardprojekt across all connections.</summary>
    public DefaultProjectOption? DefaultProject
    {
        get => _defaultProject;
        set
        {
            if (Set(ref _defaultProject, value) && !_loading && value is not null)
            {
                _ctx.Connections.SetDefaultProject(value.ConnectionId, value.ProjectKey);
                _ctx.NotifyConfigurationChanged();
            }
        }
    }

    public string ProjectsSummary { get => _projectsSummary; private set => Set(ref _projectsSummary, value); }

    public string ProjectsDetail { get => _projectsDetail; private set => Set(ref _projectsDetail, value); }

    public string LastTested { get => _lastTested; private set => Set(ref _lastTested, value); }

    public string? ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }

    public string TokenUrl => $"{CollectionUrl.Trim().TrimEnd('/')}/_usersSettings/tokens";

    public void Reload(string? selectId = null)
    {
        selectId ??= _selectedItem?.Id;
        Items.Clear();
        foreach (var c in _ctx.SafeConnections())
        {
            var s = _ctx.StatusOf(c);
            var text = s.Level switch
            {
                StatusLevel.Ok => "OK",
                StatusLevel.Warning => ExpiryClassifier.DaysLeft(c, _ctx.Today) is { } d ? (d == 1 ? "1 Tag" : $"{d} Tage") : s.StatusText,
                _ => s.ExpiryText == "abgelaufen" ? "abgelaufen" : s.StatusText,
            };
            var detail = c.Projects.Count == 1 ? "1 Projekt" : $"{c.Projects.Count} Projekte";
            Items.Add(new ConnectionListItem(c.Id, c.Name, detail, new Pill(text, s.Level),
                c.Projects.Select(p => new ProjectListItem(p.Alias, p.IsDefault, p.Visible)).ToList()));
        }

        _loading = true;
        DefaultProjectOptions.Clear();
        foreach (var p in _ctx.Connections.AllProjects())
        {
            DefaultProjectOptions.Add(new DefaultProjectOption(p.Connection.Id, p.Project.Key, $"{p.Alias} ({p.Connection.Name})"));
        }

        var def = _ctx.Connections.AllProjects().FirstOrDefault(p => p.Project.IsDefault);
        _defaultProject = DefaultProjectOptions.FirstOrDefault(o => def is not null && o.ProjectKey == def.Project.Key);
        OnPropertyChanged(nameof(DefaultProject));
        _loading = false;

        var target = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.FirstOrDefault();
        _selectedItem = null;
        if (target is null)
        {
            Load(null);
            return;
        }

        Select(target.Id);
    }

    public void Select(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return;
        }

        _selectedItem = item;
        OnPropertyChanged(nameof(SelectedItem));
        Load(_ctx.Connections.Get(id));
    }

    public bool ConfirmLeave()
    {
        if (!IsDirty)
        {
            return true;
        }

        return _ctx.Dialogs.AskSaveChanges($"Änderungen an „{Name}“ speichern?") switch
        {
            true => Save(),
            false => DiscardAndContinue(),
            null => false,
        };
    }

    public bool Save()
    {
        ValidationMessage = null;
        if (_selectedItem is null)
        {
            return false;
        }

        var c = (_ctx.Connections.Get(_original.Id) ?? _original).Clone();
        c.Name = Name;
        c.ServerType = ServerType;
        c.CollectionUrl = CollectionUrl;
        c.PatExpires = PatExpires is { } d ? DateOnly.FromDateTime(d) : null;
        c.ReminderDays = Reminder.Days;
        var pat = string.IsNullOrWhiteSpace(NewPat) ? null : NewPat.Trim();

        if (ShortNames.FirstOrDefault(i => i.Error is not null) is { } bad)
        {
            ValidationMessage = bad.Error;
            return false;
        }

        var renamed = ShortNames.Where(i => i.IsChanged).ToList();
        if (renamed.Count > 0)
        {
            var list = string.Join(", ", renamed.Select(i => $"{i.Original} → {ShortName.Normalize(i.Value)}"));
            if (!_ctx.Dialogs.Confirm("Kurzname ändern", $"{list}. {RenameWarning}"))
            {
                return false;
            }

            foreach (var i in renamed)
            {
                if (c.Projects.FirstOrDefault(p => p.Key == i.Key) is { } project)
                {
                    project.Alias = i.Value.Trim(); // Key stays, so permission settings stay attached
                }
            }
        }

        try
        {
            var saved = _ctx.Connections.Save(c, pat);
            NewPat = "";
            IsDirty = false;
            _ctx.NotifyConfigurationChanged();
            Reload(saved.Id);
            return true;
        }
        catch (ConnectionValidationException ex)
        {
            ValidationMessage = ex.Message;
            return false;
        }
    }

    private void GoTo(string page)
    {
        if (_selectedItem is not null && ConfirmLeave())
        {
            _ctx.Navigate(page, _selectedItem.Id);
        }
    }

    private bool DiscardAndContinue()
    {
        IsDirty = false;
        return true;
    }

    private void Discard() => Load(_ctx.Connections.Get(_original.Id));

    private void Remove()
    {
        if (_selectedItem is null)
        {
            return;
        }

        var projects = _original.Projects.Count;
        if (!_ctx.Dialogs.Confirm("Verbindung entfernen",
                $"Verbindung „{_original.Name}“ mit {projects} Projekt(en) und ihrem PAT entfernen? Die Einstellungen der Projekte gehen verloren."))
        {
            return;
        }

        _ctx.Connections.Remove(_original.Id);
        _ctx.Permissions.Update(s =>
        {
            s.Connections.Remove(_original.Id);
            foreach (var key in s.Projects.Keys.Where(k => k.StartsWith(_original.Id + "/", StringComparison.Ordinal)).ToList())
            {
                s.Projects.Remove(key);
            }
        });
        IsDirty = false;
        _ctx.NotifyConfigurationChanged();
        _selectedItem = null;
        Reload();
    }

    private void Load(Connection? c)
    {
        _loading = true;
        _original = c?.Clone() ?? new Connection();
        ServerType = _original.ServerType;
        Name = _original.Name;
        CollectionUrl = _original.CollectionUrl;
        NewPat = "";
        PatExpires = _original.PatExpires?.ToDateTime(TimeOnly.MinValue);
        Reminder = ReminderOptions.FirstOrDefault(o => o.Days == _original.ReminderDays) ?? new ReminderOption(_original.ReminderDays, $"{_original.ReminderDays} Tage vorher");
        HasStoredPat = c is not null && _ctx.Connections.HasPat(c.Id);
        IsReplacingPat = !HasStoredPat;

        var n = _original.Projects.Count;
        ProjectsSummary = n == 1 ? "Projekte: 1 eingerichtet" : $"Projekte: {n} eingerichtet";
        var names = string.Join(", ", _original.Projects.Select(p => p.IsDefault ? p.Alias + " (Standard)" : p.Visible ? p.Alias : p.Alias + " (nicht sichtbar)"));
        ProjectsDetail = n == 0
            ? "Noch keine – „Projekte erkennen“ liest alle Projekte dieses PAT."
            : names + (_original.LastDiscovery is { } ld ? $" · zuletzt erkannt am {ld.ToLocalTime():dd.MM.yyyy}" : "");

        ShortNames.Clear();
        foreach (var p in _original.Projects)
        {
            ShortNames.Add(new ShortNameItem(this, p));
        }

        Scope.Clear();
        foreach (var row in RightPills.Rows(_original.PatScope))
        {
            Scope.Add(row);
        }

        TestResults.Clear();
        LastTested = _original.LastTested is { } t ? $"Letzter Test: {t.ToLocalTime():dd.MM.yyyy HH:mm}" : "Noch nicht getestet";
        ValidationMessage = null;
        _loading = false;
        IsDirty = false;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsServices));
        OnPropertyChanged(nameof(IsServer));
        OnPropertyChanged(nameof(NameHint));
        RemoveCommand.RaiseCanExecuteChanged();
        TestCommand.RaiseCanExecuteChanged();
        DiscoverCommand.RaiseCanExecuteChanged();
        RescanCommand.RaiseCanExecuteChanged();
    }

    private bool Edit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name))
        {
            return false;
        }

        if (!_loading)
        {
            IsDirty = true;
        }

        return true;
    }

    private async Task TestAsync()
    {
        TestResults.Clear();
        var c = _original.Clone();
        c.CollectionUrl = CollectionUrl.Trim().TrimEnd('/');
        c.PatExpires = PatExpires is { } d ? DateOnly.FromDateTime(d) : null;
        c.ReminderDays = Reminder.Days;
        var pat = !string.IsNullOrWhiteSpace(NewPat) ? NewPat.Trim() : HasStoredPat ? _ctx.Connections.GetPat(c) : null;
        if (pat is null)
        {
            ValidationMessage = "Personal Access Token fehlt.";
            return;
        }

        var result = await _ctx.Tester.TestAsync(c, pat);
        foreach (var a in result.Areas)
        {
            TestResults.Add(a);
        }

        Scope.Clear();
        foreach (var row in RightPills.Rows(result.PatScope))
        {
            Scope.Add(row);
        }

        LastTested = $"Letzter Test: heute {result.TestedAt.ToLocalTime():HH:mm}";
        if (string.IsNullOrWhiteSpace(NewPat) && _ctx.Connections.Get(_original.Id) is { } saved)
        {
            ConnectionTester.Apply(_ctx.Connections, saved, result);
            _ctx.NotifyConfigurationChanged();
        }
    }
}
