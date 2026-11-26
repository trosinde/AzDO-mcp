using System.Collections.ObjectModel;
using System.Globalization;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

public sealed record LogRow(AuditRecord Record, string Time, string Client, string Tool, string Connection, string Duration, Pill Result)
{
    public string? RequestId => Record.RequestId;

    public bool IsPending => Record.Outcome == AuditOutcome.Pending;
}

/// <summary>Page "Protokoll" with the "Freigabe nötig" panel.</summary>
public sealed class LogViewModel : ObservableObject
{
    public const string AllConnections = "Alle Verbindungen";
    public const string AllResults = "Alle Ergebnisse";
    public const string PrivacyNote = "PATs und Authorization-Header werden nie protokolliert. Aufbewahrung: 30 Tage, lokal.";

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly ManagerContext _ctx;
    private IReadOnlyList<AuditRecord> _all = [];
    private string _connectionFilter = AllConnections;
    private string _resultFilter = AllResults;
    private string _search = "";
    private LogRow? _selectedRow;
    private ApprovalRequest? _selectedApproval;
    private string _remaining = "";

    public LogViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        ExportCommand = new RelayCommand(Export);
        ApproveCommand = new AsyncCommand(() => RespondAsync(true), () => SelectedApproval is not null);
        DenyCommand = new AsyncCommand(() => RespondAsync(false), () => SelectedApproval is not null);
        Refresh();
    }

    public static IReadOnlyList<string> ResultFilters { get; } = [AllResults, "OK", "Fehler", "wartet auf Freigabe", "abgelehnt", "gesperrt"];

    public ObservableCollection<string> ConnectionFilters { get; } = [AllConnections];

    public ObservableCollection<LogRow> Rows { get; } = [];

    public RelayCommand ExportCommand { get; }

    public AsyncCommand ApproveCommand { get; }

    public AsyncCommand DenyCommand { get; }

    public string PrivacyText => PrivacyNote;

    public string ConnectionFilter { get => _connectionFilter; set { if (Set(ref _connectionFilter, value ?? AllConnections)) { Apply(); } } }

    public string ResultFilter { get => _resultFilter; set { if (Set(ref _resultFilter, value ?? AllResults)) { Apply(); } } }

    public string Search { get => _search; set { if (Set(ref _search, value ?? "")) { Apply(); } } }

    public LogRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value) && value?.RequestId is { } id)
            {
                var pending = _ctx.Host.PendingApprovals.FirstOrDefault(p => p.RequestId == id);
                if (pending is not null)
                {
                    SelectedApproval = pending;
                }
            }
        }
    }

    /// <summary>Request shown in the "Freigabe nötig" panel.</summary>
    public ApprovalRequest? SelectedApproval
    {
        get => _selectedApproval;
        set
        {
            if (Set(ref _selectedApproval, value))
            {
                OnPropertyChanged(nameof(HasApproval));
                OnPropertyChanged(nameof(ApprovalHeadline));
                OnPropertyChanged(nameof(ApprovalClient));
                OnPropertyChanged(nameof(ApprovalChange));
                ApproveCommand.RaiseCanExecuteChanged();
                DenyCommand.RaiseCanExecuteChanged();
                Tick();
            }
        }
    }

    public bool HasApproval => SelectedApproval is not null;

    public string ApprovalHeadline => SelectedApproval is { } r
        ? r.WorkItemId is { } id ? $"Work Item #{id} in {r.Connection} ändern" : $"{r.Tool} in {r.Connection} ausführen"
        : "";

    public string ApprovalClient => SelectedApproval is { } r ? $"{r.Client} möchte" : "";

    public string ApprovalChange => SelectedApproval is { } r
        ? string.Join("\n", r.Summary[(r.Summary.IndexOf(':') + 1)..].Split(", ").Select(s => s.Trim()))
        : "";

    /// <summary>"offen 0:42".</summary>
    public string Remaining { get => _remaining; private set => Set(ref _remaining, value); }

    /// <summary>Most recent first; pending + final records of one approval shown as one row.</summary>
    public static IReadOnlyList<AuditRecord> Collapse(IEnumerable<AuditRecord> records)
    {
        var result = new List<AuditRecord>();
        var seen = new HashSet<string>();
        foreach (var r in records.OrderByDescending(r => r.Time))
        {
            if (r.RequestId is { } id && !seen.Add(id))
            {
                continue;
            }

            result.Add(r);
        }

        return result;
    }

    public void Refresh()
    {
        var now = _ctx.Time.GetUtcNow();
        _all = Collapse(_ctx.Audit.Read(now.AddDays(-30), now.AddMinutes(1)));

        var names = _all.Select(r => r.Connection).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var n in names.Where(n => !ConnectionFilters.Contains(n)))
        {
            ConnectionFilters.Add(n);
        }

        Apply();

        var pending = _ctx.Host.PendingApprovals;
        if (SelectedApproval is null || pending.All(p => p.RequestId != SelectedApproval.RequestId))
        {
            SelectedApproval = pending.FirstOrDefault();
        }

        Tick();
    }

    /// <summary>Updates the countdown; call once per second while visible.</summary>
    public void Tick()
    {
        if (SelectedApproval is not { } r)
        {
            Remaining = "";
            return;
        }

        var left = r.ExpiresAt - _ctx.Time.GetUtcNow();
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        Remaining = $"offen {(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    public IEnumerable<AuditRecord> Filtered() => _all.Where(Matches);

    private bool Matches(AuditRecord r)
    {
        if (ConnectionFilter != AllConnections && !string.Equals(r.Connection, ConnectionFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var resultOk = ResultFilter switch
        {
            AllResults => true,
            "OK" => r.Outcome == AuditOutcome.Ok,
            "Fehler" => r.Outcome == AuditOutcome.Error,
            "wartet auf Freigabe" => r.Outcome == AuditOutcome.Pending,
            "abgelehnt" => r.Outcome == AuditOutcome.Denied,
            "gesperrt" => r.Outcome == AuditOutcome.Blocked,
            _ => true,
        };
        if (!resultOk)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }

        var q = Search.Trim().TrimStart('#');
        return r.Tool.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Client.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (r.WorkItemId?.ToString(CultureInfo.InvariantCulture) == q);
    }

    private void Apply()
    {
        var selected = SelectedRow?.Record;
        Rows.Clear();
        foreach (var r in Filtered())
        {
            Rows.Add(ToRow(r));
        }

        _selectedRow = Rows.FirstOrDefault(x => x.Record == selected);
        OnPropertyChanged(nameof(SelectedRow));
    }

    public static LogRow ToRow(AuditRecord r)
    {
        var level = r.Outcome switch
        {
            AuditOutcome.Ok => StatusLevel.Ok,
            AuditOutcome.Pending => StatusLevel.Warning,
            AuditOutcome.Error => StatusLevel.Error,
            _ => (StatusLevel?)null,
        };
        var duration = r.Outcome is AuditOutcome.Pending or AuditOutcome.Denied or AuditOutcome.Blocked
            ? "–"
            : (r.DurationMs / 1000.0).ToString("0.0", De) + " s";
        return new LogRow(r, r.Time.ToLocalTime().ToString("HH:mm:ss", De), r.Client, r.Tool, r.Connection ?? "–", duration, new Pill(r.Result, level));
    }

    private void Export()
    {
        var path = _ctx.Dialogs.SaveFile("Protokoll exportieren", $"azdo-mcp-protokoll-{_ctx.Today:yyyyMMdd}.csv", "CSV-Datei (*.csv)|*.csv");
        if (path is null)
        {
            return;
        }

        AuditLog.ExportCsv(Filtered().OrderBy(r => r.Time), path);
        _ctx.Dialogs.Info("Export", $"{Rows.Count} Einträge exportiert:\n{path}");
    }

    private async Task RespondAsync(bool approved)
    {
        if (SelectedApproval is not { } r)
        {
            return;
        }

        await _ctx.Host.RespondAsync(r.RequestId, approved);
        SelectedApproval = null;
        Refresh();
    }
}
