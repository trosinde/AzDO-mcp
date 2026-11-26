using System.Text.Json.Nodes;
using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Manager.Core.ViewModels;

namespace AzdoMcp.Tests;

public class ShortNameTests
{
    [Theory]
    [InlineData("SHOP")]
    [InlineData("ops")]
    [InlineData("QA-2")]
    [InlineData("ABCDEFGHIJ")]
    public void Valid_short_names(string value) => Assert.Null(ShortName.Validate(value));

    [Theory]
    [InlineData("X")]
    [InlineData("Demo Project")]
    [InlineData("2019")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("Demo_Project")]
    public void Invalid_short_names_show_the_rule(string value) => Assert.Equal(ShortName.RuleText, ShortName.Validate(value));

    [Theory]
    [InlineData("test", "„TEST“ ist reserviert")]
    [InlineData("PR", "„PR“ ist reserviert")]
    public void Reserved_words(string value, string message) => Assert.Equal(message, ShortName.Validate(value));

    [Theory]
    [InlineData("Demo Project One", "DPO")]
    [InlineData("Ops", "OPS")]
    [InlineData("QA-Automation", "QA")]
    [InlineData("Test", "TEST2")]
    [InlineData("2019", "P2019")]
    [InlineData("Sandbox", "SANDBOX")]
    [InlineData("Musterlab", "MUSTE")]
    [InlineData("Archiv-2019", "A2019")]
    [InlineData("Musterlab-Firmware", "MF")]
    public void Suggestion(string name, string expected) => Assert.Equal(expected, ShortName.Suggest(name));

    [Fact]
    public void Suggestion_appends_digit_on_clash() => Assert.Equal("OPS2", ShortName.Suggest("Ops", ["ops"]));
}

public class ShortNameStoreTests
{
    private static Connection Conn(string alias) => new()
    {
        Name = "tfs",
        CollectionUrl = Fake.Collection,
        Projects = [new ProjectEntry { Name = "Infrastructure", Alias = alias }],
    };

    [Fact]
    public void Stored_in_capitals_and_found_in_any_case()
    {
        using var h = new Harness();
        h.Store.Save(Conn("ops"), Fake.Pat);
        Assert.Equal("OPS", h.Store.AllProjects().Single().Alias);
        Assert.NotNull(h.Store.FindProject("ops"));
    }

