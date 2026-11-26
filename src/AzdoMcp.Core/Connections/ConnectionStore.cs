using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AzdoMcp.Core.Infrastructure;

namespace AzdoMcp.Core.Connections;

public sealed class ConnectionValidationException(string message) : Exception(message);

public sealed class ConfigurationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads and writes connections.json (schema 2: connection = collection + PAT with projects)
/// and the DPAPI-protected PAT files. Safe for several processes at once.
/// </summary>
public sealed partial class ConnectionStore
{
    public const int SchemaVersion = 3;

    private readonly AppPaths _paths;
    private readonly ISecretProtector _protector;
    private readonly object _gate = new();
    private List<Connection> _cache = [];
    private (DateTime Time, long Length)? _cacheStamp;

    public ConnectionStore(AppPaths paths, ISecretProtector protector)
    {
        _paths = paths;
        _protector = protector;
    }

    public AppPaths Paths => _paths;

    private string MutexName => $@"Local\AzdoMcp-config-{_paths.RootHash}";

    public string BackupV1File => Path.Combine(_paths.Root, "connections.v1.bak.json");

    public string BackupV2File => Path.Combine(_paths.Root, "connections.v2.bak.json");

    /// <summary>All connections; re-reads when the file changed on disk. Migrates schema 1 once.</summary>
    public IReadOnlyList<Connection> GetAll()
    {
        lock (_gate)
        {
            var stamp = CurrentStamp();
            if (stamp != _cacheStamp)
            {
                _cache = ReadOrMigrate();
                _cacheStamp = CurrentStamp();
            }

            return _cache.Select(c => c.Clone()).ToList();
        }
    }

    public Connection? Get(string id) => GetAll().FirstOrDefault(c => c.Id == id);

    public Connection? FindConnection(string name) =>
        GetAll().FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ProjectRef> AllProjects() =>
        GetAll().SelectMany(c => c.Projects.Select(p => new ProjectRef(c, p))).ToList();

