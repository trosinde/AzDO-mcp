using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Server;

namespace AzdoMcp.Tests;

public class PermissionHierarchyTests
{
    private static ToolDefinition T(string name) => ToolCatalog.Find(name)!;

    /// <summary>Project with known rights (default: may write everything) and write switch on.</summary>
    private static ProjectRef P(string alias = "SHOP", AreaAccess? wi = null, AreaAccess? test = null, bool write = true, bool visible = true, string conn = "tfs")
    {
        var project = new ProjectEntry { Key = alias.ToLowerInvariant(), Name = alias, Alias = alias, AllowWrite = write, Visible = visible };
        project.ProjectRights = Acc.Of(wi ?? Acc.W, Acc.Rn, test ?? Acc.Rn, Acc.Rn);
        var c = new Connection { Id = conn, Name = conn, Projects = [project], PatScope = Acc.Of(Acc.W) };
        return new ProjectRef(c, project);
    }

    [Fact]
    public void Fresh_install_defaults()
    {
        var s = new PermissionSettings();
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(T("create_work_item"), P(), s).Mode);
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(T("add_work_item_comment"), P(), s).Mode);
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("get_work_item"), P(), s).Mode);
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("get_commits"), P(), s).Mode);
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("get_iterations"), P(), s).Mode);
    }

    [Fact]
    public void Tool_inherits_group_mode()
    {
        var s = new PermissionSettings { Groups = { [ToolGroup.Git] = PermissionMode.Ask } };
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(T("get_commits"), P(), s).Mode);
    }

    [Fact]
    public void Tool_overrides_group()
    {
        var s = new PermissionSettings
        {
            Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Ask },
            Tools = { ["add_work_item_comment"] = PermissionMode.Allowed },
        };
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("add_work_item_comment"), P(), s).Mode);
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(T("update_work_item"), P(), s).Mode);
    }

    [Fact]
    public void Project_overrides_tool()
    {
        var musterlab = P("Musterlab");
        var shop = P("SHOP");
        var s = new PermissionSettings { Tools = { ["update_work_item"] = PermissionMode.Ask } };
        s.ForProject(musterlab.PermissionKey).Tools["update_work_item"] = PermissionMode.Allowed;
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("update_work_item"), musterlab, s).Mode);
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(T("update_work_item"), shop, s).Mode);
    }

    [Fact]
    public void Resolution_order_deepest_first()
    {
        var p = P();
        var tool = T("update_work_item");
        var s = new PermissionSettings
        {
            Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Blocked },
            Tools = { ["update_work_item"] = PermissionMode.Ask },
        };
        Assert.Equal(ModeSource.Tool, PermissionEvaluator.Resolve(s, tool, p.Connection.Id, p.PermissionKey).Source);
        s.ForConnection(p.Connection.Id).Groups[ToolGroup.WorkItemsWrite] = PermissionMode.Allowed;
        Assert.Equal(ModeSource.ConnectionGroup, PermissionEvaluator.Resolve(s, tool, p.Connection.Id, p.PermissionKey).Source);
        s.ForConnection(p.Connection.Id).Tools["update_work_item"] = PermissionMode.Ask;
        Assert.Equal(ModeSource.ConnectionTool, PermissionEvaluator.Resolve(s, tool, p.Connection.Id, p.PermissionKey).Source);
        s.ForProject(p.PermissionKey).Groups[ToolGroup.WorkItemsWrite] = PermissionMode.Allowed;
        Assert.Equal((PermissionMode.Allowed, ModeSource.ProjectGroup), PermissionEvaluator.Resolve(s, tool, p.Connection.Id, p.PermissionKey));
        s.ForProject(p.PermissionKey).Tools["update_work_item"] = PermissionMode.Blocked;
        Assert.Equal((PermissionMode.Blocked, ModeSource.ProjectTool), PermissionEvaluator.Resolve(s, tool, p.Connection.Id, p.PermissionKey));
    }

    [Fact]
    public void System_group_is_always_allowed()
    {
        var s = new PermissionSettings { Groups = { [ToolGroup.System] = PermissionMode.Blocked }, Tools = { ["ping"] = PermissionMode.Blocked } };
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("ping"), P(), s).Mode);
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("list_projects"), null, s).Mode);
    }

    [Fact]
    public void Write_switch_off_blocks_write_tools()
    {
        var d = PermissionEvaluator.Evaluate(T("update_work_item"), P("OPS", write: false), new PermissionSettings());
        Assert.Equal(PermissionMode.Blocked, d.Mode);
        Assert.StartsWith("Für OPS gesperrt", d.Reason);
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("get_work_item"), P("OPS", write: false), new PermissionSettings()).Mode);
    }

    [Fact]
    public void Read_only_project_blocks_writing_even_with_switch_on()
    {
        var s = new PermissionSettings { Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Allowed } };
        Assert.Equal(PermissionMode.Blocked, PermissionEvaluator.Evaluate(T("update_work_item"), P("OPS", wi: Acc.Ro), s).Mode);
    }

    [Fact]
    public void No_access_blocks_group_with_403()
    {
        var d = PermissionEvaluator.Evaluate(T("get_test_runs"), P(test: Acc.N), new PermissionSettings());
        Assert.Equal(PermissionMode.Blocked, d.Mode);
        Assert.StartsWith("403 Scope fehlt", d.Reason);
    }

    [Fact]
    public void Pat_scope_limits_project_right()
    {
        var p = P("SHOP");
        p.Connection.PatScope = Acc.All(Acc.Ro);
        Assert.Equal(Acc.Ro, p.Effective[AccessArea.WorkItems]);
        Assert.Equal(PermissionMode.Blocked, PermissionEvaluator.Evaluate(T("create_work_item"), p, new PermissionSettings()).Mode);
    }

    [Fact]
    public void Untestable_write_does_not_block()
    {
        var p = P("Musterlab", wi: Acc.Rn);
        var s = new PermissionSettings { Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Ask } };
        Assert.Equal(PermissionMode.Ask, PermissionEvaluator.Evaluate(T("update_work_item"), p, s).Mode);
    }

    [Fact]
    public void Unknown_rights_do_not_block()
    {
        var p = P("SHOP");
        p.Connection.PatScope = null;
        p.Project.ProjectRights = null;
        Assert.Equal(PermissionMode.Allowed, PermissionEvaluator.Evaluate(T("get_test_runs"), p, new PermissionSettings()).Mode);
    }

    [Fact]
    public void Not_visible_project_rejects_everything()
    {
        var d = PermissionEvaluator.Evaluate(T("get_work_item"), P(visible: false), new PermissionSettings());
        Assert.Equal(PermissionMode.Blocked, d.Mode);
        Assert.StartsWith("kein Zugriff", d.Reason);
    }

    [Fact]
    public void Mode_never_extends_effective_right()
    {
        var p = P("OPS", wi: Acc.Ro);
        var s = new PermissionSettings();
        s.ForProject(p.PermissionKey).Tools["update_work_item"] = PermissionMode.Allowed;
        Assert.Equal(PermissionMode.Blocked, PermissionEvaluator.Evaluate(T("update_work_item"), p, s).Mode);
    }

    [Fact]
    public void Connection_level_block_message()
    {
        var p = P("SHOP", conn: "tfs");
        var s = new PermissionSettings();
        s.ForConnection("tfs").Groups[ToolGroup.Git] = PermissionMode.Blocked;
        Assert.Equal("Für tfs gesperrt", PermissionEvaluator.Evaluate(T("get_commits"), p, s).Reason);
    }

    [Fact]
    public void Store_round_trip_and_v1_connection_blocks_migrate()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "permissions.json"), """{"groups":{"Test":"Blocked"},"connectionBlocks":{"c1":["Git"]}}""");
        var store = new PermissionStore(dir.AppPaths);
        var s = store.Get();
        Assert.Equal(PermissionMode.Blocked, s.Groups[ToolGroup.Test]);
        Assert.Equal(PermissionMode.Blocked, s.Connections["c1"].Groups[ToolGroup.Git]);

        s.ForProject("c1/p1");
        store.Save(s);
        var text = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("connectionBlocks", text);
        Assert.DoesNotContain("c1/p1", text); // empty scopes are not written
    }
}

