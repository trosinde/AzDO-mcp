using System.Text;
using System.Text.Json.Nodes;
using AzdoMcp.Core.Connections;

namespace AzdoMcp.Tests;

public class ConnectionStoreTests
{
    private static Connection Conn(string name, params string[] aliases) => new()
    {
        Name = name,
        CollectionUrl = Fake.Collection,
        Projects = aliases.Select(a => new ProjectEntry { Name = a, Alias = a }).ToList(),
    };

    [Fact]
    public void Aliases_are_unique_across_connections()
    {
        using var h = new Harness("SHOP");
        var ex = Assert.Throws<ConnectionValidationException>(() => h.Store.Save(Conn("other", "shop"), Fake.Pat));
        Assert.Contains("bereits verwendet", ex.Message);
        Assert.Single(h.Store.GetAll());
    }

    [Fact]
    public void Connection_names_are_unique()
    {
        using var h = new Harness("SHOP");
        Assert.Throws<ConnectionValidationException>(() => h.Store.Save(Conn("shop", "X"), Fake.Pat));
    }

    [Fact]
    public void First_project_becomes_default()
    {
        using var h = new Harness("AA");
        Assert.True(h.Proj("AA").Project.IsDefault);
    }

    [Fact]
    public void Exactly_one_default_project()
    {
        using var h = new Harness("AA");
        h.Add("BB", isDefault: true);
        var all = h.Store.AllProjects();
        Assert.Single(all, p => p.Project.IsDefault);
        Assert.True(h.Proj("BB").Project.IsDefault);
    }

    [Fact]
    public void One_pat_for_all_projects_of_a_connection()
    {
        using var h = new Harness();
        var c = h.Store.Save(Conn("tfs", "SHOP", "OPS", "Musterlab"), Fake.Pat);
        Assert.Single(Directory.EnumerateFiles(h.Dir.AppPaths.SecretsDirectory));
        Assert.Equal(Fake.Pat, h.Store.GetPat(h.Proj("OPS").Connection));
        Assert.Equal(c.Id, h.Proj("Musterlab").Connection.Id);
    }

    [Fact]
    public void Removing_default_moves_default_to_remaining()
    {
        using var h = new Harness("AA", "BB");
        h.Store.Remove(h.Conn("AA").Id);
        Assert.True(h.Store.AllProjects().Single().Project.IsDefault);
    }

    [Fact]
    public void Find_project_by_alias_case_insensitive_and_default()
    {
        using var h = new Harness();
        h.Store.Save(Conn("tfs", "SHOP", "OPS"), Fake.Pat);
        Assert.Equal("OPS", h.Store.FindProject("ops")!.Alias);
        Assert.Equal("SHOP", h.Store.FindProject(null)!.Alias);
        Assert.Null(h.Store.FindProject("Musterlab"));
    }