    /// <summary>By alias (case-insensitive); null or empty selects the default project.</summary>
    public ProjectRef? FindProject(string? alias)
    {
        var all = AllProjects();
        if (string.IsNullOrWhiteSpace(alias))
        {
            return all.FirstOrDefault(p => p.Project.IsDefault) ?? (all.Count == 1 ? all[0] : null);
        }

        return all.FirstOrDefault(p => string.Equals(p.Alias, alias.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or replaces a connection with its projects. <paramref name="newPat"/> null keeps the stored PAT.</summary>
    public Connection Save(Connection connection, string? newPat)
    {
        return Mutate(list =>
        {
            var saved = Upsert(list, connection);
            if (newPat is not null)
            {
                WriteSecret(saved.Id, newPat);
                saved.LastAuthFailure = null;
            }
            else if (!HasPat(saved.Id))
            {
                throw new ConnectionValidationException("Personal Access Token fehlt.");
            }

            return saved;
        });
    }

    /// <summary>Saves several new connections with their PATs all-or-nothing (v1 import).</summary>
    public IReadOnlyList<Connection> SaveAll(IReadOnlyList<(Connection Connection, string Pat)> items)
    {
        return Mutate(list =>
        {
            var written = new List<string>();
            try
            {
                var result = new List<Connection>();
                foreach (var (connection, pat) in items)
                {
                    var saved = Upsert(list, connection);
                    WriteSecret(saved.Id, pat);
                    written.Add(saved.Id);
                    result.Add(saved);
                }

                return result;
            }
            catch
            {
                foreach (var id in written)
                {
                    TryDelete(_paths.SecretFile(id));
                }

                throw;
            }
        });
    }

    public void Remove(string id)
    {
        Mutate(list =>
        {
            var existing = list.FirstOrDefault(c => c.Id == id);
            if (existing is not null)
            {
                list.Remove(existing);
                TryDelete(_paths.SecretFile(id));
            }

            return 0;
        });
    }

    /// <summary>Changes one connection in place (status fields, projects) with full validation.</summary>
    public void Update(string id, Action<Connection> change)
    {
        Mutate(list =>
        {
            var existing = list.FirstOrDefault(c => c.Id == id);
            if (existing is null)
            {
                return 0;
            }

            var copy = existing.Clone();
            change(copy);
            Upsert(list, copy);
            return 0;
        });
    }

    /// <summary>Makes exactly this project the default (by permission key).</summary>
    public void SetDefaultProject(string connectionId, string projectKey) => Mutate(list =>
    {
        foreach (var p in list.SelectMany(c => c.Projects))
        {
            p.IsDefault = false;
        }

        var target = list.FirstOrDefault(c => c.Id == connectionId)?.Projects.FirstOrDefault(p => p.Key == projectKey)
                     ?? throw new ConnectionValidationException("Projekt nicht gefunden.");
        target.IsDefault = true;
        return 0;
    });

    public bool HasPat(string connectionId) => File.Exists(_paths.SecretFile(connectionId));

    /// <exception cref="SecretUnreadableException">No PAT or it cannot be decrypted by this user.</exception>
    public string GetPat(Connection connection)
    {
        var file = _paths.SecretFile(connection.Id);
        if (!File.Exists(file))
        {
            throw new SecretUnreadableException(connection.Name);
        }

        try
        {
            return Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(file)));
        }
        catch (CryptographicException ex)
        {
            throw new SecretUnreadableException(connection.Name, ex);
        }
    }

    public bool CanReadPat(Connection connection)
    {
        try
        {
            GetPat(connection);
            return true;
        }
        catch (SecretUnreadableException)
        {
            return false;
        }
    }

    /// <summary>Proposes a Kurzname that meets the rules and is not used yet ("DPO", "OPS2", …).</summary>
    public string SuggestAlias(string projectName, IEnumerable<string>? reserved = null) =>
        ShortName.Suggest(projectName, AllProjects().Select(p => p.Alias).Concat(reserved ?? []));

    public static string SanitizeAlias(string name)
    {
        var s = new string(name.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray()).Trim('-');
        if (s.Length == 0)
        {
            s = "projekt";
        }

        return s.Length > 36 ? s[..36] : s;
    }

    private T Mutate<T>(Func<List<Connection>, T> change)
    {
        return FileUtil.WithMutex(MutexName, () =>
        {
            lock (_gate)
            {
                var list = ReadOrMigrateLocked();
                var result = change(list);
                Normalize(list);
                Write(list);
                _cache = list;
                _cacheStamp = CurrentStamp();
                return result;
            }
        });
    }

    private static Connection Upsert(List<Connection> list, Connection connection)
    {
        var copy = connection.Clone();
        copy.Name = copy.Name.Trim();
        copy.CollectionUrl = copy.CollectionUrl.Trim().TrimEnd('/');
        foreach (var p in copy.Projects)
        {
            p.Alias = p.Alias.Trim();
            p.Name = p.Name.Trim();
        }

        Validate(copy);

        // Strict Kurzname rules for new or changed names; unchanged legacy aliases stay valid.
        var stored = list.FirstOrDefault(c => c.Id == copy.Id)?.Projects.ToDictionary(p => p.Key) ?? [];
        foreach (var p in copy.Projects)
        {
            var unchanged = stored.TryGetValue(p.Key, out var before) && string.Equals(before.Alias, p.Alias, StringComparison.Ordinal);
            if (unchanged)
            {
                continue;
            }

            if (ShortName.Validate(p.Alias) is { } error)
            {
                throw new ConnectionValidationException(error);
            }

            p.Alias = ShortName.Normalize(p.Alias);
        }

        if (list.Any(c => c.Id != copy.Id && string.Equals(c.Name, copy.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConnectionValidationException($"Der Name „{copy.Name}“ wird bereits verwendet.");
        }

        var otherAliases = list.Where(c => c.Id != copy.Id).SelectMany(c => c.Projects).Select(p => p.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in copy.Projects)
        {
            if (otherAliases.Contains(p.Alias) || !seen.Add(p.Alias))
            {
                throw new ConnectionValidationException($"Kurzname „{p.Alias.ToUpperInvariant()}“ wird bereits verwendet");
            }
        }

        if (copy.Projects.Any(p => p.IsDefault))
        {
            var keep = copy.Projects.First(p => p.IsDefault);
            foreach (var p in list.Where(c => c.Id != copy.Id).SelectMany(c => c.Projects).Concat(copy.Projects))
            {
                p.IsDefault = ReferenceEquals(p, keep);
            }
        }

        var index = list.FindIndex(c => c.Id == copy.Id);
        if (index >= 0)
        {
            list[index] = copy;
        }
        else
        {
            list.Add(copy);
        }

        return copy;
    }

    public static void Validate(Connection c)
    {
        if (string.IsNullOrWhiteSpace(c.Name))
        {
            throw new ConnectionValidationException("Name der Verbindung fehlt.");
        }

        if (!NamePattern().IsMatch(c.Name.Trim()))
        {
            throw new ConnectionValidationException("Der Name darf nur Buchstaben, Ziffern, „-“, „_“ und „.“ enthalten (max. 40 Zeichen).");
        }

        if (!Uri.TryCreate(c.CollectionUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ConnectionValidationException("Collection-URL muss mit https:// beginnen.");
        }

        if (c.ReminderDays is < 0 or > 365)
        {
            throw new ConnectionValidationException("Erinnerung muss zwischen 0 und 365 Tagen liegen.");
        }

        foreach (var p in c.Projects)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                throw new ConnectionValidationException("Projektname fehlt.");
            }

            if (!NamePattern().IsMatch(p.Alias.Trim()))
            {
                throw new ConnectionValidationException(ShortName.RuleText);
            }
        }
    }

    /// <summary>Exactly one default project when any project exists.</summary>
    private static void Normalize(List<Connection> list)
    {
        var all = list.SelectMany(c => c.Projects).ToList();
        var defaults = all.Where(p => p.IsDefault).ToList();
        if (all.Count > 0 && defaults.Count == 0)
        {
            all[0].IsDefault = true;
        }

        foreach (var extra in defaults.Skip(1))
        {
            extra.IsDefault = false;
        }
    }

    private void WriteSecret(string id, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new ConnectionValidationException("Personal Access Token fehlt.");
        }

        Directory.CreateDirectory(_paths.SecretsDirectory);
        var blob = _protector.Protect(Encoding.UTF8.GetBytes(pat.Trim()));
        FileUtil.WriteAllBytesAtomic(_paths.SecretFile(id), blob);
    }

    private (DateTime, long)? CurrentStamp()
    {
        var fi = new FileInfo(_paths.ConnectionsFile);
        return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : null;
    }

    private List<Connection> ReadOrMigrate()
    {
        var (version, list) = ReadFile();
        if (version >= SchemaVersion)
        {
            return list;
        }

        // Older schema on disk: migrate under the cross-process lock (another process may do it first).
        return FileUtil.WithMutex(MutexName, ReadOrMigrateLocked);
    }

    private List<Connection> ReadOrMigrateLocked()
    {
        var (version, list) = ReadFile();
        if (version >= SchemaVersion || !File.Exists(_paths.ConnectionsFile))
        {
            return list;
        }

        var text = File.ReadAllText(_paths.ConnectionsFile);
        List<Connection> migrated;
        if (version == 2)
        {
            migrated = JsonSerializer.Deserialize<ConnectionsDocument>(SchemaMigration.FromV2(text), Json.File)?.Connections ?? [];
            File.Copy(_paths.ConnectionsFile, BackupV2File, overwrite: true);
        }
        else
        {
            migrated = SchemaMigration.FromV1(text);
            File.Copy(_paths.ConnectionsFile, BackupV1File, overwrite: true);
        }

        Normalize(migrated);
        Write(migrated);
        return migrated;
    }

    private (int Version, List<Connection> List) ReadFile()
    {
        if (!File.Exists(_paths.ConnectionsFile))
        {
            return (SchemaVersion, []);
        }

        try
        {
            var text = File.ReadAllText(_paths.ConnectionsFile);
            var version = JsonNode.Parse(text)?["version"]?.GetValue<int>() ?? 1;
            if (version < SchemaVersion)
            {
                return (version, []);
            }

            var doc = JsonSerializer.Deserialize<ConnectionsDocument>(text, Json.File);
            return (version, doc?.Connections ?? []);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new ConfigurationException($"connections.json ist ungültig: {ex.Message}", ex);
        }
    }

    private void Write(List<Connection> list)
    {
        _paths.EnsureCreated();
        var json = JsonSerializer.Serialize(new ConnectionsDocument { Version = SchemaVersion, Connections = list }, Json.File);
        FileUtil.WriteAllTextAtomic(_paths.ConnectionsFile, json);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,40}$")]
    private static partial Regex NamePattern();

    private sealed class ConnectionsDocument
    {
        public int Version { get; set; } = SchemaVersion;

        public List<Connection> Connections { get; set; } = [];
    }
}

/// <summary>Schema 1 (one project per connection) and schema 2 (one level per area) → schema 3.</summary>
public static class SchemaMigration
{
    /// <summary>
    /// Converts schema 2 rights ("levels": Write/Read/None) to Lesen/Schreiben. "Read" becomes ja/n/a,
    /// except Work Items, which becomes ja/nein so no write is possible before the next discovery.
    /// </summary>
    public static string FromV2(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new ConfigurationException("connections.json ist leer.");
        foreach (var conn in root["connections"]?.AsArray() ?? [])
        {
            if (conn is not JsonObject c)
            {
                continue;
            }

            Convert(c, "patScope");
            foreach (var proj in c["projects"]?.AsArray() ?? [])
            {
                if (proj is JsonObject p)
                {
                    Convert(p, "projectRights");
                }
            }
        }

        root["version"] = ConnectionStore.SchemaVersion;
        return root.ToJsonString();

        static void Convert(JsonObject owner, string property)
        {
            if (owner[property] is not JsonObject rights)
            {
                return;
            }

            var areas = new JsonObject();
            foreach (var (area, level) in rights["levels"]?.AsObject() ?? [])
            {
                var (read, write) = level?.GetValue<string>() switch
                {
                    "Write" => ("Yes", "Yes"),
                    "Read" => ("Yes", area == nameof(AccessArea.WorkItems) ? "No" : "NotTestable"),
                    _ => ("No", "No"),
                };
                areas[area] = new JsonObject { ["read"] = read, ["write"] = write };
            }

            owner[property] = new JsonObject { ["areas"] = areas };
        }
    }

