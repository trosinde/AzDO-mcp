using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzdoMcp.Core.Connections;

public sealed record LegacyEntry(string Name, string BaseUrl, string Pat);

public sealed record LegacyConfig(string Path, IReadOnlyList<LegacyEntry> Entries);

public sealed record ImportResult(IReadOnlyList<Connection> Imported, IReadOnlyList<string> Messages);

/// <summary>Imports the v1 config.json (plain-text PATs) into the encrypted store.</summary>
public sealed class LegacyImporter(ConnectionStore store)
{
    /// <summary>Places where v1 configurations are usually found.</summary>
    public static IReadOnlyList<string> DefaultCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(home, ".azdo-mcp", "config.json"),
            Path.Combine(home, ".local", "share", "mcp-azure-devops", "config.json"),
        ];
    }

    /// <summary>Returns the first candidate that is a v1 configuration with at least one PAT.</summary>
    public static LegacyConfig? Detect(IEnumerable<string>? candidates = null)
    {
        foreach (var path in candidates ?? DefaultCandidates())
        {
            var config = TryRead(path);
            if (config is { Entries.Count: > 0 })
            {
                return config;
            }
        }

        return null;
    }

    public static LegacyConfig? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (root?["projects"] is not JsonObject projects)
            {
                return null;
            }

            var entries = new List<LegacyEntry>();
            foreach (var (name, value) in projects)
            {
                var baseUrl = value?["baseUrl"]?.GetValue<string>();
                var pat = value?["pat"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(pat))
                {
                    entries.Add(new LegacyEntry(name, baseUrl, pat));
                }
            }

            return new LegacyConfig(path, entries);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Imports all entries or none: one connection per collection + PAT, one project per entry.
    /// Deletes the v1 file only after everything was stored.
    /// </summary>
    public ImportResult Import(LegacyConfig config)
    {
        var usedNames = store.GetAll().Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedAliases = store.AllProjects().Select(p => p.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        var items = new List<(Connection, string)>();

        var parsed = config.Entries.Select(e => (Entry: e, Split: Split(e))).ToList();
        foreach (var group in parsed.GroupBy(x => (x.Split.Collection.ToUpperInvariant(), x.Entry.Pat)))
        {
            var first = group.First().Split;
            // dev.azure.com/<org> → "<org>", on-prem server.example.local/<coll> → "server"
            var baseName = first.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase)
                ? first.Collection.TrimEnd('/').Split('/')[^1]
                : first.Host.Split('.')[0];
            var connName = Unique(SanitizeName(baseName), usedNames);
            usedNames.Add(connName);
            var connection = new Connection
            {
                Name = connName,
                ServerType = first.ServerType,
                CollectionUrl = first.Collection,
            };

            foreach (var (entry, split) in group)
            {
                var wanted = ShortName.IsValid(entry.Name) ? ShortName.Normalize(entry.Name) : ShortName.Suggest(entry.Name);
                var alias = ShortName.IsValid(wanted) && !usedAliases.Contains(wanted) ? wanted : ShortName.Suggest(entry.Name, usedAliases);
                usedAliases.Add(alias);
                if (alias != wanted)
                {
                    messages.Add($"„{entry.Name}“ gab es schon – importiert als „{alias}“.");
                }

                connection.Projects.Add(new ProjectEntry
                {
                    Name = split.Project,
                    Alias = alias,
                    // v1 had no write switch: everything could write, keep that behaviour.
                    AllowWrite = true,
                    IsDefault = entry.Name.Equals("default", StringComparison.OrdinalIgnoreCase),
                });
            }

            items.Add((connection, group.Key.Pat));
        }

        var imported = store.SaveAll(items);
        File.Delete(config.Path);
        var projects = imported.Sum(c => c.Projects.Count);
        messages.Insert(0, $"{projects} Projekt(e) in {imported.Count} Verbindung(en) importiert, PATs verschlüsselt, {Path.GetFileName(config.Path)} gelöscht.");
        return new ImportResult(imported, messages);
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var i = 2; used.Contains(candidate); i++)
        {
            candidate = $"{name}-{i}";
        }

        return candidate;
    }

    public sealed record SplitUrl(string Collection, string Project, string Host, ServerType ServerType);

    /// <summary>Splits a v1 baseUrl (collection + project).</summary>
    public static SplitUrl Split(LegacyEntry entry)
    {
        if (!Uri.TryCreate(entry.BaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var uri))
        {
            throw new ConnectionValidationException($"baseUrl von „{entry.Name}“ ist keine gültige URL.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToList();
        var authority = uri.GetLeftPart(UriPartial.Authority);
        var isServices = uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase);

        if (segments.Count == 0)
        {
            throw new ConnectionValidationException($"baseUrl von „{entry.Name}“ enthält kein Projekt.");
        }

        var collectionSegments = segments.Take(segments.Count - 1).Select(Uri.EscapeDataString);
        var collection = $"{authority}/{string.Join('/', collectionSegments)}".TrimEnd('/');
        return new SplitUrl(collection, segments[^1], uri.Host, isServices ? ServerType.Services : ServerType.Server);
    }

    private static string SanitizeName(string name)
    {
        var chars = name.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray();
        var s = new string(chars);
        return s.Length > 36 ? s[..36] : (s.Length == 0 ? "import" : s);
    }
}