    [Theory]
    [InlineData("Demo Project", ShortName.RuleText)]
    [InlineData("X", ShortName.RuleText)]
    [InlineData("test", "„TEST“ ist reserviert")]
    public void Invalid_short_name_is_rejected(string alias, string message)
    {
        using var h = new Harness();
        var ex = Assert.Throws<ConnectionValidationException>(() => h.Store.Save(Conn(alias), Fake.Pat));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Clash_message_uses_kurzname()
    {
        using var h = new Harness("SHOP");
        var ex = Assert.Throws<ConnectionValidationException>(() => h.Store.Save(Conn("shop"), Fake.Pat));
        Assert.Equal("Kurzname „SHOP“ wird bereits verwendet", ex.Message);
    }

    [Fact]
    public void Legacy_alias_stays_valid_until_edited()
    {
        using var dir = new TempDir();
        var protector = new FakeProtector();
        File.WriteAllText(dir.AppPaths.ConnectionsFile, """
            {"version":3,"connections":[{"id":"c1","name":"tfs","serverType":"Server","collectionUrl":"https://azdo.example.local/DefaultCollection",
              "projects":[{"key":"k1","name":"Demo Project One","alias":"Demo_Project_One","isDefault":true,"allowWrite":true,"visible":true}]}]}
            """);
        Directory.CreateDirectory(dir.AppPaths.SecretsDirectory);
        File.WriteAllBytes(dir.AppPaths.SecretFile("c1"), protector.Protect(System.Text.Encoding.UTF8.GetBytes(Fake.Pat)));
        var store = new ConnectionStore(dir.AppPaths, protector);

        var c = store.GetAll().Single();
        c.ReminderDays = 14;
        store.Save(c, null); // unchanged legacy alias passes
        Assert.Equal("Demo_Project_One", store.AllProjects().Single().Alias);

        c = store.GetAll().Single();
        c.Projects[0].Alias = "Demo_Project_Two";
        Assert.Throws<ConnectionValidationException>(() => store.Save(c, null));
    }
}

public class ShortNameServerTests
{
    private static Harness TwoProjects()
    {
        var h = new Harness();
        h.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects =
            [
                new ProjectEntry { Name = "Demo Project One", Alias = "SHOP", IsDefault = true, AllowWrite = true },
                new ProjectEntry { Name = "Infrastructure", Alias = "OPS", AllowWrite = true },
                new ProjectEntry { Name = "QA-Automation", Alias = "QA", Visible = false },
            ],
        }, Fake.Pat);
        return h;
    }

    [Fact]
    public void Instructions_list_projects_default_and_example()
    {
        using var h = TwoProjects();
        var text = h.Executor.Instructions();
        Assert.Contains("SHOP = Demo Project One (tfs)", text);
        Assert.Contains("OPS = Infrastructure (tfs)", text);
        Assert.Contains("Standardprojekt: SHOP", text);
        Assert.Contains("Nennt der Nutzer einen dieser Kurznamen, ist dieses Azure-DevOps-Projekt gemeint", text);
        Assert.Contains("„gib mir aus SHOP das Work Item 4711“ → get_work_item mit project=\"SHOP\", id=4711", text);
        Assert.DoesNotContain("QA-Automation", text); // not visible
        Assert.DoesNotContain(Fake.Collection, text);
        Assert.DoesNotContain(Fake.Pat, text);
    }

    [Fact]
    public void Instructions_without_projects()
    {
        using var h = new Harness();
        Assert.Contains("Noch kein Projekt eingerichtet", h.Executor.Instructions());
    }

    [Fact]
    public void Project_parameter_lists_short_names()
    {
        using var h = TwoProjects();
        var tool = h.Executor.VisibleTools().Single(t => t.Name == "get_work_item");
        var project = tool.InputSchema["properties"]!["project"]!;
        Assert.Equal(["SHOP", "OPS"], project["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains("SHOP = Demo Project One", project["description"]!.GetValue<string>());
        Assert.Contains("Standardprojekt SHOP", project["description"]!.GetValue<string>());
    }

    [Fact]
    public void Rename_changes_the_tool_list_signature()
    {
        using var h = TwoProjects();
        var before = ToolListing.Signature(h.Executor.VisibleTools());
        h.SetProject("OPS", p => p.Alias = "INF");
        Assert.NotEqual(before, ToolListing.Signature(h.Executor.VisibleTools()));
        Assert.Contains("INF", h.Executor.VisibleTools().Single(t => t.Name == "get_work_item").InputSchema["properties"]!["project"]!["enum"]!.ToJsonString());
    }
}

public class WorkItemProjectCheckTests
{
    private static Harness Setup()
    {
        var h = new Harness();
        h.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects =
            [
                new ProjectEntry { Name = "Demo Project One", Alias = "SHOP", IsDefault = true, AllowWrite = true },
                new ProjectEntry { Name = "Infrastructure", Alias = "OPS", AllowWrite = false },
            ],
        }, Fake.Pat);
        h.Http.On("4711?fields=System.TeamProject", """{"id":4711,"fields":{"System.TeamProject":"Demo Project One"}}""");
        h.Http.On("5000?fields=System.TeamProject", """{"id":5000,"fields":{"System.TeamProject":"Infrastructure"}}""");
        h.Http.On("6000?fields=System.TeamProject", """{"id":6000,"fields":{"System.TeamProject":"Secret Project"}}""");
        return h;
    }

    [Fact]
    public async Task Own_item_is_returned()
    {
        using var h = Setup();
        h.Http.On("/Demo Project One/_apis/wit/workitems/4711?", """{"id":4711,"fields":{"System.Title":"T"}}""");
        var r = await h.Call("get_work_item", new { project = "SHOP", id = 4711 });
        Assert.False(r.IsError, r.Text);
    }

    [Fact]
    public async Task Item_of_configured_project_names_it()
    {
        using var h = Setup();
        var r = await h.Call("get_work_item", new { project = "SHOP", id = 5000 });
        Assert.True(r.IsError);
        Assert.Equal("Work Item 5000 gehört nicht zu SHOP – es liegt in OPS", r.Text);
        var record = h.Records().Last();
        Assert.Equal(AuditOutcome.Blocked, record.Outcome);
        Assert.Equal("abgelehnt", record.Result);
        Assert.Empty(h.Http.Calls);
    }

    [Fact]
    public async Task Unconfigured_project_is_not_named()
    {
        using var h = Setup();
        var r = await h.Call("get_work_item", new { project = "SHOP", id = 6000 });
        Assert.Equal("Work Item 6000 gehört nicht zu SHOP", r.Text);
        Assert.DoesNotContain("Secret", r.Text);
    }

    [Fact]
    public async Task No_write_and_no_approval_across_projects()
    {
        using var h = Setup();
        h.Permissions.Save(new PermissionSettings { Groups = { [ToolGroup.WorkItemsWrite] = PermissionMode.Ask } });
        var r = await h.Call("update_work_item", new { project = "SHOP", id = 5000, fields = new Dictionary<string, object> { ["System.Title"] = "x" } });
        Assert.True(r.IsError);
        Assert.Empty(h.Manager.Requests);
        Assert.DoesNotContain(h.Http.Requests, x => x.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Batch_leaves_out_foreign_items()
    {
        using var h = Setup();
        h.Http.On("workitemsbatch", """
            {"value":[{"id":1,"fields":{"System.TeamProject":"Demo Project One"}},{"id":2,"fields":{"System.TeamProject":"Demo Project One"}},{"id":3,"fields":{"System.TeamProject":"Infrastructure"}}]}
            """);
        var r = await h.Call("get_work_items_with_relations", new { project = "SHOP", ids = new[] { 1, 2, 3 } });
        Assert.False(r.IsError, r.Text);
        var json = JsonNode.Parse(r.Text)!;
        Assert.Equal(2, json["count"]!.GetValue<int>());
        Assert.Equal("Nicht in SHOP, ausgelassen: 3", json["note"]!.GetValue<string>());
    }
}

public class ShortNameManagerTests
{
    private static async Task<SetupWizardViewModel> AtFirstProject(ManagerHarness m)
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
    public async Task Wizard_suggests_and_previews_the_short_name()
    {
        await using var m = new ManagerHarness(http: new FakeAzdo());
        var w = await AtFirstProject(m);
        var s = w.CurrentSetup!;
        Assert.Equal("ALPHA", s.Alias);
        Assert.Equal("So sprichst du es an: „offene Bugs in ALPHA“", s.AliasHint);
        Assert.StartsWith(ShortName.RuleText, ProjectSetup.RuleText);
    }

    [Fact]
    public async Task Wizard_blocks_invalid_short_name()
    {
        await using var m = new ManagerHarness(http: new FakeAzdo());
        var w = await AtFirstProject(m);
        w.CurrentSetup!.Alias = "Demo Project";
        Assert.Equal(ShortName.RuleText, w.CurrentSetup.AliasError);
        await w.NextCommand.ExecuteAsync();
        Assert.True(w.IsProjectStep);
        Assert.Equal("Projekt 1 von 2 · Alpha", w.StepTitle);
    }

    private static ManagerHarness WithProjects()
    {
        var m = new ManagerHarness();
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects =
            [
                new ProjectEntry { Name = "Demo Project One", Alias = "SHOP", IsDefault = true },
                new ProjectEntry { Name = "Infrastructure", Alias = "OPS" },
            ],
        }, Fake.Pat);
        return m;
    }

    [Fact]
    public async Task Rename_warns_and_keeps_permissions()
    {
        await using var m = WithProjects();
        var ops = m.H.Proj("OPS");
        m.H.Permissions.Update(s => s.ForProject(ops.PermissionKey).Groups[ToolGroup.Git] = PermissionMode.Ask);
        var vm = new ConnectionsViewModel(m.Ctx);
        var item = vm.ShortNames.Single(i => i.Original == "OPS");
        item.Value = "inf";
        Assert.Null(item.Error);
        Assert.True(vm.IsDirty);
        Assert.True(vm.Save());

        Assert.Contains("Prompts mit dem alten Kurznamen funktionieren danach nicht mehr.", m.Dialogs.Confirms.Single());
        var renamed = m.H.Proj("INF");
        Assert.Equal(ops.Project.Key, renamed.Project.Key);
        Assert.Equal(PermissionMode.Ask, m.H.Permissions.Get().Projects[renamed.PermissionKey].Groups[ToolGroup.Git]);
    }

    [Fact]
    public async Task Rename_can_be_cancelled_and_clash_is_shown()
    {
        await using var m = WithProjects();
        var vm = new ConnectionsViewModel(m.Ctx);
        var item = vm.ShortNames.Single(i => i.Original == "OPS");
        item.Value = "shop";
        Assert.Equal("Kurzname „SHOP“ wird bereits verwendet", item.Error);
        Assert.False(vm.Save());

        item.Value = "INF";
        m.Dialogs.ConfirmAnswer = false;
        Assert.False(vm.Save());
        Assert.NotNull(m.H.Store.FindProject("OPS"));
    }

    [Fact]
    public async Task Legacy_alias_shows_hint_with_suggestion()
    {
        await using var m = new ManagerHarness();
        File.WriteAllText(m.H.Dir.AppPaths.ConnectionsFile, """
            {"version":3,"connections":[{"id":"c1","name":"tfs","serverType":"Server","collectionUrl":"https://azdo.example.local/DefaultCollection",
              "projects":[{"key":"k1","name":"Demo Project One","alias":"Demo_Project_One","isDefault":true,"visible":true}]}]}
            """);
        var vm = new ConnectionsViewModel(m.Ctx);
        Assert.Equal("entspricht nicht den Kurznamen-Regeln – Vorschlag: DPO", vm.ShortNames.Single().LegacyHint);
    }
}