    [Fact]
    public void Pat_is_never_stored_in_plain_text()
    {
        using var h = new Harness("SHOP");
        foreach (var file in Directory.EnumerateFiles(h.Dir.Path, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.DoesNotContain(Fake.Pat, Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain(Fake.Pat, Encoding.Unicode.GetString(bytes));
        }
    }

    [Fact]
    public void Pat_round_trips_and_can_be_replaced()
    {
        using var h = new Harness("SHOP");
        var c = h.Conn("SHOP");
        Assert.Equal(Fake.Pat, h.Store.GetPat(c));

        c.PatExpires = new DateOnly(2030, 1, 1);
        h.Store.Save(c, "replaced-token-value-0000000000000000000000");
        Assert.Equal("replaced-token-value-0000000000000000000000", h.Store.GetPat(h.Conn("SHOP")));
        Assert.Equal(new DateOnly(2030, 1, 1), h.Conn("SHOP").PatExpires);
    }

    [Fact]
    public void Undecryptable_pat_reports_replace_hint()
    {
        using var h = new Harness("SHOP");
        var c = h.Conn("SHOP");
        File.WriteAllBytes(h.Dir.AppPaths.SecretFile(c.Id), [1, 2, 3, 4, 5, 6]);
        var ex = Assert.Throws<SecretUnreadableException>(() => h.Store.GetPat(c));
        Assert.Contains("PAT für SHOP nicht lesbar – bitte ersetzen", ex.Message);
        Assert.Equal(StatusLevel.Error, ExpiryClassifier.Classify(c, DateOnly.FromDateTime(DateTime.Today), h.Store.CanReadPat(c)).Level);
    }

    [Fact]
    public void New_connection_without_pat_is_rejected()
    {
        using var h = new Harness();
        Assert.Throws<ConnectionValidationException>(() => h.Store.Save(Conn("X", "X"), null));
        Assert.Empty(h.Store.GetAll());
    }

    [Theory]
    [InlineData("", Fake.Collection, "P")]
    [InlineData("has space", Fake.Collection, "P")]
    [InlineData("X", "not a url", "P")]
    [InlineData("X", "ftp://azdo.example.local/c", "P")]
    [InlineData("X", Fake.Collection, "bad alias")]
    public void Invalid_connections_are_rejected(string name, string url, string alias)
    {
        using var h = new Harness();
        Assert.Throws<ConnectionValidationException>(() =>
            h.Store.Save(new Connection { Name = name, CollectionUrl = url, Projects = [new ProjectEntry { Name = "P", Alias = alias }] }, Fake.Pat));
    }

    [Fact]
    public void Connection_without_projects_is_allowed()
    {
        using var h = new Harness();
        h.Store.Save(Conn("tfs"), Fake.Pat);
        Assert.Empty(h.Store.AllProjects());
        Assert.Single(h.Store.GetAll());
    }

    [Fact]
    public void Folder_is_created_on_first_save()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "fresh", "AzdoMcpData");
        var store = new ConnectionStore(new AzdoMcp.Core.AppPaths(root), new FakeProtector());
        store.Save(Conn("AA", "AA"), Fake.Pat);
        Assert.True(Directory.Exists(Path.Combine(root, "secrets")));
        Assert.True(Directory.Exists(Path.Combine(root, "logs")));
    }

    [Fact]
    public void Second_instance_sees_changes_without_restart()
    {
        using var h = new Harness("AA");
        var server = new ConnectionStore(h.Dir.AppPaths, h.Protector);
        Assert.Single(server.AllProjects());

        h.Add("BB");
        Assert.NotNull(server.FindProject("bb"));
    }

    [Fact]
    public void Corrupt_file_raises_configuration_error()
    {
        using var h = new Harness();
        File.WriteAllText(h.Dir.AppPaths.ConnectionsFile, "{ not json");
        Assert.Throws<ConfigurationException>(() => h.Store.GetAll());
    }

    [Fact]
    public void Suggest_alias_avoids_clashes()
    {
        using var h = new Harness("SHOP");
        Assert.Equal("SHOP2", h.Store.SuggestAlias("SHOP"));
        Assert.Equal("MP", h.Store.SuggestAlias("My Project"));
        Assert.Equal("XP", h.Store.SuggestAlias("X", ["X"]));
    }

    [Fact]
    public void Dpapi_round_trip_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI nur unter Windows");
        var p = SecretProtectors.CreateDefault();
        var blob = p.Protect("geheim"u8.ToArray());
        Assert.DoesNotContain("geheim", Encoding.UTF8.GetString(blob));
        Assert.Equal("geheim", Encoding.UTF8.GetString(p.Unprotect(blob)));
        blob[^1] ^= 0xFF;
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => p.Unprotect(blob));
    }
}

