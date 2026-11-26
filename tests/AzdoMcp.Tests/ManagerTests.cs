using System.Net;
using System.Text.Json.Nodes;
using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Manager.Core.Clients;
using AzdoMcp.Manager.Core.Ipc;
using AzdoMcp.Manager.Core.Services;
using AzdoMcp.Manager.Core.ViewModels;

namespace AzdoMcp.Tests;

internal sealed class FakeDialogs : IDialogs
{
    public bool ConfirmAnswer { get; set; } = true;

    public bool? SaveAnswer { get; set; } = true;

    public string? SavePath { get; set; }

    public string? Folder { get; set; }

    public List<string> Messages { get; } = [];

    public List<string> OpenedUrls { get; } = [];

    public string? Clipboard { get; private set; }

    public List<string> Confirms { get; } = [];

    public bool Confirm(string title, string message)
    {
        Confirms.Add(message);
        return ConfirmAnswer;
    }

    public bool? AskSaveChanges(string message)
    {
        Messages.Add("ask: " + message);
        return SaveAnswer;
    }

    public void Info(string title, string message) => Messages.Add("info: " + message);

    public void Error(string title, string message) => Messages.Add("error: " + message);

    public string? SaveFile(string title, string defaultName, string filter) => SavePath;

    public string? PickFolder(string title) => Folder;

    public void OpenUrl(string url) => OpenedUrls.Add(url);

    public void OpenFolder(string path)
    {
    }

    public void CopyText(string text) => Clipboard = text;
}

internal sealed class FakeRunKey : IRunKey
{
    public Dictionary<string, string> Values { get; } = [];

    public string? Get(string name) => Values.GetValueOrDefault(name);

    public void Set(string name, string value) => Values[name] = value;

    public void Delete(string name) => Values.Remove(name);
}

/// <summary>Manager context on a temp profile with fakes.</summary>
internal sealed class ManagerHarness : IAsyncDisposable
{
    public ManagerHarness(params string[] connections)
        : this(null, connections)
    {
    }

    /// <param name="http">Handler for connection test and discovery (default: the harness' FakeHttp).</param>
    public ManagerHarness(HttpMessageHandler? http, params string[] connections)
    {
        H = new Harness(connections);
        Profile = Path.Combine(H.Dir.Path, "profile");
        Env = new ManagerEnvironment
        {
            LocalAppData = Path.Combine(Profile, "Local"),
            AppData = Path.Combine(Profile, "Roaming"),
            UserProfile = Profile,
            AppDirectory = Path.Combine(Profile, "Local", "AzdoMcp", "current"),
        };
        Host = new ManagerHost(Hub);
        Host.Start();
        Ctx = new ManagerContext
        {
            Paths = H.Dir.AppPaths,
            Connections = H.Store,
            Permissions = H.Permissions,
            Audit = H.Audit,
            Host = Host,
            Tester = new ConnectionTester(new AzdoClientFactory(http ?? H.Http)),
            Discovery = new AzdoMcp.Core.Discovery.ProjectDiscovery(new AzdoClientFactory(http ?? H.Http)),
            Clients = new ClientSetupService(Env),
            Autostart = new AutostartService(RunKey, Env.ManagerExePath),
            Reminders = new ReminderScheduler(H.Dir.AppPaths),
            Environment = Env,
            Dialogs = Dialogs,
            LegacyCandidates = [Path.Combine(H.Dir.Path, "v1-config.json")],
        };
        Ctx.Navigate = (k, a) => Navigations.Add((k, a));
    }

    public Harness H { get; }

    public string Profile { get; }

    public ManagerEnvironment Env { get; }

    public InMemoryPipeHub Hub { get; } = new();

    public ManagerHost Host { get; }

    public FakeDialogs Dialogs { get; } = new();

    public FakeRunKey RunKey { get; } = new();

    public ManagerContext Ctx { get; }

    /// <summary>Last page requested through ManagerContext.Navigate.</summary>
    public List<(string Key, string? Arg)> Navigations { get; } = [];

    public void Log(string tool, AuditOutcome outcome, string result, bool write = false, string conn = "SHOP", DateTimeOffset? at = null, string? requestId = null) =>
        H.Audit.Append(new AuditRecord { Time = at ?? DateTimeOffset.UtcNow, Client = "Claude Code", Tool = tool, Connection = conn, Outcome = outcome, Result = result, IsWrite = write, DurationMs = 400, RequestId = requestId });

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        H.Dispose();
    }
}

public class OverviewViewModelTests
{
    private static string V1Json(params string[] names)
    {
        var projects = new JsonObject();
        foreach (var n in names)
        {
            projects[n] = new JsonObject { ["baseUrl"] = Fake.Collection + "/" + n, ["pat"] = Fake.Pat };
        }

        return new JsonObject { ["projects"] = projects }.ToJsonString();
    }

    [Fact]
    public async Task Figures_from_log()
    {
        await using var m = new ManagerHarness("SHOP");
        for (var i = 0; i < 9; i++)
        {
            m.Log("get_work_item", AuditOutcome.Ok, "OK");
        }

        for (var i = 0; i < 3; i++)
        {
            m.Log("update_work_item", AuditOutcome.Ok, "OK", write: true);
        }

        m.Log("get_test_runs", AuditOutcome.Error, "403 Scope fehlt");
        m.Log("get_commits", AuditOutcome.Error, "401 PAT abgelaufen");
        m.Log("x", AuditOutcome.Pending, "wartet auf Freigabe", requestId: "r1");
        m.Log("x", AuditOutcome.Denied, "abgelehnt", requestId: "r1");

        var vm = new OverviewViewModel(m.Ctx);
        Assert.Equal(15, vm.CallsToday);
        Assert.Equal(3, vm.WritesToday);
        Assert.Equal("davon 3 schreibend", vm.WritesTodayText);
        Assert.Equal(2, vm.Errors24h);
        Assert.Contains("403", vm.ErrorsHint);
        Assert.Contains("401", vm.ErrorsHint);
    }

