using System.Text;
using AzdoMcp.Core;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Discovery;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Server;

namespace AzdoMcp.Tests;

public class ProjectDiscoveryTests
{
    private static Connection Tfs() => new() { Name = "tfs", CollectionUrl = Fake.Collection };

    private static Task<DiscoveryResult> Discover(FakeAzdo azdo, Connection? c = null) =>
        new ProjectDiscovery(new AzdoClientFactory(azdo)).DiscoverAsync(c ?? Tfs(), Fake.Pat, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Lists_all_pages_once_and_names_the_user()
    {
        var azdo = new FakeAzdo { PageSize = 1 };
        var r = await Discover(azdo);
        Assert.Equal(["Alpha", "Beta", "Gamma"], r.Projects.Select(p => p.Name));
        Assert.Equal("Max Muster", r.UserName);
        Assert.Equal(3, azdo.Requests.Count(x => x.Url.Contains("/_apis/projects?")));
    }

    [Fact]
    public async Task Effective_rights_from_security_api()
    {
        var r = await Discover(new FakeAzdo());
        Assert.True(r.UsedSecurityApi);
        var alpha = r.Projects.Single(p => p.Name == "Alpha");
        var beta = r.Projects.Single(p => p.Name == "Beta");
        var gamma = r.Projects.Single(p => p.Name == "Gamma");

        Assert.Equal(Acc.W, alpha.Effective[AccessArea.WorkItems]);
        Assert.Equal(Acc.Rn, alpha.Effective[AccessArea.Code]); // PAT: Code Schreiben not testable
        Assert.Equal(Acc.W, alpha.ProjectRights[AccessArea.Code]);
        Assert.Equal(Acc.Ro, beta.Effective[AccessArea.WorkItems]); // edit bit answered false
        Assert.Equal(Acc.Ro, beta.ProjectRights[AccessArea.Code]); // contribute denied
        Assert.Equal(Acc.N, gamma.Effective[AccessArea.WorkItems]);
        Assert.Equal(Acc.N, gamma.Effective[AccessArea.Code]);
    }

    [Fact]
    public async Task Pipelines_scope_probe_and_project_right()
    {
        var r = await Discover(new FakeAzdo());
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Pipelines]);
        Assert.Equal(Acc.Rn, r.Projects.Single(p => p.Name == "Alpha").Effective[AccessArea.Pipelines]);
        Assert.Equal(Acc.N, r.Projects.Single(p => p.Name == "Gamma").Effective[AccessArea.Pipelines]);