public class SchemaMigrationTests
{
    private static string V1(params (string Id, string Short, string Project, bool Default, bool Write, string[] Missing)[] entries) =>
        new JsonObject
        {
            ["version"] = 1,
            ["connections"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject
            {
                ["id"] = e.Id,
                ["shortName"] = e.Short,
                ["serverType"] = "Server",
                ["collectionUrl"] = Fake.Collection,
                ["project"] = e.Project,
                ["patExpires"] = "2030-01-31",
                ["reminderDays"] = 14,
                ["isDefault"] = e.Default,
                ["allowWrite"] = e.Write,
                ["missingScopes"] = new JsonArray(e.Missing.Select(m => (JsonNode)m).ToArray()),
            }).ToArray()),
        }.ToJsonString();

    [Fact]
    public void Migrates_each_entry_to_connection_with_one_project()
    {
        using var dir = new TempDir();
        var protector = new FakeProtector();
        Directory.CreateDirectory(dir.AppPaths.SecretsDirectory);
        File.WriteAllBytes(dir.AppPaths.SecretFile("c1"), protector.Protect(Encoding.UTF8.GetBytes(Fake.Pat)));
        File.WriteAllText(dir.AppPaths.ConnectionsFile, V1(("c1", "SHOP", "Demo Project One", true, true, ["Test"]), ("c2", "OPS", "OPS", false, false, [])));

        var store = new ConnectionStore(dir.AppPaths, protector);
        var all = store.GetAll();

        Assert.Equal(2, all.Count);
        var shop = store.FindProject("SHOP")!;
        Assert.Equal("c1", shop.Connection.Id);
        Assert.Equal("Demo Project One", shop.Project.Name);
        Assert.True(shop.Project.IsDefault);
        Assert.True(shop.Project.AllowWrite);
        Assert.Equal(new DateOnly(2030, 1, 31), shop.Connection.PatExpires);
        Assert.Equal(14, shop.Connection.ReminderDays);
        Assert.Equal(Check.No, shop.Effective[AccessArea.Test].Read);
        Assert.Equal(AreaAccess.Unknown, shop.Effective[AccessArea.WorkItems]); // "?" until first discovery: no cap
        Assert.Equal(Fake.Pat, store.GetPat(shop.Connection));
        Assert.False(store.FindProject("OPS")!.Project.AllowWrite);
    }

    [Fact]
    public void Writes_schema_3_once_and_keeps_backup()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.AppPaths.ConnectionsFile, V1(("c1", "AA", "AA", true, true, [])));
        var store = new ConnectionStore(dir.AppPaths, new FakeProtector());
        store.GetAll();

        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(dir.AppPaths.ConnectionsFile))!["version"]!.GetValue<int>());
        Assert.True(File.Exists(store.BackupV1File));
        var stamp = File.GetLastWriteTimeUtc(dir.AppPaths.ConnectionsFile);

        new ConnectionStore(dir.AppPaths, new FakeProtector()).GetAll();
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(dir.AppPaths.ConnectionsFile));
    }

    private static string V2() => """
        {
          "version": 2,
          "connections": [
            {
              "id": "c1",
              "name": "tfs",
              "serverType": "Server",
              "collectionUrl": "https://azdo.example.local/DefaultCollection",
              "patScope": { "levels": { "WorkItems": "Read", "Code": "Read", "Test": "None", "Iterations": "Write" } },
              "projects": [
                { "key": "k1", "azdoId": "p1", "name": "Demo Project One", "alias": "SHOP", "isDefault": true, "allowWrite": true,
                  "projectRights": { "levels": { "WorkItems": "Write", "Code": "Write" } }, "visible": true }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Migrates_schema_2_rights_to_lesen_schreiben()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.AppPaths.ConnectionsFile, V2());
        var store = new ConnectionStore(dir.AppPaths, new FakeProtector());
        var c = store.GetAll().Single();

        Assert.Equal(Acc.Ro, c.PatScope![AccessArea.WorkItems]); // safe side: no write before the next discovery
        Assert.Equal(Acc.Rn, c.PatScope[AccessArea.Code]);
        Assert.Equal(Acc.N, c.PatScope[AccessArea.Test]);
        Assert.Equal(Acc.W, c.PatScope[AccessArea.Iterations]);
        var p = c.Projects.Single();
        Assert.Equal(Acc.W, p.ProjectRights![AccessArea.WorkItems]);
        Assert.Equal(AreaAccess.Unknown, p.ProjectRights[AccessArea.Test]);
        Assert.Equal("SHOP", p.Alias);
        Assert.True(p.AllowWrite);
        Assert.True(File.Exists(store.BackupV2File));
        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(dir.AppPaths.ConnectionsFile))!["version"]!.GetValue<int>());
    }

    [Fact]
    public void Schema_2_migration_runs_once()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.AppPaths.ConnectionsFile, V2());
        new ConnectionStore(dir.AppPaths, new FakeProtector()).GetAll();
        var backup = File.ReadAllText(Path.Combine(dir.AppPaths.Root, "connections.v2.bak.json"));
        var stamp = File.GetLastWriteTimeUtc(dir.AppPaths.ConnectionsFile);

        new ConnectionStore(dir.AppPaths, new FakeProtector()).GetAll();
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(dir.AppPaths.ConnectionsFile));
        Assert.Contains("\"levels\"", backup);
    }
}
