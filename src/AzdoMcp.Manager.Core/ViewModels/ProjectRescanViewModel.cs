using System.Collections.ObjectModel;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Discovery;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

public sealed class RescanNewRow(DiscoveredProject project) : ObservableObject
{
    private bool _isSelected = DiscoveryRow.HasAnyRead(project);

    public DiscoveredProject Project { get; } = project;

    public string Name => Project.Name;

    public IReadOnlyList<RightsRow> Rights { get; } = RightPills.Rows(project.Effective);

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>"Work Items Schreiben" with old → new.</summary>
public sealed record RescanChange(string Area, Pill Old, Pill New);

public sealed record RescanChangedRow(string Name, IReadOnlyList<RescanChange> Changes, string Note);

public sealed record RescanMissingRow(string Alias, string ProjectKey);

/// <summary>Page "Projekte neu einlesen": diff against the stored projects.</summary>
public sealed class ProjectRescanViewModel : ObservableObject
{
    public const string ApplyNote = "Was «Übernehmen» ändert: neue Projekte mit Voreinstellung (Lesen = Erlaubt, Schreiben = Nachfragen, Schreibschalter aus). Mehr Rechte schalten nie automatisch Schreiben frei; weniger Rechte sperren sofort.";
    public const string MissingNote = "PAT oder Projektrechte geändert, oder Projekt gelöscht. Tools liefern dort «kein Zugriff», bis du es entfernst.";

    private readonly ManagerContext _ctx;
    private readonly SetupWizardViewModel _wizard;
    private Connection? _connection;
    private string _pat = "";
    private DiscoveryResult? _result;
    private bool _isBusy;
    private string? _message;
    private string _summary = "";
    private string _unchangedText = "";

    public ProjectRescanViewModel(ManagerContext ctx, SetupWizardViewModel wizard)
    {
        _ctx = ctx;
        _wizard = wizard;
        ApplyCommand = new AsyncCommand(ApplyAsync, () => _result is not null && !IsBusy, ex => Message = ex.Message);
        CancelCommand = new RelayCommand(() => _ctx.Navigate("connections", _connection?.Id));
        RemoveCommand = new RelayCommand(p => Remove(p as RescanMissingRow));
    }

    public ObservableCollection<RescanNewRow> NewRows { get; } = [];

    public ObservableCollection<RescanMissingRow> MissingRows { get; } = [];

    public ObservableCollection<RescanChangedRow> ChangedRows { get; } = [];

    public AsyncCommand ApplyCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public string Title => $"Projekte neu einlesen · {_connection?.Name}";

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public string UnchangedText { get => _unchangedText; private set => Set(ref _unchangedText, value); }

    public string ApplyNoteText => ApplyNote;

    public string MissingNoteText => MissingNote;

    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { ApplyCommand.RaiseCanExecuteChanged(); } } }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public async Task StartAsync(string connectionId)
    {
        NewRows.Clear();
        MissingRows.Clear();
        ChangedRows.Clear();
        _result = null;
        Message = null;
        _connection = _ctx.Connections.Get(connectionId) ?? throw new InvalidOperationException("Verbindung nicht gefunden.");
        OnPropertyChanged(nameof(Title));
        IsBusy = true;
        try
        {
            _pat = _ctx.Connections.GetPat(_connection);
            _result = await _ctx.Discovery.DiscoverAsync(_connection, _pat);
            Fill(DiscoveryMerger.Diff(_connection, _result));
        }
        catch (Exception ex) when (ex is AzdoApiException or SecretUnreadableException)
        {
            Message = SecretScrubber.Scrub(ex.Message, _pat);
        }
        finally
        {
            IsBusy = false;
            ApplyCommand.RaiseCanExecuteChanged();
        }
    }

    private void Fill(DiscoveryDiff diff)
    {
        foreach (var n in diff.New)
        {
            NewRows.Add(new RescanNewRow(n));
        }

        foreach (var m in diff.Missing)
        {
            MissingRows.Add(new RescanMissingRow(m.Alias, m.Key));
        }

        foreach (var c in diff.Changed)
        {
            var changes = c.Changes.Select(x => new RescanChange($"{x.Area.Display()} {x.Column.Display()}", RightPills.Cell(x.Old), RightPills.Cell(x.New))).ToList();
            var notes = new List<string>();
            if (c.Changes.Any(x => x.Area == AccessArea.WorkItems && x.Column == AccessColumn.Write && x.Old == Check.No && !c.Project.AllowWrite))
            {
                notes.Add("Schreibschalter bleibt aus – bewusst freischalten");
            }

            notes.AddRange(c.Changes.Where(x => x.New == Check.No).Select(x => x.Column == AccessColumn.Read
                ? $"{x.Area.Display()}-Tools werden für {c.Project.Alias} gesperrt"
                : $"Schreib-Tools werden für {c.Project.Alias} gesperrt"));
            if (!c.Project.Visible)
            {
                notes.Add("wieder sichtbar");
            }

            ChangedRows.Add(new RescanChangedRow(c.Project.Alias, changes, string.Join(" · ", notes)));
        }

        UnchangedText = diff.Unchanged.Count == 0 ? "" : "Unverändert: " + string.Join(", ", diff.Unchanged.Select(u => u.Alias));
        var last = _connection!.LastDiscovery is { } ld ? $"Vergleich mit der letzten Erkennung vom {ld.ToLocalTime():dd.MM.yyyy}" : "Erste Erkennung für diese Verbindung";
        Summary = $"{last} · {diff.ChangeCount} Änderung(en) gefunden";
    }

    private async Task ApplyAsync()
    {
        if (_connection is null || _result is null)
        {
            return;
        }

        var selected = NewRows.Where(r => r.IsSelected).Select(r => r.Project.AzdoId).ToList();
        if (selected.Count > 0)
        {
            // New projects go through the wizard's project steps; the wizard applies everything.
            _wizard.StartFromRescan(_ctx.Connections.Get(_connection.Id) ?? _connection, _pat, _result, selected);
            _ctx.Navigate("wizard-continue", _connection.Id);
            return;
        }

        var merged = DiscoveryMerger.Apply(_ctx.Connections.Get(_connection.Id) ?? _connection, _result, new Dictionary<string, ProjectChoice>());
        _ctx.Connections.Save(merged.Connection, null);
        _ctx.NotifyConfigurationChanged();
        await Task.CompletedTask;
        _ctx.Navigate("connections", _connection.Id);
    }

    private void Remove(RescanMissingRow? row)
    {
        if (row is null || _connection is null ||
            !_ctx.Dialogs.Confirm("Projekt entfernen", $"Projekt „{row.Alias}“ und seine Einstellungen entfernen?"))
        {
            return;
        }

        _ctx.Connections.Update(_connection.Id, c => c.Projects.RemoveAll(p => p.Key == row.ProjectKey));
        _ctx.Permissions.Update(s => s.Projects.Remove($"{_connection.Id}/{row.ProjectKey}"));
        MissingRows.Remove(row);
        _ctx.NotifyConfigurationChanged();
    }
}