        var noBuild = await Discover(new FakeAzdo { PatBuild = false });
        Assert.Equal(Check.No, noBuild.PatScope[AccessArea.Pipelines].Read);
        Assert.Equal(Check.No, noBuild.Projects.Single(p => p.Name == "Alpha").Effective[AccessArea.Pipelines].Read);
    }

    [Fact]
    public async Task Builds_view_denied()
    {
        var azdo = new FakeAzdo();
        azdo.BuildDenied.Add("Beta");
        var r = await Discover(azdo);
        Assert.Equal(Check.No, r.Projects.Single(p => p.Name == "Beta").ProjectRights[AccessArea.Pipelines].Read);
        Assert.Equal(Check.Yes, r.Projects.Single(p => p.Name == "Alpha").ProjectRights[AccessArea.Pipelines].Read);
    }

    [Fact]
    public async Task Pat_scope_is_probed_where_the_user_has_rights()
    {
        var azdo = new FakeAzdo();
        var gamma = azdo.Projects[2];
        azdo.Projects.RemoveAt(2);
        azdo.Projects.Insert(0, gamma); // server lists the no-access project first
        var r = await Discover(azdo);
        Assert.Equal(Acc.W, r.PatScope[AccessArea.WorkItems]);
        Assert.Equal(Acc.W, r.Projects.Single(p => p.Name == "Alpha").Effective[AccessArea.WorkItems]);
    }

    [Fact]
    public async Task Connection_test_does_not_blame_the_pat_for_a_project_right()
    {
        var azdo = new FakeAzdo();
        var c = new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects =
            [
                new ProjectEntry { AzdoId = "id-gamma", Name = "Gamma", Alias = "Gamma", ProjectRights = Acc.All(Acc.N) },
                new ProjectEntry { AzdoId = "id-alpha", Name = "Alpha", Alias = "Alpha", ProjectRights = Acc.All(Acc.W) },
            ],
        };
        var r = await new ConnectionTester(new AzdoClientFactory(azdo)).TestAsync(c, Fake.Pat, TestContext.Current.CancellationToken);
        Assert.Equal(Acc.W, r.PatScope![AccessArea.WorkItems]);
    }

    [Fact]
    public async Task Denied_read_implies_denied_write()
    {
        var azdo = new FakeAzdo();
        azdo.Projects[0] = azdo.Projects[0] with { GitRead = false }; // Alpha: contribute bit set, read denied
        var r = await Discover(azdo);
        Assert.Equal(Acc.N, r.Projects.Single(p => p.Name == "Alpha").ProjectRights[AccessArea.Code]);
    }

    [Fact]
    public async Task Full_access_pat_shows_untestable_writes_as_na()
    {
        var r = await Discover(new FakeAzdo());
        Assert.Equal(Acc.W, r.PatScope[AccessArea.WorkItems]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Code]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Test]);
        Assert.Equal(Acc.Rn, r.PatScope[AccessArea.Iterations]);
    }

    [Fact]
    public async Task Other_probe_failure_is_not_a_denial()
    {
        var r = await Discover(new FakeAzdo { IterationsMissing = true });
        Assert.Equal(new AreaAccess(Check.NotTestable, Check.NotTestable), r.PatScope[AccessArea.Iterations]);
    }

    [Fact]
    public async Task Pat_scope_limits_even_when_project_allows()
    {
        var r = await Discover(new FakeAzdo { PatWorkItemsWrite = false });
        Assert.Equal(Acc.Ro, r.PatScope[AccessArea.WorkItems]);
        Assert.Equal(Acc.Ro, r.Projects.Single(p => p.Name == "Alpha").Effective[AccessArea.WorkItems]);
    }

    [Fact]
    public async Task Fallback_to_read_probes_without_security_api()
    {
        var r = await Discover(new FakeAzdo { SecurityApi = false });
        Assert.False(r.UsedSecurityApi);
        Assert.NotEmpty(r.Notes);
        var alpha = r.Projects.Single(p => p.Name == "Alpha");
        Assert.Equal(Acc.Rn, alpha.ProjectRights[AccessArea.WorkItems]); // every Schreiben is n/a
        Assert.Equal(Acc.Rn, alpha.Effective[AccessArea.WorkItems]);
        Assert.Equal(Acc.N, r.Projects.Single(p => p.Name == "Gamma").Effective[AccessArea.WorkItems]);
    }

    [Fact]
    public async Task Only_write_request_is_validate_only()
    {
        var azdo = new FakeAzdo();
        await Discover(azdo);
        var nonGet = azdo.Requests.Where(x => x.Method != HttpMethod.Get).ToList();
        Assert.All(nonGet.Where(x => x.Url.Contains("/_apis/wit/workitems/")), x => Assert.Contains("validateOnly=true", x.Url));
        Assert.DoesNotContain(azdo.Requests, x => x.Method == HttpMethod.Patch || x.Method == HttpMethod.Delete || x.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Expired_pat_stops_discovery()
    {
        var http = new FakeHttp().On("_apis/connectionData", "{}", System.Net.HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<AzdoApiException>(() =>
            new ProjectDiscovery(new AzdoClientFactory(http)).DiscoverAsync(Tfs(), Fake.Pat, TestContext.Current.CancellationToken));
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain(http.Requests, r => r.Url.Contains("/_apis/projects"));
    }
}

public class DiscoveryMergerTests
{
    private static AreaRights R(AreaAccess wi, AreaAccess? code = null, AreaAccess? test = null) => Acc.Of(wi, code, test);

    private static DiscoveredProject D(string id, string name, AreaRights rights) => new(id, name, rights, rights);

    private static DiscoveryResult Result(params DiscoveredProject[] projects) =>
        new(DateTimeOffset.UtcNow, "u", "Max Muster", Acc.All(Acc.W), projects, true, []);

    private static Connection Stored() => new()
    {
        Name = "tfs",
        CollectionUrl = Fake.Collection,
        Projects =
        [
            new ProjectEntry { Key = "k-shop", AzdoId = "id-shop", Name = "SHOP", Alias = "SHOP", IsDefault = true, AllowWrite = true, ProjectRights = R(Acc.W) },
            new ProjectEntry { Key = "k-ops", AzdoId = "id-ops", Name = "OPS", Alias = "OPS", ProjectRights = R(Acc.Ro) },
            new ProjectEntry { Key = "k-qa", AzdoId = "id-qa", Name = "QA", Alias = "QA", ProjectRights = R(Acc.Ro) },
        ],
    };

    [Fact]
    public void Diff_groups_new_missing_changed_unchanged()
    {
        var diff = DiscoveryMerger.Diff(Stored(), Result(
            D("id-shop", "SHOP", R(Acc.W)),
            D("id-ops", "OPS", R(Acc.W)),
            D("id-new", "Sandbox", R(Acc.W))));

        Assert.Equal(["Sandbox"], diff.New.Select(n => n.Name));
        Assert.Equal(["QA"], diff.Missing.Select(m => m.Alias));
        var change = Assert.Single(diff.Changed);
        Assert.Equal("OPS", change.Project.Alias);
        Assert.Equal(new AreaChange(AccessArea.WorkItems, AccessColumn.Write, Check.No, Check.Yes), change.Changes.Single());
        Assert.Equal(["SHOP"], diff.Unchanged.Select(u => u.Alias));
        Assert.Equal(3, diff.ChangeCount);
    }

    [Fact]
    public void Untestable_change_is_not_reported_but_stored()
    {
        var stored = Stored();
        stored.Projects[0].ProjectRights![AccessArea.Code] = AreaAccess.Unknown; // "?"
        var now = R(Acc.W);
        var diff = DiscoveryMerger.Diff(stored, Result(D("id-shop", "SHOP", now)));
        Assert.Contains(diff.Unchanged, u => u.Alias == "SHOP");

        var merged = DiscoveryMerger.Apply(stored, Result(D("id-shop", "SHOP", now)), new Dictionary<string, ProjectChoice>());
        Assert.Equal(Acc.Rn, merged.Connection.Projects.Single(p => p.Alias == "SHOP").ProjectRights![AccessArea.Code]);
    }

    [Fact]
    public void Change_to_nein_is_reported_per_column()
    {
        var stored = Stored();
        stored.PatScope = Acc.All(Acc.W);
        var diff = DiscoveryMerger.Diff(stored, Result(D("id-shop", "SHOP", R(Acc.Ro))));
        var change = Assert.Single(Assert.Single(diff.Changed).Changes);
        Assert.Equal("Work Items Schreiben: ja → nein", change.Text);
    }

    [Fact]
    public void Untestable_write_allows_the_write_switch()
    {
        var merged = DiscoveryMerger.Apply(Stored(), Result(D("id-n", "Neu", R(Acc.Rn))),
            new Dictionary<string, ProjectChoice> { ["id-n"] = new("neu", AllowWrite: true) });
        Assert.True(merged.Connection.Projects.Single(p => p.Alias == "neu").AllowWrite);
    }

    [Fact]
    public void Apply_never_overwrites_existing_settings()
    {
        var stored = Stored();
        stored.Projects[0].Alias = "dv";
        var merged = DiscoveryMerger.Apply(stored, Result(D("id-shop", "SHOP-renamed", R(Acc.Ro))), new Dictionary<string, ProjectChoice>());
        var shop = merged.Connection.Projects.Single(p => p.Key == "k-shop");
        Assert.Equal("dv", shop.Alias);
        Assert.True(shop.IsDefault);
        Assert.True(shop.AllowWrite);
        Assert.Equal("SHOP-renamed", shop.Name);
        Assert.Equal(Acc.Ro, shop.ProjectRights![AccessArea.WorkItems]);
    }

    [Fact]
    public void More_rights_never_enable_writing()
    {
        var merged = DiscoveryMerger.Apply(Stored(), Result(D("id-ops", "OPS", R(Acc.W))), new Dictionary<string, ProjectChoice>());
        Assert.False(merged.Connection.Projects.Single(p => p.Alias == "OPS").AllowWrite);
    }

    [Fact]
    public void Missing_projects_are_marked_not_deleted()
    {
        var merged = DiscoveryMerger.Apply(Stored(), Result(D("id-shop", "SHOP", R(Acc.W))), new Dictionary<string, ProjectChoice>());
        Assert.Equal(3, merged.Connection.Projects.Count);
        Assert.False(merged.Connection.Projects.Single(p => p.Alias == "QA").Visible);
        Assert.True(merged.Connection.Projects.Single(p => p.Alias == "SHOP").Visible);
    }

    [Fact]
    public void New_projects_with_choices_and_write_needs_right()
    {
        var modes = new Dictionary<ToolGroup, PermissionMode> { [ToolGroup.Git] = PermissionMode.Ask };
        var merged = DiscoveryMerger.Apply(Stored(), Result(
                D("id-a", "Alpha", R(Acc.W)),
                D("id-b", "Beta", R(Acc.Ro))),
            new Dictionary<string, ProjectChoice>
            {
                ["id-a"] = new("alpha", AllowWrite: true, ChangedModes: modes),
                ["id-b"] = new("beta", AllowWrite: true),
            });

        var alpha = merged.Connection.Projects.Single(p => p.Alias == "alpha");
        Assert.True(alpha.AllowWrite);
        Assert.False(merged.Connection.Projects.Single(p => p.Alias == "beta").AllowWrite);
        Assert.Equal(PermissionMode.Ask, merged.ProjectModes[$"{merged.Connection.Id}/{alpha.Key}"][ToolGroup.Git]);
        Assert.Single(merged.ProjectModes);
    }

    [Fact]
    public void Migrated_entry_is_matched_by_name_and_gets_its_id()
    {
        var stored = Stored();
        stored.Projects[1].AzdoId = "";
        var merged = DiscoveryMerger.Apply(stored, Result(D("id-ops-real", "ops", R(Acc.Ro))), new Dictionary<string, ProjectChoice>());
        var ops = merged.Connection.Projects.Single(p => p.Key == "k-ops");
        Assert.Equal("id-ops-real", ops.AzdoId);
        Assert.True(ops.Visible);
    }

    [Fact]
    public void Default_choices_avoid_alias_clashes()
    {
        using var h = new Harness("SHOP");
        var diff = DiscoveryMerger.Diff(new Connection { Name = "x", CollectionUrl = Fake.Collection }, Result(D("x1", "SHOP", R(Acc.Ro)), D("x2", "SHOP", R(Acc.Ro))));
        var choices = DiscoveryMerger.DefaultChoices(diff, h.Store.SuggestAlias);
        Assert.Equal(["SHOP2", "SHOP3"], choices.Values.Select(c => c.Alias).Order());
    }
}

public class DiscoverCommandTests
{
    private static async Task<(int Code, string Out, string Err)> Run(Harness h, FakeAzdo azdo, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await DiscoverCommand.RunAsync(["discover", .. args], h.Store, new AzdoClientFactory(azdo), o, e, TestContext.Current.CancellationToken);
        return (code, o.ToString(), e.ToString());
    }

    private static Harness WithTfs()
    {
        var h = new Harness();
        h.Store.Save(new Connection { Name = "tfs", CollectionUrl = Fake.Collection }, Fake.Pat);
        return h;
    }

    [Fact]
    public async Task Dry_run_prints_table_and_changes_nothing()
    {
        using var h = WithTfs();
        var before = File.ReadAllText(h.Dir.AppPaths.ConnectionsFile);
        var (code, output, _) = await Run(h, new FakeAzdo(), "tfs");
        Assert.Equal(0, code);
        Assert.Contains("Alpha", output);
        Assert.Contains("Gamma", output);
        Assert.Contains("3 neu", output);
        Assert.Contains("PAT-Scope (Lesen/Schreiben): Work Items ja/ja · Code ja/n/a", output);
        Assert.Contains("ja/nein", output); // Beta: Work Items Schreiben denied
        Assert.Equal(before, File.ReadAllText(h.Dir.AppPaths.ConnectionsFile));
    }

    [Fact]
    public async Task Apply_adds_new_projects_with_defaults()
    {
        using var h = WithTfs();
        var (code, _, _) = await Run(h, new FakeAzdo(), "tfs", "--apply");
        Assert.Equal(0, code);
        var projects = h.Store.AllProjects();
        Assert.Equal(["ALPHA", "BETA", "GAMMA"], projects.Select(p => p.Alias).Order());
        Assert.All(projects, p => Assert.False(p.Project.AllowWrite));
        Assert.Equal(Acc.Rn, h.Proj("Alpha").Effective[AccessArea.Code]);
        Assert.NotNull(h.Conn("Alpha").LastDiscovery);
    }

    [Fact]
    public async Task Apply_keeps_existing_settings()
    {
        using var h = WithTfs();
        await Run(h, new FakeAzdo(), "tfs", "--apply");
        h.SetProject("Alpha", p =>
        {
            p.Alias = "AA";
            p.AllowWrite = true;
        });
        var azdo = new FakeAzdo();
        azdo.Projects.RemoveAll(p => p.Name == "Beta");
        await Run(h, azdo, "tfs", "--apply");

        Assert.True(h.Proj("AA").Project.AllowWrite);
        Assert.False(h.Proj("Beta").Project.Visible);
        Assert.Equal(3, h.Store.AllProjects().Count);
    }

    [Fact]
    public async Task Unknown_connection_exit_1_with_names()
    {
        using var h = WithTfs();
        var (code, _, err) = await Run(h, new FakeAzdo(), "nope");
        Assert.Equal(1, code);
        Assert.Contains("Unbekannte Verbindung „nope“", err);
        Assert.Contains("tfs", err);
    }

    [Fact]
    public async Task Pat_never_in_output_log_or_files()
    {
        using var h = WithTfs();
        var (_, output, err) = await Run(h, new FakeAzdo(), "tfs", "--apply");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + Fake.Pat));
        Assert.DoesNotContain(Fake.Pat, output + err);
        Assert.DoesNotContain(basic, output + err);
        await h.Call("list_projects");
        await h.Call("get_work_item", new { id = 1, project = "Alpha" });
        foreach (var file in Directory.EnumerateFiles(h.Dir.Path, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain(Fake.Pat, text);
            Assert.DoesNotContain(basic, text);
        }
    }
}

public class ProjectSelectionTests
{
    [Fact]
    public async Task List_projects_returns_only_configured_projects_with_rights()
    {
        using var h = new Harness();
        h.Store.Save(new Connection { Name = "tfs", CollectionUrl = Fake.Collection }, Fake.Pat);
        var azdo = new FakeAzdo();
        await DiscoverCommand.RunAsync(["discover", "tfs", "--apply"], h.Store, new AzdoClientFactory(azdo), TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken);
        h.SetConn("Alpha", c => c.Projects.RemoveAll(p => p.Alias == "GAMMA"));

        var r = await h.Call("list_projects");
        var json = System.Text.Json.Nodes.JsonNode.Parse(r.Text)!;
        Assert.Equal(2, json["count"]!.GetValue<int>());
        var alpha = json["projects"]!.AsArray().Single(p => p!["project"]!.GetValue<string>() == "ALPHA")!;
        Assert.Equal("ja", alpha["effectiveRights"]!["WorkItems"]!["write"]!.GetValue<string>());
        Assert.Equal(("ja", "n/a"), (alpha["effectiveRights"]!["Code"]!["read"]!.GetValue<string>(), alpha["effectiveRights"]!["Code"]!["write"]!.GetValue<string>()));
        Assert.Equal("nur Lesen · Schreibschalter aus", alpha["status"]!.GetValue<string>());
        Assert.Equal("tfs", alpha["connection"]!.GetValue<string>());
        Assert.False(alpha["writeSwitch"]!.GetValue<bool>());
        Assert.DoesNotContain(Fake.Pat, r.Text);
    }

    [Fact]
    public async Task Calls_use_the_project_of_the_alias()
    {
        using var h = new Harness();
        h.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects = [new ProjectEntry { Name = "Demo Project One", Alias = "SHOP" }, new ProjectEntry { Name = "Infrastructure", Alias = "OPS" }],
        }, Fake.Pat);
        h.Http.On("/Infrastructure/_apis/wit/workitems/5", """{"id":5,"fields":{}}""");
        var r = await h.Call("get_work_item", new { id = 5, project = "ops" });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("/Infrastructure/", h.Http.Calls.Single().Url);
    }
}
