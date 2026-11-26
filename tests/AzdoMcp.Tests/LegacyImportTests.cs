using AzdoMcp.Core.Connections;

namespace AzdoMcp.Tests;

public class LegacyImportTests
{
    private static string WriteV1(TempDir dir, int entries = 3, bool samePat = true, string collection = Fake.Collection)
    {
        var projects = string.Join(",\n", Enumerable.Range(1, entries).Select(i =>
            $$"""    "P{{i}}": { "baseUrl": "{{collection}}/Project{{i}}", "pat": "{{Fake.Pat}}{{(samePat ? "" : i.ToString())}}" }"""));
        var path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, $$"""
            {
              "projects": {
            {{projects}}
              },
              "timeout": 120000
            }
            """);
        return path;
    }

    [Fact]
    public void Detects_plain_text_pats()
    {
        using var dir = new TempDir();
        var path = WriteV1(dir);
        var cfg = LegacyImporter.Detect([Path.Combine(dir.Path, "missing.json"), path]);
        Assert.NotNull(cfg);
        Assert.Equal(3, cfg.Entries.Count);
    }

    [Fact]
    public void Same_collection_and_pat_become_one_connection()
    {
        using var h = new Harness();
        var path = WriteV1(h.Dir);
        var result = new LegacyImporter(h.Store).Import(LegacyImporter.TryRead(path)!);

        var c = Assert.Single(result.Imported);
        Assert.Equal(3, c.Projects.Count);
        Assert.Single(Directory.EnumerateFiles(h.Dir.AppPaths.SecretsDirectory));
        Assert.False(File.Exists(path));
        Assert.Equal("Project2", h.Proj("P2").Project.Name);
        Assert.Equal(Fake.Collection, h.Conn("P2").CollectionUrl);
        Assert.Equal("azdo", c.Name);
        Assert.Equal(Fake.Pat, h.Store.GetPat(c));
    }

    [Fact]
    public void Different_pats_become_separate_connections()
    {
        using var h = new Harness();
        var path = WriteV1(h.Dir, entries: 2, samePat: false);
        var result = new LegacyImporter(h.Store).Import(LegacyImporter.TryRead(path)!);
        Assert.Equal(2, result.Imported.Count);
        Assert.Equal(["azdo", "azdo-2"], result.Imported.Select(c => c.Name).Order());
    }

    [Fact]
    public void Failure_keeps_v1_file_and_imports_nothing()
    {
        using var h = new Harness();
        var path = WriteV1(h.Dir, entries: 2, samePat: false);
        h.Protector.FailOnCall = 1; // second PAT fails

        Assert.ThrowsAny<Exception>(() => new LegacyImporter(h.Store).Import(LegacyImporter.TryRead(path)!));
        Assert.True(File.Exists(path));
        Assert.Empty(h.Store.GetAll());
        Assert.Empty(Directory.EnumerateFiles(h.Dir.AppPaths.SecretsDirectory));
    }

    [Fact]
    public void Name_clash_gets_suffix()
    {
        using var h = new Harness("P1");
        var path = WriteV1(h.Dir, entries: 1);
        var result = new LegacyImporter(h.Store).Import(LegacyImporter.TryRead(path)!);
        Assert.Equal("P12", result.Imported.Single().Projects.Single().Alias);
        Assert.Contains(result.Messages, m => m.Contains("P12"));
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/My%20Project", "https://dev.azure.com/org", "My Project", ServerType.Services)]
    [InlineData("https://org.visualstudio.com/Proj", "https://org.visualstudio.com", "Proj", ServerType.Services)]
    [InlineData("https://azdo.example.local/tfs/DefaultCollection/Proj/", "https://azdo.example.local/tfs/DefaultCollection", "Proj", ServerType.Server)]
    public void Splits_base_url(string baseUrl, string collection, string project, ServerType type)
    {
        var s = LegacyImporter.Split(new LegacyEntry("x", baseUrl, "p"));
        Assert.Equal(collection, s.Collection);
        Assert.Equal(project, s.Project);
        Assert.Equal(type, s.ServerType);
    }

    [Fact]
    public void Services_connection_is_named_after_organisation()
    {
        using var h = new Harness();
        var path = WriteV1(h.Dir, entries: 1, collection: "https://dev.azure.com/contoso");
        var c = new LegacyImporter(h.Store).Import(LegacyImporter.TryRead(path)!).Imported.Single();
        Assert.Equal("contoso", c.Name);
    }
}