    [Fact]
    public async Task Connection_and_project_rows()
    {
        await using var m = new ManagerHarness();
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            PatExpires = m.Ctx.Today.AddDays(4),
            PatScope = Acc.All(Acc.W),
            Projects =
            [
                new ProjectEntry { Name = "SHOP", Alias = "SHOP", IsDefault = true, AllowWrite = true, ProjectRights = Acc.All(Acc.W) },
                new ProjectEntry { Name = "OPS", Alias = "OPS", AllowWrite = true, ProjectRights = new AreaRights { Areas = { [AccessArea.WorkItems] = Acc.Ro, [AccessArea.Code] = Acc.Rn, [AccessArea.Test] = Acc.N } } },
                new ProjectEntry { Name = "Musterlab", Alias = "Musterlab", AllowWrite = false, ProjectRights = Acc.All(Acc.W) },
            ],
        }, Fake.Pat);

        var vm = new OverviewViewModel(m.Ctx);
        Assert.Equal(1, vm.ConnectionCount);
        Assert.Equal("· 3 Projekte", vm.ProjectCountText);
        var conn = vm.Rows[0];
        Assert.False(conn.IsProject);
        Assert.Equal(("in 4 Tagen", "y"), (conn.Expiry!.Text, conn.Expiry.Tone));
        Assert.Equal("erneuern", conn.Status!.Text);

        var shop = vm.Rows.Single(r => r.Title == "SHOP");
        Assert.True(shop.IsDefault);
        Assert.Equal(["gg", "gg", "gg", "gg", "gg"], shop.Rights.Select(r => r.Read.Tone + r.Write.Tone));
        Assert.Equal("schreibt mit Freigabe", shop.StatusText);
        Assert.Contains("Work Items Schreiben: PAT ja · Projektrecht ja → ja", shop.StatusTooltip);

        var ops = vm.Rows.Single(r => r.Title == "OPS");
        Assert.Equal(["gr", "gn", "nn", "rr", "nn"], ops.Rights.Select(r => r.Read.Tone + r.Write.Tone));
        Assert.Equal(["ja", "nein"], [ops.Rights[0].Read.Text, ops.Rights[0].Write.Text]);
        Assert.Equal("nicht prüfbar ohne echtes Schreiben", ops.Rights[1].Write.Tooltip?.Split(" – ")[1]);
        Assert.Equal("nur Lesen · Projektrecht", ops.StatusText);

        Assert.Equal("nur Lesen · Schreibschalter aus", vm.Rows.Single(r => r.Title == "MUSTERLAB").StatusText);
        Assert.DoesNotContain(vm.Rows.Where(r => r.IsProject).SelectMany(r => r.Rights), r => r.Read.Tone == "y" || r.Write.Tone == "y");
        Assert.Equal(StatusLevel.Warning, vm.ConnectionsHintLevel);
    }

    [Fact]
    public async Task Project_status_direct_when_allowed()
    {
        await using var m = new ManagerHarness("Musterlab");
        m.H.Permissions.Save(new PermissionSettings { Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Allowed } });
        var vm = new OverviewViewModel(m.Ctx);
        Assert.Equal("schreibt direkt · Schreibrecht nicht geprüft", vm.Rows.Single(r => r.IsProject).StatusText); // rights still "?"

        m.H.SetConn("Musterlab", c => c.PatScope = Acc.All(Acc.W));
        m.H.SetProject("Musterlab", p => p.ProjectRights = Acc.All(Acc.W));
        vm.Refresh();
        Assert.Equal("schreibt direkt", vm.Rows.Single(r => r.IsProject).StatusText);
    }

    [Fact]
    public async Task Migration_banner_and_import_per_collection()
    {
        await using var m = new ManagerHarness();
        File.WriteAllText(m.Ctx.LegacyCandidates[0], V1Json("AA", "BB", "CC"));
        var vm = new OverviewViewModel(m.Ctx);
        Assert.True(vm.ShowLegacyBanner);
        Assert.Equal("3 PATs im Klartext gefunden", vm.LegacyText);

        vm.ImportCommand.Execute(null);
        Assert.False(File.Exists(m.Ctx.LegacyCandidates[0]));
        Assert.Single(m.H.Store.GetAll());
        Assert.Equal(3, m.H.Store.AllProjects().Count);
        Assert.False(vm.ShowLegacyBanner);
    }

    [Fact]
    public async Task Later_hides_banner()
    {
        await using var m = new ManagerHarness();
        File.WriteAllText(m.Ctx.LegacyCandidates[0], V1Json("AA"));
        var vm = new OverviewViewModel(m.Ctx);
        vm.LaterCommand.Execute(null);
        Assert.False(vm.ShowLegacyBanner);
        Assert.True(File.Exists(m.Ctx.LegacyCandidates[0]));
    }

    [Fact]
    public async Task Pause_toggles_server_status()
    {
        await using var m = new ManagerHarness();
        var vm = new OverviewViewModel(m.Ctx);
        Assert.Equal("MCP-Server läuft", vm.ServerStatus);
        await vm.PauseCommand.ExecuteAsync();
        Assert.Equal("MCP-Server pausiert", vm.ServerStatus);
        Assert.Equal("Fortsetzen", vm.PauseButtonText);
        await vm.PauseCommand.ExecuteAsync();
        Assert.Equal("MCP-Server läuft", vm.ServerStatus);
    }

    [Fact]
    public async Task Pending_approvals_counted()
    {
        await using var m = new ManagerHarness("SHOP");
        await using var channel = new AzdoMcp.Core.Ipc.ManagerChannel(m.Hub, retryInterval: TimeSpan.FromMilliseconds(50));
        channel.Start();
        _ = channel.RequestApprovalAsync(new AzdoMcp.Core.Ipc.ApprovalRequest { Tool = "update_work_item", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2) }, TestContext.Current.CancellationToken);
        await Eventually.True(() => m.Host.PendingApprovals.Count == 1);
        var vm = new OverviewViewModel(m.Ctx);
        Assert.Equal(1, vm.PendingCount);
        Assert.Equal("update_work_item prüfen", vm.PendingHint);
    }

    [Fact]
    public async Task Add_connection_starts_wizard()
    {
        await using var m = new ManagerHarness();
        new OverviewViewModel(m.Ctx).AddConnectionCommand.Execute(null);
        Assert.Equal(("wizard", (string?)null), m.Navigations.Single());
    }
}

public class ConnectionsViewModelTests
{
    [Fact]
    public async Task Saved_pat_is_hidden()
    {
        await using var m = new ManagerHarness("SHOP");
        var vm = new ConnectionsViewModel(m.Ctx);
        Assert.Equal("SHOP", vm.Name);
        Assert.True(vm.HasStoredPat);
        Assert.False(vm.IsReplacingPat);
        Assert.Equal("", vm.NewPat);
        Assert.Equal("Projekte: 1 eingerichtet", vm.ProjectsSummary);
    }