public class ToolListingTests
{
    [Fact]
    public void Globally_blocked_group_is_not_listed()
    {
        using var h = new Harness("SHOP");
        h.Permissions.Save(new PermissionSettings { Groups = { [ToolGroup.Test] = PermissionMode.Blocked } });
        var names = h.Executor.VisibleTools().Select(t => t.Name).ToList();
        Assert.DoesNotContain("get_test_runs", names);
        Assert.DoesNotContain("get_test_results", names);
        Assert.DoesNotContain("analyze_test_results", names);
        Assert.Equal(27, names.Count);
    }

    [Fact]
    public void Partially_blocked_tool_names_the_projects()
    {
        using var h = new Harness("SHOP", "OPS");
        h.SetProject("OPS", p => p.AllowWrite = false);
        var tool = h.Executor.VisibleTools().Single(t => t.Name == "update_work_item");
        Assert.EndsWith("Nicht verfügbar für: OPS.", tool.Description);
    }

    [Fact]
    public void Blocked_for_every_project_is_not_listed()
    {
        using var h = new Harness("SHOP");
        h.SetConn("SHOP", c => c.PatScope = Acc.Of(Acc.W, code: Acc.N));
        Assert.DoesNotContain(h.Executor.VisibleTools(), t => t.Group == ToolGroup.Git);
    }