    public static List<Connection> FromV1(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new ConfigurationException("connections.json ist leer.");
        var result = new List<Connection>();
        foreach (var old in root["connections"]?.AsArray() ?? [])
        {
            if (old is null)
            {
                continue;
            }

            string? S(string name) => old[name]?.GetValue<string>();
            var shortName = S("shortName") ?? "verbindung";
            var missing = (old["missingScopes"]?.AsArray() ?? []).Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet();

            AreaRights? scope = null;
            if (missing.Count > 0)
            {
                scope = new AreaRights();
                var denied = new AreaAccess(Check.No, Check.No);
                if (missing.Contains("WorkItemsRead"))
                {
                    scope[AccessArea.WorkItems] = denied;
                    scope[AccessArea.Iterations] = denied;
                }
                else if (missing.Contains("WorkItemsWrite"))
                {
                    scope[AccessArea.WorkItems] = new AreaAccess(Check.Yes, Check.No);
                }

                if (missing.Contains("Git"))
                {
                    scope[AccessArea.Code] = denied;
                }

                if (missing.Contains("Test"))
                {
                    scope[AccessArea.Test] = denied;
                }

                if (missing.Contains("Planning"))
                {
                    scope[AccessArea.Iterations] = denied;
                }
            }

            result.Add(new Connection
            {
                // Same ID keeps secrets\<id>.bin valid.
                Id = S("id") ?? Guid.NewGuid().ToString("N"),
                Name = shortName,
                ServerType = Enum.TryParse<ServerType>(S("serverType"), out var st) ? st : ServerType.Server,
                CollectionUrl = S("collectionUrl") ?? "",
                PatExpires = DateOnly.TryParse(S("patExpires"), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
                ReminderDays = old["reminderDays"]?.GetValue<int>() ?? 7,
                LastTested = DateTimeOffset.TryParse(S("lastTested"), out var lt) ? lt : null,
                LastAuthFailure = DateTimeOffset.TryParse(S("lastAuthFailure"), out var la) ? la : null,
                PatScope = scope,
                Projects =
                [
                    new ProjectEntry
                    {
                        Name = S("project") ?? shortName,
                        Alias = shortName,
                        IsDefault = old["isDefault"]?.GetValue<bool>() ?? false,
                        AllowWrite = old["allowWrite"]?.GetValue<bool>() ?? true,
                    },
                ],
            });
        }

        return result;
    }
}