    [Fact]
    public async Task List_shows_projects_below_connection()
    {
        await using var m = new ManagerHarness();
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects = [new ProjectEntry { Name = "SHOP", Alias = "SHOP" }, new ProjectEntry { Name = "OPS", Alias = "OPS" }],
        }, Fake.Pat);
        var vm = new ConnectionsViewModel(m.Ctx);
        var item = vm.Items.Single();
        Assert.Equal("2 Projekte", item.Detail);
        Assert.Equal(["SHOP", "OPS"], item.Projects.Select(p => p.Alias));
        Assert.True(item.Projects[0].IsDefault);
    }

    [Fact]
    public async Task Edit_connection_keeps_projects()
    {
        await using var m = new ManagerHarness("SHOP");
        var vm = new ConnectionsViewModel(m.Ctx);
        vm.Name = "tfs";
        vm.PatExpires = new DateTime(2030, 1, 1);
        Assert.True(vm.Save(), vm.ValidationMessage);
        var c = m.H.Store.GetAll().Single();
        Assert.Equal("tfs", c.Name);
        Assert.Equal(new DateOnly(2030, 1, 1), c.PatExpires);
        Assert.Equal("SHOP", c.Projects.Single().Alias);
    }

    [Fact]
    public async Task New_connection_starts_wizard()
    {
        await using var m = new ManagerHarness("SHOP");
        new ConnectionsViewModel(m.Ctx).NewCommand.Execute(null);
        Assert.Equal("wizard", m.Navigations.Single().Key);
    }

    [Fact]
    public async Task Discover_and_rescan_navigate_with_connection()
    {
        await using var m = new ManagerHarness("SHOP");
        var vm = new ConnectionsViewModel(m.Ctx);
        vm.DiscoverCommand.Execute(null);
        vm.RescanCommand.Execute(null);
        var id = m.H.Conn("SHOP").Id;
        Assert.Equal([("wizard", (string?)id), ("rescan", id)], m.Navigations);
    }

    [Fact]
    public async Task Default_project_selection()
    {
        await using var m = new ManagerHarness("AA", "BB");
        var vm = new ConnectionsViewModel(m.Ctx);
        vm.DefaultProject = vm.DefaultProjectOptions.Single(o => o.Text.StartsWith("BB"));
        Assert.True(m.H.Proj("BB").Project.IsDefault);
        Assert.False(m.H.Proj("AA").Project.IsDefault);
    }

    [Fact]
    public async Task Token_url()
    {
        await using var m = new ManagerHarness("SHOP");
        new ConnectionsViewModel(m.Ctx).CreatePatCommand.Execute(null);
        Assert.Equal(Fake.Collection + "/_usersSettings/tokens", m.Dialogs.OpenedUrls.Single());
    }

    [Fact]
    public async Task Unsaved_changes_prompt_on_switch()
    {
        await using var m = new ManagerHarness("AA", "BB");
        var vm = new ConnectionsViewModel(m.Ctx);
        vm.PatExpires = new DateTime(2031, 5, 5);
        Assert.True(vm.IsDirty);

        m.Dialogs.SaveAnswer = null;
        vm.SelectedItem = vm.Items.Single(i => i.Name == "BB");
        Assert.Equal("AA", vm.Name);

        m.Dialogs.SaveAnswer = true;
        vm.SelectedItem = vm.Items.Single(i => i.Name == "BB");
        Assert.Equal("BB", vm.Name);
        Assert.Equal(new DateOnly(2031, 5, 5), m.H.Conn("AA").PatExpires);
    }

    [Fact]
    public async Task Main_view_model_asks_before_leaving_dirty_page()
    {
        await using var m = new ManagerHarness("AA");
        var main = new MainViewModel(m.Ctx);
        main.Navigate("connections");
        main.Connections.PatExpires = new DateTime(2031, 1, 1);
        m.Dialogs.SaveAnswer = null;
        Assert.False(main.Navigate("log"));
        Assert.Same(main.Connections, main.Page);
        m.Dialogs.SaveAnswer = false;
        Assert.True(main.Navigate("log"));
        Assert.Null(m.H.Conn("AA").PatExpires);
    }

    [Fact]
    public async Task Connection_test_shows_pat_scope()
    {
        await using var m = new ManagerHarness("SHOP");
        m.H.Http.On("_apis/connectionData", """{"authenticatedUser":{"providerDisplayName":"Max Muster"}}""")
            .On("_apis/projects", """{"value":[{"id":"p1","name":"SHOP"}]}""")
            .On("_apis/wit/wiql", "{}").On("validateOnly=true", "{}").On("_apis/git/repositories", "{}")
            .On("_apis/test/runs", "{}", HttpStatusCode.Forbidden).On("teamsettings/iterations", "{}");
        var vm = new ConnectionsViewModel(m.Ctx);
        await vm.TestCommand.ExecuteAsync();
        Assert.Contains(vm.TestResults, a => a.Text == "Angemeldet als Max Muster");
        Assert.Equal(("Work Items", "ja", "ja"), (vm.Scope[0].Name, vm.Scope[0].Read.Text, vm.Scope[0].Write.Text));
        Assert.Equal(("Code", "ja", "n/a"), (vm.Scope[1].Name, vm.Scope[1].Read.Text, vm.Scope[1].Write.Text));
        Assert.Equal(("Pipelines", "n/a", "n/a"), (vm.Scope[2].Name, vm.Scope[2].Read.Text, vm.Scope[2].Write.Text));
        Assert.Equal(("Test", "nein"), (vm.Scope[3].Name, vm.Scope[3].Read.Text));
        Assert.StartsWith("Schreiben wird nur für Work Items geprüft", vm.ScopeFootnoteText);
        Assert.Equal(Check.No, m.H.Proj("SHOP").Effective[AccessArea.Test].Read);
    }

    [Fact]
    public async Task Remove_connection_removes_project_settings()
    {
        await using var m = new ManagerHarness("AA", "BB");
        var a = m.H.Proj("AA");
        m.H.Permissions.Update(s => s.ForProject(a.PermissionKey).Groups[ToolGroup.Git] = PermissionMode.Ask);
        var vm = new ConnectionsViewModel(m.Ctx);
        vm.RemoveCommand.Execute(null);
        Assert.Single(m.H.Store.GetAll());
        Assert.Equal("BB", vm.Name);
        Assert.Empty(m.H.Permissions.Get().Projects);
    }
}