    [Fact]
    public async Task List_changed_is_sent_only_when_the_set_differs()
    {
        using var h = new Harness("SHOP");
        var sent = 0;
        using var monitor = new ToolListMonitor(h.Executor, h.Dir.AppPaths, _ =>
        {
            Interlocked.Increment(ref sent);
            return Task.CompletedTask;
        });

        Assert.False(await monitor.CheckAsync(TestContext.Current.CancellationToken));
        h.Permissions.Save(new PermissionSettings { Groups = { [ToolGroup.Git] = PermissionMode.Blocked } });
        Assert.True(await monitor.CheckAsync(TestContext.Current.CancellationToken));
        Assert.False(await monitor.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, sent);
    }

    [Fact]
    public async Task Watcher_triggers_list_changed_after_file_edit()
    {
        using var h = new Harness("SHOP");
        var sent = 0;
        using var monitor = new ToolListMonitor(h.Executor, h.Dir.AppPaths, _ =>
        {
            Interlocked.Increment(ref sent);
            return Task.CompletedTask;
        });

        h.Permissions.Save(new PermissionSettings { Groups = { [ToolGroup.Planning] = PermissionMode.Blocked } });
        await Eventually.True(() => Volatile.Read(ref sent) == 1);
    }
}

public class BlockedCallTests
{
    [Fact]
    public async Task Globally_blocked_tool_is_rejected_and_logged()
    {
        using var h = new Harness("SHOP");
        h.Permissions.Save(new PermissionSettings { Groups = { [ToolGroup.Test] = PermissionMode.Blocked } });
        var r = await h.Call("get_test_runs");
        Assert.True(r.IsError);
        Assert.Contains("Tool gesperrt", r.Text);
        Assert.Empty(h.Http.Calls);
        var rec = h.Records().Single();
        Assert.Equal(AuditOutcome.Blocked, rec.Outcome);
        Assert.Equal("gesperrt", rec.Result);
    }

    [Fact]
    public async Task Write_switch_off_rejects_without_calling_azure_devops()
    {
        using var h = new Harness("default", "OPS");
        h.SetProject("OPS", p => p.AllowWrite = false);
        var r = await h.Call("update_work_item", new { id = 1, project = "OPS", state = "Closed" });
        Assert.True(r.IsError);
        Assert.Contains("Für OPS gesperrt", r.Text);
        Assert.Empty(h.Http.Calls);
        Assert.Empty(h.Manager.Requests);
    }

    [Fact]
    public async Task Missing_scope_rejects_with_403()
    {
        using var h = new Harness("SHOP");
        h.SetConn("SHOP", c => c.PatScope = Acc.Of(Acc.W, test: Acc.N));
        var r = await h.Call("get_test_runs", new { project = "SHOP" });
        Assert.True(r.IsError);
        Assert.Contains("403 Scope fehlt", r.Text);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task Corrupt_permission_file_fails_closed()
    {
        using var h = new Harness("SHOP");
        File.WriteAllText(h.Permissions.FilePath, "{ kaputt");
        var r = await h.Call("get_work_item", new { id = 1 });
        Assert.True(r.IsError);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task Unknown_alias_is_an_error_even_if_project_exists_in_collection()
    {
        using var h = new Harness("SHOP");
        var r = await h.Call("get_work_item", new { id = 1, project = "Musterlab" });
        Assert.True(r.IsError);
        Assert.Contains("Unbekannter Kurzname „Musterlab“. Eingerichtet: SHOP", r.Text);
        Assert.Empty(h.Http.Calls);
    }
}
