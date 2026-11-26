using System.Collections.ObjectModel;
using AzdoMcp.Core;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

public enum PermissionLevel
{
    Group,
    Tool,
    Connection,
    Project,
}

/// <summary>Schreibschalter of one project, shared by all its rows.</summary>
public sealed class WriteSwitch(PermissionsViewModel owner, ProjectRef project) : ObservableObject
{
    private bool _allowWrite = project.Project.AllowWrite;

    public string ConnectionId => project.Connection.Id;

    public string ProjectKey => project.Project.Key;

    public bool CanWrite => project.Effective[AccessArea.WorkItems].Write != Check.No;

    public bool AllowWrite
    {
        get => _allowWrite;
        set
        {
            if (CanWrite && Set(ref _allowWrite, value))
            {
                owner.Changed();
            }
        }
    }

    public bool IsChanged => _allowWrite != project.Project.AllowWrite;
}

/// <summary>One row of the "Tools &amp; Rechte" tree.</summary>
public sealed class PermissionNode : ObservableObject
{
    private readonly PermissionsViewModel _owner;
    private bool _isExpanded;

    internal PermissionNode(PermissionsViewModel owner, PermissionLevel level, ToolGroup group, string? tool, Connection? connection, ProjectRef? project, string title, string? subtitle, int depth)
    {
        _owner = owner;
        Level = level;
        Group = group;
        Tool = tool;
        Connection = connection;
        Project = project;
        Title = title;
        Subtitle = subtitle;
        Depth = depth;
        ResetCommand = new RelayCommand(() => Own = null, () => Own is not null);
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded, () => HasChildren);
    }

    public PermissionLevel Level { get; }

    public string LevelText => Level switch
    {
        PermissionLevel.Group => "Gruppe",
        PermissionLevel.Tool => "Tool",
        PermissionLevel.Connection => "Verbindung",
        _ => "Projekt",
    };

    public ToolGroup Group { get; }

    public string? Tool { get; }

    public Connection? Connection { get; }

    public ProjectRef? Project { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public int Depth { get; }

    public double IndentWidth => Depth * 22;

    public List<PermissionNode> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpanderGlyph));
                _owner.RebuildVisible();
            }
        }
    }

    public string ExpanderGlyph => !HasChildren ? "" : IsExpanded ? "▾" : "▸";

    public RelayCommand ResetCommand { get; }

    public RelayCommand ToggleCommand { get; }

    public WriteSwitch? WriteSwitch { get; internal set; }

    public bool ShowWriteSwitch => WriteSwitch is not null;

    /// <summary>Mode set on exactly this row; null = inherited.</summary>
    public PermissionMode? Own
    {
        get => _owner.GetOwn(this);
        set
        {
            _owner.SetOwn(this, value);
            ResetCommand.RaiseCanExecuteChanged();
        }
    }

    public (PermissionMode Mode, ModeSource Source) Resolved => _owner.Resolve(this);

    public PermissionMode Mode => Resolved.Mode;

    public bool IsInherited => Own is null;

    public bool IsAllowed { get => Mode == PermissionMode.Allowed; set { if (value) { Own = PermissionMode.Allowed; } } }

    public bool IsAsk { get => Mode == PermissionMode.Ask; set { if (value) { Own = PermissionMode.Ask; } } }

    public bool IsBlocked { get => Mode == PermissionMode.Blocked; set { if (value) { Own = PermissionMode.Blocked; } } }

    public string Hint => _owner.HintFor(this);

    public bool HintIsException => Own is not null && Level != PermissionLevel.Group;

    public Pill? Right => _owner.RightFor(this);

    public string? RightText => _owner.RightTextFor(this);

    public void Refresh()
    {
        foreach (var n in new[] { nameof(Own), nameof(Mode), nameof(IsInherited), nameof(IsAllowed), nameof(IsAsk), nameof(IsBlocked), nameof(Hint), nameof(HintIsException), nameof(Right), nameof(RightText) })
        {
            OnPropertyChanged(n);
        }

        ResetCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Page "Tools &amp; Rechte": Gruppe → Tool → Verbindung → Projekt.</summary>
public sealed class PermissionsViewModel : ObservableObject
{
    public const string Subtitle = "Ebenen: Gruppe → Tool → Verbindung → Projekt. Die tiefste gesetzte Ebene gilt; Schreibschalter und effektives Recht² begrenzen immer.";
    public const string Footnote1 = "Erlaubt = KI führt sofort aus · Nachfragen = Freigabe-Fenster mit Erlauben/Ablehnen, ohne Antwort nach 2 min abgelehnt · Gesperrt = Tool wird nicht angeboten. Blasser Modus = geerbt von der Ebene darüber.";
    public const string Footnote2 = "Effektives Recht = der Wert, den die Gruppe braucht (Lesen ihres Bereichs, bei «Work Items schreiben» Work Items Schreiben): PAT-Scope kombiniert mit deinen Projektrechten in Azure DevOps. Grün = ja · Rot = nein (sperrt) · Grau = n/a (nicht prüfbar) oder ? (noch nicht geprüft). Ein Modus hier kann es nur einschränken.";
    public const string Footnote3 = "PAT-Scope = die beim Erstellen des PAT freigegebenen Bereiche; gilt für alle Projekte der Verbindung.";

    private static readonly ToolGroup[] Groups = [ToolGroup.WorkItemsRead, ToolGroup.WorkItemsWrite, ToolGroup.Git, ToolGroup.Pipelines, ToolGroup.Test, ToolGroup.Planning];

    private readonly ManagerContext _ctx;
    private PermissionSettings _draft = new();
    private IReadOnlyList<Connection> _connections = [];
    private readonly List<PermissionNode> _roots = [];
    private readonly Dictionary<string, WriteSwitch> _switches = [];
    private bool _isDirty;
    private bool _loading;

    public PermissionsViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        SaveCommand = new RelayCommand(() => Save(), () => IsDirty);
        Reload();
    }

    public ObservableCollection<PermissionNode> Rows { get; } = [];

    public IReadOnlyList<PermissionNode> Roots => _roots;

    public RelayCommand SaveCommand { get; }

    public string SubtitleText => Subtitle;

    public string Footnote1Text => Footnote1;

    public string Footnote2Text => Footnote2;

    public string Footnote3Text => Footnote3;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Reload()
    {
        _loading = true;
        try
        {
            _draft = _ctx.Permissions.Get();
        }
        catch (ConfigurationException ex)
        {
            _ctx.Dialogs.Error("Rechte", ex.Message);
            _draft = new PermissionSettings();
        }

        _connections = _ctx.SafeConnections();
        var expanded = AllNodes().Where(n => n.IsExpanded).Select(Key).ToHashSet();
        _roots.Clear();
        _switches.Clear();
        foreach (var c in _connections)
        {
            foreach (var p in c.Projects)
            {
                var r = new ProjectRef(c, p);
                _switches[r.PermissionKey] = new WriteSwitch(this, r);
            }
        }

        foreach (var g in Groups)
        {
            var group = new PermissionNode(this, PermissionLevel.Group, g, null, null, null, g.Display(), null, 0);
            AddConnections(group, g, null, 1);
            foreach (var t in ToolCatalog.All.Where(t => t.Group == g))
            {
                var tool = new PermissionNode(this, PermissionLevel.Tool, g, t.Name, null, null, t.Name, null, 1);
                AddConnections(tool, g, t.Name, 2);
                group.Children.Add(tool);
            }

            _roots.Add(group);
        }

        foreach (var n in AllNodes().Where(n => expanded.Contains(Key(n))))
        {
            n.IsExpanded = true;
        }

        _loading = false;
        IsDirty = false;
        RebuildVisible();
    }

    private void AddConnections(PermissionNode parent, ToolGroup g, string? tool, int depth)
    {
        foreach (var c in _connections)
        {
            var title = tool is null ? $"{c.Name} · alle Tools" : c.Name;
            var conn = new PermissionNode(this, PermissionLevel.Connection, g, tool, c, null, title, "/ " + c.Host, depth);
            foreach (var p in c.Projects)
            {
                var r = new ProjectRef(c, p);
                var node = new PermissionNode(this, PermissionLevel.Project, g, tool, c, r, p.Alias, p.Visible ? null : "nicht mehr sichtbar", depth + 1);
                if (g == ToolGroup.WorkItemsWrite)
                {
                    node.WriteSwitch = _switches[r.PermissionKey];
                }

                conn.Children.Add(node);
            }

            parent.Children.Add(conn);
        }
    }

    public void RebuildVisible()
    {
        if (_loading)
        {
            return;
        }

        Rows.Clear();
        void Walk(PermissionNode n)
        {
            Rows.Add(n);
            if (n.IsExpanded)
            {
                foreach (var c in n.Children)
                {
                    Walk(c);
                }
            }
        }

        foreach (var r in _roots)
        {
            Walk(r);
        }
    }

    public IEnumerable<PermissionNode> AllNodes()
    {
        var stack = new Stack<PermissionNode>(_roots);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            foreach (var c in n.Children)
            {
                stack.Push(c);
            }
        }
    }

    /// <summary>Finds a row (tests, deep links).</summary>
    public PermissionNode? Find(ToolGroup group, string? tool = null, string? connectionName = null, string? projectAlias = null) =>
        AllNodes().FirstOrDefault(n => n.Group == group && n.Tool == tool
                                       && (connectionName is null ? n.Connection is null : n.Connection?.Name == connectionName)
                                       && (projectAlias is null ? n.Project is null : n.Project?.Alias == projectAlias));

    internal PermissionMode? GetOwn(PermissionNode n)
    {
        var slot = Slot(n, create: false);
        if (slot is null)
        {
            return null;
        }

        return n.Tool is null
            ? slot.Groups.TryGetValue(n.Group, out var g) ? g : null
            : slot.Tools.TryGetValue(n.Tool, out var t) ? t : null;
    }

    internal void SetOwn(PermissionNode n, PermissionMode? mode)
    {
        var slot = Slot(n, create: true)!;
        if (n.Tool is null)
        {
            if (mode is { } m)
            {
                slot.Groups[n.Group] = m;
            }
            else
            {
                slot.Groups.Remove(n.Group);
            }
        }
        else if (mode is { } m)
        {
            slot.Tools[n.Tool] = m;
        }
        else
        {
            slot.Tools.Remove(n.Tool);
        }

        Changed();
    }

    internal void Changed()
    {
        if (_loading)
        {
            return;
        }

        IsDirty = true;
        foreach (var n in AllNodes())
        {
            n.Refresh();
        }
    }

    internal (PermissionMode, ModeSource) Resolve(PermissionNode n)
    {
        var connId = n.Connection?.Id;
        var projKey = n.Project?.PermissionKey;
        if (n.Tool is null)
        {
            return PermissionEvaluator.ResolveGroup(_draft, n.Group, connId, projKey);
        }

        return PermissionEvaluator.Resolve(_draft, ToolCatalog.Find(n.Tool)!, connId, projKey);
    }

    internal string HintFor(PermissionNode n)
    {
        if (n.Level == PermissionLevel.Group)
        {
            var count = ToolCatalog.All.Count(t => t.Group == n.Group);
            return count == 1 ? "1 Tool" : $"{count} Tools";
        }

        if (n.Level == PermissionLevel.Project && n.Project is { } denied && NeededFor(denied, n.Group) == Check.No)
        {
            var reason = DeniedReason(denied, n.Group);
            return n.Own is null ? reason : "Ausnahme · " + reason;
        }

        if (n.Own is not null)
        {
            if (n.Project is { } p && n.Group == ToolGroup.WorkItemsWrite && !p.Project.AllowWrite)
            {
                return "Ausnahme · Schreibschalter aus";
            }

            return "Ausnahme";
        }

        return n.Resolved.Source switch
        {
            ModeSource.Group => "wie Gruppe",
            ModeSource.Tool => "wie Tool",
            ModeSource.ConnectionGroup or ModeSource.ConnectionTool => "wie Verbindung",
            ModeSource.ProjectGroup => "wie Projekt (alle Tools)",
            ModeSource.Default => "wie Gruppe",
            _ => "",
        };
    }

    internal Pill? RightFor(PermissionNode n)
    {
        var area = Access.AreaOf(n.Group);
        return n.Level switch
        {
            PermissionLevel.Project when n.Project is { } p => p.Project.Visible ? RightPills.Cell(NeededFor(p, n.Group), Label(n.Group)) : new Pill("nicht sichtbar", StatusLevel.Error),
            PermissionLevel.Connection when n.Connection is { } c => RightPills.Cell(Needed(c.PatScope?[area] ?? AreaAccess.Unknown, n.Group), Label(n.Group)),
            _ => null,
        };
    }

    /// <summary>The value a group needs: Lesen of its area, or Work Items Schreiben for write groups.</summary>
    internal static Check? Needed(AreaAccess access, ToolGroup group) => group == ToolGroup.WorkItemsWrite ? access.Write : access.Read;

    internal static Check? NeededFor(ProjectRef p, ToolGroup group) => Needed(p.Effective[Access.AreaOf(group)], group);

    private static string Label(ToolGroup group) => $"{Access.AreaOf(group).Display()} {(group == ToolGroup.WorkItemsWrite ? "Schreiben" : "Lesen")}";

    /// <summary>Why a needed value is "nein": the PAT or the project right.</summary>
    internal static string DeniedReason(ProjectRef p, ToolGroup group)
    {
        var area = Access.AreaOf(group);
        var write = group == ToolGroup.WorkItemsWrite;
        var pat = Needed(p.Connection.PatScope?[area] ?? AreaAccess.Unknown, group) == Check.No;
        return (pat, write) switch
        {
            (true, true) => "PAT ohne Schreibrecht",
            (true, false) => "PAT ohne Zugriff",
            (false, true) => "kein Projektrecht zum Bearbeiten",
            _ => "kein Projektrecht",
        };
    }

    internal string? RightTextFor(PermissionNode n)
    {
        var area = Access.AreaOf(n.Group);
        if (n.Level == PermissionLevel.Connection)
        {
            return "PAT³:";
        }

        if (n.Level != PermissionLevel.Group)
        {
            return null;
        }

        var projects = _connections.SelectMany(c => c.Projects.Select(p => new ProjectRef(c, p))).ToList();
        if (projects.Count == 0)
        {
            return null;
        }

        var none = projects.Count(p => NeededFor(p, n.Group) == Check.No);
        var readable = projects.Count - none;
        return none > 0 ? $"„nein“ in {none} Projekt(en)" : $"{readable} Projekt(e) {(n.Group == ToolGroup.WorkItemsWrite ? "erreichbar" : "lesbar")}";
    }

    public bool Save()
    {
        try
        {
            _ctx.Permissions.Save(_draft);
            foreach (var group in _switches.Values.Where(s => s.IsChanged).GroupBy(s => s.ConnectionId))
            {
                _ctx.Connections.Update(group.Key, c =>
                {
                    foreach (var s in group)
                    {
                        var p = c.Projects.FirstOrDefault(x => x.Key == s.ProjectKey);
                        if (p is not null)
                        {
                            p.AllowWrite = s.AllowWrite;
                        }
                    }
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or ConnectionValidationException)
        {
            _ctx.Dialogs.Error("Speichern fehlgeschlagen", ex.Message);
            return false;
        }

        IsDirty = false;
        _ctx.NotifyConfigurationChanged();
        Reload();
        return true;
    }

    public bool ConfirmLeave()
    {
        if (!IsDirty)
        {
            return true;
        }

        return _ctx.Dialogs.AskSaveChanges("Geänderte Rechte speichern?") switch
        {
            true => Save(),
            false => Discard(),
            null => false,
        };
    }

    private bool Discard()
    {
        Reload();
        return true;
    }

    private ScopeModes? Slot(PermissionNode n, bool create)
    {
        switch (n.Level)
        {
            case PermissionLevel.Project:
                return create ? _draft.ForProject(n.Project!.PermissionKey) : _draft.Projects.GetValueOrDefault(n.Project!.PermissionKey);
            case PermissionLevel.Connection:
                return create ? _draft.ForConnection(n.Connection!.Id) : _draft.Connections.GetValueOrDefault(n.Connection!.Id);
            default:
                // Global level is the settings object itself.
                var g = new ScopeModes { Groups = _draft.Groups, Tools = _draft.Tools };
                return g;
        }
    }

    private static string Key(PermissionNode n) => $"{n.Level}|{n.Group}|{n.Tool}|{n.Connection?.Id}|{n.Project?.PermissionKey}";
}