public class PermissionsViewModelTests
{
    private static async Task<ManagerHarness> TwoProjects()
    {
        var m = new ManagerHarness();
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            PatScope = new AreaRights { Areas = { [AccessArea.WorkItems] = Acc.W, [AccessArea.Test] = Acc.Rn } },
            Projects =
            [
                new ProjectEntry { Name = "SHOP", Alias = "SHOP", AllowWrite = true },
                new ProjectEntry { Name = "Musterlab", Alias = "Musterlab", AllowWrite = true },
                new ProjectEntry { Name = "OPS", Alias = "OPS", ProjectRights = new AreaRights { Areas = { [AccessArea.WorkItems] = Acc.Ro } } },
            ],
        }, Fake.Pat);
        await Task.CompletedTask;
        return m;
    }

    [Fact]
    public async Task Tree_levels_and_texts()
    {
        await using var m = await TwoProjects();
        var vm = new PermissionsViewModel(m.Ctx);
        Assert.Equal(["Work Items lesen", "Work Items schreiben", "Git", "Pipelines", "Test", "Planung"], vm.Rows.Select(r => r.Title));
        Assert.All(vm.Rows, r => Assert.Equal("Gruppe", r.LevelText));
        Assert.Equal("6 Tools", vm.Rows[1].Hint);
        Assert.StartsWith("Ebenen: Gruppe → Tool → Verbindung → Projekt.", vm.SubtitleText);
        Assert.EndsWith("Blasser Modus = geerbt von der Ebene darüber.", vm.Footnote1Text);

        var write = vm.Rows[1];
        write.IsExpanded = true;
        var tool = vm.Find(ToolGroup.WorkItemsWrite, "update_work_item")!;
        Assert.Contains(tool, vm.Rows);
        Assert.Equal("wie Gruppe", tool.Hint);
        Assert.True(tool.IsInherited);
        Assert.True(tool.IsAsk);
    }

    [Fact]
    public async Task Project_exception_is_saved_and_shown()
    {
        await using var m = await TwoProjects();
        var vm = new PermissionsViewModel(m.Ctx);
        var musterlab = vm.Find(ToolGroup.WorkItemsWrite, "update_work_item", "tfs", "MUSTERLAB")!;
        var shop = vm.Find(ToolGroup.WorkItemsWrite, "update_work_item", "tfs", "SHOP")!;
        Assert.Equal("wie Gruppe", shop.Hint);

        musterlab.IsAllowed = true;
        Assert.Equal("Ausnahme", musterlab.Hint);
        Assert.True(vm.IsDirty);
        Assert.True(vm.Save());

        var tool = AzdoMcp.Core.Tools.ToolCatalog.Find("update_work_item")!;
        var s = m.H.Permissions.Get();
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(tool, m.H.Proj("Musterlab"), s).Mode);
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(tool, m.H.Proj("SHOP"), s).Mode);
    }

    [Fact]
    public async Task Connection_level_block_applies_to_all_projects()
    {
        await using var m = await TwoProjects();
        var vm = new PermissionsViewModel(m.Ctx);
        vm.Find(ToolGroup.WorkItemsWrite, null, "tfs")!.IsBlocked = true;
        Assert.Equal("wie Verbindung", vm.Find(ToolGroup.WorkItemsWrite, "create_work_item", "tfs", "SHOP")!.Hint);
        vm.Save();
        var tool = AzdoMcp.Core.Tools.ToolCatalog.Find("create_work_item")!;
        Assert.Equal(PermissionMode.Blocked, PermissionEvaluator.Evaluate(tool, m.H.Proj("SHOP"), m.H.Permissions.Get()).Mode);
    }

    [Fact]
    public async Task Reset_returns_to_inherited()
    {
        await using var m = await TwoProjects();
        var vm = new PermissionsViewModel(m.Ctx);
        var row = vm.Find(ToolGroup.Git, "get_commits")!;
        row.IsBlocked = true;
        Assert.False(row.IsInherited);
        row.ResetCommand.Execute(null);
        Assert.True(row.IsInherited);
        Assert.True(row.IsAllowed);
    }

    [Fact]
    public async Task Write_switch_on_project_rows()
    {
        await using var m = await TwoProjects();
        var vm = new PermissionsViewModel(m.Ctx);
        var ops = vm.Find(ToolGroup.WorkItemsWrite, null, "tfs", "OPS")!;
        Assert.True(ops.ShowWriteSwitch);
        Assert.False(ops.WriteSwitch!.CanWrite);
        Assert.Equal(("nein", "r"), (ops.Right!.Text, ops.Right.Tone)); // Work Items Schreiben
        Assert.Equal("kein Projektrecht zum Bearbeiten", ops.Hint);
        Assert.Equal(("ja", "g"), (vm.Find(ToolGroup.WorkItemsRead, null, "tfs", "OPS")!.Right!.Text, vm.Find(ToolGroup.WorkItemsRead, null, "tfs", "OPS")!.Right!.Tone));
        Assert.Null(vm.Find(ToolGroup.Git, null, "tfs", "OPS")!.WriteSwitch);

        var shop = vm.Find(ToolGroup.WorkItemsWrite, null, "tfs", "SHOP")!;
        shop.WriteSwitch!.AllowWrite = false;
        vm.Save();
        Assert.False(m.H.Proj("SHOP").Project.AllowWrite);
    }

    [Fact]
    public async Task Connection_rows_show_pat_scope()
    {
        await using var m = await TwoProjects();
        var vm = new PermissionsViewModel(m.Ctx);
        var conn = vm.Find(ToolGroup.WorkItemsWrite, null, "tfs")!;
        Assert.Equal("PAT³:", conn.RightText);
        Assert.Equal("ja", conn.Right!.Text);
    }
}

public class SetupWizardTests
{
    private static async Task<(ManagerHarness M, SetupWizardViewModel W, FakeAzdo Azdo)> NewWizard()
    {
        var azdo = new FakeAzdo();
        var m = new ManagerHarness(http: azdo);
        var w = new SetupWizardViewModel(m.Ctx);
        w.StartNew();
        w.CollectionUrl = Fake.Collection;
        w.Pat = Fake.Pat;
        w.Name = "tfs";
        await Task.CompletedTask;
        return (m, w, azdo);
    }

    [Fact]
    public async Task Next_needs_a_successful_test()
    {
        var (m, w, _) = await NewWizard();
        await using var _m = m;
        Assert.True(w.IsConnectionStep);
        Assert.False(w.CanGoNext);
        await w.TestCommand.ExecuteAsync();
        Assert.True(w.CanGoNext);
        Assert.Contains(w.Scope, s => s.Name == "Work Items" && s.Write.Text == "ja");
        w.Pat = Fake.Pat + "x";
        Assert.False(w.CanGoNext);
    }

    private static async Task<SetupWizardViewModel> AtFirstProject(FakeAzdo azdo, ManagerHarness m)
    {
        var w = new SetupWizardViewModel(m.Ctx);
        w.StartNew();
        w.CollectionUrl = Fake.Collection;
        w.Pat = Fake.Pat;
        w.Name = "tfs";
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        return w;
    }

    [Fact]
    public async Task Untestable_write_right_allows_the_switch_with_hint()
    {
        var azdo = new FakeAzdo { SecurityApi = false };
        await using var m = new ManagerHarness(http: azdo);
        var w = await AtFirstProject(azdo, m);
        var alpha = w.CurrentSetup!;
        Assert.Equal(("ja", "n/a"), (alpha.Rights[0].Read.Text, alpha.Rights[0].Write.Text));
        Assert.True(alpha.CanWrite);
        alpha.AllowWrite = true;
        Assert.True(alpha.AllowWrite);
        Assert.Equal("Schreibrecht nicht prüfbar – Azure DevOps entscheidet beim Schreiben", alpha.WriteHint);
    }

    [Fact]
    public async Task Pat_without_write_locks_the_switch_with_reason()
    {
        var azdo = new FakeAzdo { PatWorkItemsWrite = false };
        await using var m = new ManagerHarness(http: azdo);
        var w = await AtFirstProject(azdo, m);
        Assert.False(w.CurrentSetup!.CanWrite);
        Assert.Equal("Nicht möglich: PAT ohne Schreibrecht.", w.CurrentSetup.WriteHint);
    }

    [Fact]
    public async Task Preselects_projects_readable_in_any_area()
    {
        var azdo = new FakeAzdo();
        azdo.Projects[2] = azdo.Projects[2] with { GitRead = true }; // Gamma: only Code readable
        await using var m = new ManagerHarness(http: azdo);
        var w = new SetupWizardViewModel(m.Ctx);
        w.StartNew();
        w.CollectionUrl = Fake.Collection;
        w.Pat = Fake.Pat;
        w.Name = "tfs";
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        Assert.True(w.Rows.Single(r => r.Name == "Gamma").IsSelected);
    }

    [Fact]
    public async Task Full_run_sets_up_selected_projects_with_per_project_rules()
    {
        var (m, w, azdo) = await NewWizard();
        await using var _m = m;
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();

        Assert.True(w.IsProjectsStep);
        Assert.Equal(["Alpha", "Beta", "Gamma"], w.Rows.Select(r => r.Name));
        Assert.True(w.Rows.Single(r => r.Name == "Alpha").IsSelected);
        Assert.True(w.Rows.Single(r => r.Name == "Beta").IsSelected);
        Assert.False(w.Rows.Single(r => r.Name == "Gamma").IsSelected); // no access anywhere
        Assert.Equal(2, w.SelectedCount);

        await w.NextCommand.ExecuteAsync();
        Assert.True(w.IsProjectStep);
        Assert.Equal("Projekt 1 von 2 · Alpha", w.StepTitle);
        var alpha = w.CurrentSetup!;
        Assert.True(alpha.CanWrite);
        Assert.False(alpha.AllowWrite);
        Assert.True(alpha.IsDefault);
        Assert.True(alpha.Groups.Single(g => g.Group == ToolGroup.WorkItemsRead).IsAllowed);
        Assert.True(alpha.Groups.Single(g => g.Group == ToolGroup.WorkItemsWrite).IsAsk);
        alpha.AllowWrite = true;
        alpha.Groups.Single(g => g.Group == ToolGroup.Git).IsAsk = true;

        await w.NextCommand.ExecuteAsync();
        var beta = w.CurrentSetup!;
        Assert.Equal("Beta", beta.Name);
        Assert.False(beta.CanWrite);
        beta.AllowWrite = true;
        Assert.False(beta.AllowWrite);
        var betaWrite = beta.Groups.Single(g => g.Group == ToolGroup.WorkItemsWrite);
        Assert.True(betaWrite.IsLocked);
        Assert.Equal("nur Lesen", betaWrite.LockReason);
        Assert.Equal("Nicht möglich: kein Projektrecht zum Bearbeiten.", beta.WriteHint);
        Assert.Equal(5, beta.Rights.Count);

        await w.NextCommand.ExecuteAsync();
        Assert.True(w.IsSummaryStep);
        Assert.Equal("Übernehmen (2 Projekte)", w.ApplyText);
        await w.ApplyCommand.ExecuteAsync();

        var all = m.H.Store.AllProjects();
        Assert.Equal(["ALPHA", "BETA"], all.Select(p => p.Alias).Order());
        Assert.Single(Directory.EnumerateFiles(m.H.Dir.AppPaths.SecretsDirectory));
        Assert.True(m.H.Proj("Alpha").Project.AllowWrite);
        Assert.True(m.H.Proj("Alpha").Project.IsDefault);
        Assert.False(m.H.Proj("Beta").Project.AllowWrite);

        // Only the changed mode was stored.
        var s = m.H.Permissions.Get();
        var alphaModes = s.Projects[m.H.Proj("Alpha").PermissionKey];
        Assert.Equal([ToolGroup.Git], alphaModes.Groups.Keys);
        Assert.False(s.Projects.ContainsKey(m.H.Proj("Beta").PermissionKey));
        Assert.Equal(("connections", m.H.Conn("Alpha").Id), m.Navigations.Last());
        Assert.DoesNotContain(azdo.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Unchanged_defaults_follow_later_global_changes()
    {
        var (m, w, _) = await NewWizard();
        await using var _m = m;
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        w.SkipRestCommand.Execute(null);
        await w.ApplyCommand.ExecuteAsync();

        m.H.Permissions.Update(s => s.Groups[ToolGroup.Git] = PermissionMode.Ask);
        var tool = AzdoMcp.Core.Tools.ToolCatalog.Find("get_commits")!;
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(tool, m.H.Proj("Beta"), m.H.Permissions.Get()).Mode);
    }

    [Fact]
    public async Task Back_keeps_inputs()
    {
        var (m, w, _) = await NewWizard();
        await using var _m = m;
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        w.CurrentSetup!.Alias = "A1";
        await w.NextCommand.ExecuteAsync();
        w.CurrentSetup!.Alias = "B1";
        w.BackCommand.Execute(null);
        Assert.Equal("A1", w.CurrentSetup!.Alias);
        w.BackCommand.Execute(null);
        Assert.True(w.IsProjectsStep);
        await w.NextCommand.ExecuteAsync();
        Assert.Equal("A1", w.CurrentSetup!.Alias);
        await w.NextCommand.ExecuteAsync();
        Assert.Equal("B1", w.CurrentSetup!.Alias);
    }

    [Fact]
    public async Task Alias_clash_blocks_next()
    {
        var (m, w, _) = await NewWizard();
        await using var _m = m;
        m.H.Add("Alpha");
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        Assert.Equal("ALPHA2", w.CurrentSetup!.Alias);
        w.CurrentSetup.Alias = "alpha";
        await w.NextCommand.ExecuteAsync();
        Assert.Equal("Projekt 1 von 2 · Alpha", w.StepTitle);
        Assert.Equal("Kurzname „ALPHA“ wird bereits verwendet", w.CurrentSetup.AliasError);
    }

    [Fact]
    public async Task Locked_group_without_access()
    {
        var azdo = new FakeAzdo { PatTest = false };
        await using var m = new ManagerHarness(http: azdo);
        var w = new SetupWizardViewModel(m.Ctx);
        w.StartNew();
        w.CollectionUrl = Fake.Collection;
        w.Pat = Fake.Pat;
        w.Name = "tfs";
        await w.TestCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        await w.NextCommand.ExecuteAsync();
        var test = w.CurrentSetup!.Groups.Single(g => g.Group == ToolGroup.Test);
        Assert.Equal("kein Zugriff", test.LockReason);
        test.IsAllowed = true;
        Assert.True(test.IsBlocked);
    }

    [Fact]
    public async Task Existing_connection_starts_at_projects_and_marks_configured()
    {
        await using var m = new ManagerHarness(http: new FakeAzdo());
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects = [new ProjectEntry { AzdoId = "id-alpha", Name = "Alpha", Alias = "Alpha", AllowWrite = true }],
        }, Fake.Pat);
        var w = new SetupWizardViewModel(m.Ctx);
        await w.StartForConnectionAsync(m.H.Conn("Alpha").Id);
        Assert.True(w.IsProjectsStep);
        var alpha = w.Rows.Single(r => r.Name == "Alpha");
        Assert.True(alpha.IsConfigured);
        Assert.False(alpha.CanSelect);
        Assert.Equal("Alle Projekte (3)", w.FilterAllText);
        w.Filter = SetupWizardViewModel.FilterNew;
        Assert.Equal(["Beta", "Gamma"], w.FilteredRows.Select(r => r.Name));
        Assert.DoesNotContain(w.Steps, s => s.Title == "Verbindung");
    }
}

public class ProjectRescanTests
{
    private static ManagerHarness WithStored(FakeAzdo azdo)
    {
        var m = new ManagerHarness(http: azdo);
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            LastDiscovery = DateTimeOffset.UtcNow.AddDays(-10),
            PatScope = Acc.All(Acc.W),
            Projects =
            [
                new ProjectEntry { AzdoId = "id-alpha", Name = "Alpha", Alias = "Alpha", ProjectRights = Acc.All(Acc.W), AllowWrite = true },
                new ProjectEntry { AzdoId = "id-beta", Name = "Beta", Alias = "Beta", ProjectRights = Acc.Of(Acc.N, Acc.Rn, Acc.Rn, Acc.Rn) },
                new ProjectEntry { AzdoId = "id-qa", Name = "QA", Alias = "QA" },
            ],
        }, Fake.Pat);
        return m;
    }

    [Fact]
    public async Task Diff_groups()
    {
        await using var m = WithStored(new FakeAzdo());
        var wizard = new SetupWizardViewModel(m.Ctx);
        var vm = new ProjectRescanViewModel(m.Ctx, wizard);
        await vm.StartAsync(m.H.Conn("Alpha").Id);

        Assert.Equal(["Gamma"], vm.NewRows.Select(r => r.Name));
        Assert.False(vm.NewRows.Single().IsSelected); // no access → not preselected
        Assert.Equal(["QA"], vm.MissingRows.Select(r => r.Alias));
        var beta = vm.ChangedRows.Single(r => r.Name == "BETA");
        // Security API now answers: WI readable, contribute/test plans denied → moves to or from "nein"
        Assert.Equal(["Work Items Lesen nein→ja", "Code Schreiben n/a→nein", "Test Schreiben n/a→nein"],
            beta.Changes.Select(c => $"{c.Area} {c.Old.Text}→{c.New.Text}"));
        // Alpha: Code Schreiben ja → n/a is not a move to or from "nein"
        Assert.DoesNotContain(vm.ChangedRows, r => r.Name == "ALPHA");
        Assert.Contains("3 Änderung", vm.Summary);
    }

    [Fact]
    public async Task Apply_without_new_projects_refreshes_and_marks_missing()
    {
        await using var m = WithStored(new FakeAzdo());
        var vm = new ProjectRescanViewModel(m.Ctx, new SetupWizardViewModel(m.Ctx));
        await vm.StartAsync(m.H.Conn("Alpha").Id);
        await vm.ApplyCommand.ExecuteAsync();

        Assert.False(m.H.Proj("QA").Project.Visible);
        Assert.Equal(Acc.Ro, m.H.Proj("Beta").Effective[AccessArea.WorkItems]);
        Assert.True(m.H.Proj("Alpha").Project.AllowWrite);
        Assert.Equal(3, m.H.Store.AllProjects().Count);
    }

    [Fact]
    public async Task New_project_goes_through_wizard_project_step()
    {
        var azdo = new FakeAzdo();
        azdo.Projects.Add(new FakeAzdo.FakeProject("id-docs", "Docs", true, true, true, false, true));
        await using var m = WithStored(azdo);
        var wizard = new SetupWizardViewModel(m.Ctx);
        var vm = new ProjectRescanViewModel(m.Ctx, wizard);
        await vm.StartAsync(m.H.Conn("Alpha").Id);
        Assert.True(vm.NewRows.Single(r => r.Name == "Docs").IsSelected);

        await vm.ApplyCommand.ExecuteAsync();
        Assert.Equal("wizard-continue", m.Navigations.Last().Key);
        Assert.Null(m.H.Store.FindProject("Docs")); // nothing saved yet
        Assert.True(wizard.IsProjectStep);
        Assert.Equal("Docs", wizard.CurrentSetup!.Name);

        await wizard.NextCommand.ExecuteAsync();
        await wizard.ApplyCommand.ExecuteAsync();
        Assert.NotNull(m.H.Store.FindProject("Docs"));
        Assert.False(m.H.Proj("QA").Project.Visible);
    }

    [Fact]
    public async Task Remove_only_on_request()
    {
        await using var m = WithStored(new FakeAzdo());
        var vm = new ProjectRescanViewModel(m.Ctx, new SetupWizardViewModel(m.Ctx));
        await vm.StartAsync(m.H.Conn("Alpha").Id);
        m.Dialogs.ConfirmAnswer = false;
        vm.RemoveCommand.Execute(vm.MissingRows.Single());
        Assert.NotNull(m.H.Store.FindProject("QA"));
        m.Dialogs.ConfirmAnswer = true;
        vm.RemoveCommand.Execute(vm.MissingRows.Single());
        Assert.Null(m.H.Store.FindProject("QA"));
        Assert.Equal(2, m.H.Store.AllProjects().Count);
    }
}

public class ReminderSchedulerTests
{
    [Fact]
    public async Task Reminder_once_per_day()
    {
        await using var m = new ManagerHarness("SHOP", "OK");
        m.H.SetConn("SHOP", c => c.PatExpires = m.Ctx.Today.AddDays(4));
        m.H.SetConn("OK", c => c.PatExpires = m.Ctx.Today.AddDays(40));

        var due = m.Ctx.Reminders.Due(m.H.Store.GetAll());
        Assert.Equal("PAT für SHOP läuft in 4 Tagen ab", due.Single().Text);
        m.Ctx.Reminders.MarkShown(due.Select(d => d.Connection));
        Assert.Empty(m.Ctx.Reminders.Due(m.H.Store.GetAll()));
    }

    [Fact]
    public async Task Expired_is_reminded()
    {
        await using var m = new ManagerHarness("XX");
        m.H.SetConn("XX", c => c.PatExpires = m.Ctx.Today.AddDays(-2));
        Assert.Equal("PAT für XX ist abgelaufen", m.Ctx.Reminders.Due(m.H.Store.GetAll()).Single().Text);
    }
}

public class TrayModelTests
{
    [Fact]
    public async Task Tray_entries_and_overall_level()
    {
        await using var m = new ManagerHarness("default", "SHOP", "OPS");
        m.H.SetConn("SHOP", c => c.PatExpires = m.Ctx.Today.AddDays(4));
        var main = new MainViewModel(m.Ctx);
        Assert.Equal(StatusLevel.Warning, main.OverallLevel);
        Assert.Equal("PAT in 4 Tagen", main.TrayConnections.Single(c => c.Name == "SHOP").Hint);

        m.H.SetConn("OPS", c => c.PatExpires = m.Ctx.Today.AddDays(-1));
        main.RefreshTray();
        Assert.Equal(StatusLevel.Error, main.OverallLevel);
        Assert.Equal("PAT abgelaufen", main.TrayConnections.Single(c => c.Name == "OPS").Hint);
        Assert.Equal("Offene Freigaben (0)", main.PendingMenuText);
        Assert.Equal("Benutzer-Installation · ohne Admin", main.FooterInstall);
    }

    [Fact]
    public async Task Tray_connection_opens_connections_page()
    {
        await using var m = new ManagerHarness("default", "SHOP");
        var main = new MainViewModel(m.Ctx);
        main.Navigate("connections", m.H.Conn("SHOP").Id);
        Assert.Same(main.Connections, main.Page);
        Assert.Equal("SHOP", main.Connections.Name);
    }

    [Fact]
    public async Task Wizard_and_rescan_pages_keep_connections_nav()
    {
        await using var m = new ManagerHarness("SHOP");
        var main = new MainViewModel(m.Ctx);
        main.Navigate("wizard");
        Assert.Same(main.Wizard, main.Page);
        Assert.Equal("connections", main.Current.Key);
        Assert.True(main.Wizard.IsConnectionStep);
    }
}

public class LogViewModelTests
{
    [Fact]
    public async Task Rows_newest_first_with_filters()
    {
        await using var m = new ManagerHarness();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        m.Log("get_work_item", AuditOutcome.Ok, "OK", at: t0);
        m.Log("get_test_runs", AuditOutcome.Error, "403 Scope fehlt", at: t0.AddMinutes(1));
        m.Log("get_pull_requests", AuditOutcome.Error, "401 PAT abgelaufen", conn: "OPS", at: t0.AddMinutes(2));

        var vm = new LogViewModel(m.Ctx);
        Assert.Equal(["get_pull_requests", "get_test_runs", "get_work_item"], vm.Rows.Select(r => r.Tool));
        Assert.Equal("0,4 s", vm.Rows[0].Duration);
        Assert.Equal("r", vm.Rows[0].Result.Tone);
        Assert.Contains("OPS", vm.ConnectionFilters);

        vm.ConnectionFilter = "SHOP";
        Assert.Equal(2, vm.Rows.Count);
        vm.ResultFilter = "Fehler";
        Assert.Single(vm.Rows);
        vm.ConnectionFilter = LogViewModel.AllConnections;
        vm.ResultFilter = LogViewModel.AllResults;
        vm.Search = "pull";
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task Export_writes_filtered_rows()
    {
        await using var m = new ManagerHarness();
        m.Log("get_work_item", AuditOutcome.Ok, "OK");
        m.Log("get_commits", AuditOutcome.Ok, "OK", conn: "OPS");
        m.Dialogs.SavePath = Path.Combine(m.H.Dir.Path, "x.csv");
        var vm = new LogViewModel(m.Ctx) { ConnectionFilter = "OPS" };
        vm.ExportCommand.Execute(null);
        var lines = File.ReadAllLines(m.Dialogs.SavePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("get_commits", lines[1]);
    }

    [Fact]
    public async Task Live_update_shows_new_record_on_refresh()
    {
        await using var m = new ManagerHarness();
        var vm = new LogViewModel(m.Ctx);
        Assert.Empty(vm.Rows);
        m.Log("ping", AuditOutcome.Ok, "OK");
        vm.Refresh();
        Assert.Equal("ping", vm.Rows[0].Tool);
    }

    [Fact]
    public async Task Pending_panel_approve()
    {
        await using var m = new ManagerHarness("SHOP");
        await using var channel = new AzdoMcp.Core.Ipc.ManagerChannel(m.Hub, retryInterval: TimeSpan.FromMilliseconds(50));
        channel.Start();
        var answer = channel.RequestApprovalAsync(new AzdoMcp.Core.Ipc.ApprovalRequest
        {
            Client = "Claude Code",
            Tool = "update_work_item",
            Connection = "SHOP",
            WorkItemId = 48211,
            Summary = "update_work_item: Status Active → Resolved, Priorität 2 → 1",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(42.5),
        }, TestContext.Current.CancellationToken);
        await Eventually.True(() => m.Host.PendingApprovals.Count == 1);

        var vm = new LogViewModel(m.Ctx);
        Assert.True(vm.HasApproval);
        Assert.Equal("Claude Code möchte", vm.ApprovalClient);
        Assert.Equal("Work Item #48211 in SHOP ändern", vm.ApprovalHeadline);
        Assert.Equal("Status Active → Resolved\nPriorität 2 → 1", vm.ApprovalChange);
        Assert.StartsWith("offen 0:4", vm.Remaining);

        await vm.ApproveCommand.ExecuteAsync();
        Assert.Equal(AzdoMcp.Core.Ipc.ApprovalOutcome.Approved, await answer);
        Assert.False(vm.HasApproval);
    }
}

public class ClientConfigWriterTests
{
    private static ClientSetupService Setup(TempDir dir) => new(new ManagerEnvironment
    {
        LocalAppData = Path.Combine(dir.Path, "Local"),
        AppData = Path.Combine(dir.Path, "Roaming"),
        UserProfile = dir.Path,
        AppDirectory = Path.Combine(dir.Path, "Local", "AzdoMcp", "current"),
    });

    [Fact]
    public void Installed_path_is_stable_path()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Assert.Equal(Path.Combine(dir.Path, "Local", "AzdoMcp", "current", "azdo-mcp.exe"), s.ExePath);
    }

    [Fact]
    public void Claude_desktop_keeps_other_servers_and_backs_up()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(s.ClaudeDesktopConfig)!);
        File.WriteAllText(s.ClaudeDesktopConfig, """
            {
              "mcpServers": {
                "other": { "command": "other.exe" }
              },
              "theme": "dark"
            }
            """);
        Assert.False(s.Detect(ClientKind.ClaudeDesktop).IsConfigured);

        var r = s.WriteClaudeDesktop();
        var json = JsonNode.Parse(File.ReadAllText(s.ClaudeDesktopConfig))!;
        Assert.Equal("other.exe", json["mcpServers"]!["other"]!["command"]!.GetValue<string>());
        Assert.Equal(s.ExePath, json["mcpServers"]!["azure-devops"]!["command"]!.GetValue<string>());
        Assert.Equal("--stdio", json["mcpServers"]!["azure-devops"]!["args"]![0]!.GetValue<string>());
        Assert.Equal("dark", json["theme"]!.GetValue<string>());
        Assert.NotNull(r.BackupPath);
        Assert.Contains(".azdo-mcp-backup-", r.BackupPath);
        Assert.DoesNotContain("azure-devops", File.ReadAllText(r.BackupPath!));
        Assert.True(s.Detect(ClientKind.ClaudeDesktop).IsConfigured);
    }

    [Fact]
    public void Writing_twice_replaces_entry()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        s.WriteClaudeDesktop();
        s.WriteClaudeDesktop();
        var json = JsonNode.Parse(File.ReadAllText(s.ClaudeDesktopConfig))!;
        Assert.Single(json["mcpServers"]!.AsObject());
    }

    [Fact]
    public void Opencode_jsonc_comments_are_kept()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(s.OpenCodeConfig)!);
        File.WriteAllText(s.OpenCodeConfig, """
            {
              // mein Kommentar
              "$schema": "https://opencode.ai/config.json",
              /* Block-Kommentar */
              "mcp": {
                "other": { "type": "local", "command": ["x"] } // inline
              }
            }
            """);
        s.WriteOpenCode();
        var text = File.ReadAllText(s.OpenCodeConfig);
        Assert.Contains("// mein Kommentar", text);
        Assert.Contains("/* Block-Kommentar */", text);
        Assert.Contains("// inline", text);
        var json = JsoncEditor.Parse(text)!;
        Assert.Equal("local", json["mcp"]!["azure-devops"]!["type"]!.GetValue<string>());
        Assert.Equal(s.ExePath, json["mcp"]!["azure-devops"]!["command"]![0]!.GetValue<string>());
        Assert.NotNull(json["mcp"]!["other"]);
        Assert.True(s.Detect(ClientKind.OpenCode).IsConfigured);
    }

    [Fact]
    public void Vscode_user_and_workspace()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        s.WriteVsCode();
        Assert.Equal("stdio", JsonNode.Parse(File.ReadAllText(s.VsCodeUserConfig))!["servers"]!["azure-devops"]!["type"]!.GetValue<string>());

        var ws = Path.Combine(dir.Path, "ws");
        Directory.CreateDirectory(ws);
        var r = s.WriteVsCode(ws);
        Assert.Equal(Path.Combine(ws, ".vscode", "mcp.json"), r.Path);
        Assert.Null(r.BackupPath);
    }

    [Fact]
    public void Invalid_file_is_not_written()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(s.ClaudeDesktopConfig)!);
        File.WriteAllText(s.ClaudeDesktopConfig, "{ \"mcpServers\": ");
        var ex = Assert.Throws<ClientConfigException>(() => s.WriteClaudeDesktop());
        Assert.Contains("kein gültiges JSON", ex.Message);
        Assert.Equal("{ \"mcpServers\": ", File.ReadAllText(s.ClaudeDesktopConfig));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(s.ClaudeDesktopConfig)!, "*backup*"));
    }

    [Fact]
    public void No_secret_or_url_in_client_configs()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        s.WriteClaudeDesktop();
        s.WriteOpenCode();
        s.WriteVsCode();
        foreach (var f in new[] { s.ClaudeDesktopConfig, s.OpenCodeConfig, s.VsCodeUserConfig })
        {
            var text = File.ReadAllText(f);
            Assert.DoesNotContain(Fake.Pat, text);
            Assert.DoesNotContain("http", text);
        }
    }

    [Fact]
    public void Claude_code_command_and_cli_detection()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Assert.Equal($"claude mcp add --scope user azure-devops -- \"{s.ExePath}\" --stdio", s.ClaudeCodeCommand);
        Assert.Null(ClientSetupService.FindClaudeCli(dir.Path));
        File.WriteAllText(Path.Combine(dir.Path, "claude.cmd"), "");
        Assert.Equal(Path.Combine(dir.Path, "claude.cmd"), ClientSetupService.FindClaudeCli(dir.Path));
    }

    [Fact]
    public void Claude_code_detected_from_user_config()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        File.WriteAllText(s.ClaudeCodeConfig, new JsonObject { ["mcpServers"] = new JsonObject { ["azure-devops"] = new JsonObject { ["type"] = "stdio", ["command"] = s.ExePath, ["args"] = new JsonArray("--stdio") } } }.ToJsonString());
        Assert.True(s.Detect(ClientKind.ClaudeCode).IsConfigured);
    }

    [Fact]
    public async Task Clients_page_copy_command_and_status()
    {
        await using var m = new ManagerHarness();
        var vm = new ClientsViewModel(m.Ctx);
        Assert.Equal(["Claude Code", "Claude Desktop", "OpenCode", "VS Code / GitHub Copilot"], vm.Cards.Select(c => c.Name));
        Assert.All(vm.Cards, c => Assert.Equal("offen", c.Status.Text));
        vm.Cards[0].Actions[0].Command.Execute(null);
        Assert.Equal(m.Ctx.Clients.ClaudeCodeCommand, m.Dialogs.Clipboard);
        vm.Cards[1].Actions[0].Command.Execute(null);
        Assert.Equal("eingerichtet", vm.Cards[1].Status.Text);
        Assert.Equal(@"%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe", vm.ExeDisplayPath.Replace('/', '\\'));
    }

    [Fact]
    public void Empty_file_gets_entry()
    {
        var text = JsoncEditor.SetProperty("", ["mcpServers", "azure-devops"], new JsonObject { ["command"] = "x" });
        Assert.Equal("x", JsonNode.Parse(text)!["mcpServers"]!["azure-devops"]!["command"]!.GetValue<string>());
    }
}

public class AutostartTests
{
    [Fact]
    public void Toggle_run_key()
    {
        var key = new FakeRunKey();
        var a = new AutostartService(key, @"C:\Users\x\AppData\Local\AzdoMcp\current\AzdoMcp.Manager.exe");
        Assert.False(a.IsEnabled);
        a.Enable();
        Assert.True(a.IsEnabled);
        Assert.Equal("\"C:\\Users\\x\\AppData\\Local\\AzdoMcp\\current\\AzdoMcp.Manager.exe\" --minimized", key.Values[AutostartService.ValueName]);
        a.Disable();
        Assert.False(a.IsEnabled);
        Assert.Empty(key.Values);
    }

    [Fact]
    public async Task Tray_toggle_through_main_view_model()
    {
        await using var m = new ManagerHarness();
        var main = new MainViewModel(m.Ctx);
        Assert.False(main.Autostart);
        main.ToggleAutostartCommand.Execute(null);
        Assert.True(main.Autostart);
        main.ToggleAutostartCommand.Execute(null);
        Assert.False(main.Autostart);
        Assert.Empty(m.RunKey.Values);
    }
}

