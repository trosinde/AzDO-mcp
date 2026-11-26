using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;

namespace AzdoMcp.Core.Discovery;

public sealed record DiscoveredProject(string AzdoId, string Name, AreaRights ProjectRights, AreaRights Effective);

public sealed record DiscoveryResult(
    DateTimeOffset At,
    string? UserId,
    string? UserName,
    AreaRights PatScope,
    IReadOnlyList<DiscoveredProject> Projects,
    bool UsedSecurityApi,
    IReadOnlyList<string> Notes);

/// <summary>
/// Finds every project the PAT can see and the effective right per area
/// (= lower of PAT scope and the user's project rights). Sends no write request except
/// the non-persisting validateOnly probe for the Work Items write scope.
/// </summary>
public sealed class ProjectDiscovery(AzdoClientFactory factory, TimeProvider? time = null)
{
    public const int PageSize = 100;
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<DiscoveryResult> DiscoverAsync(Connection connection, string pat, CancellationToken ct = default)
    {
        var client = factory.Create(connection, pat, timeout: StepTimeout);
        var notes = new List<string>();

        // 1. Login
        var data = await client.GetAsync("_apis/connectionData?connectOptions=none&lastChangeId=-1", ct).ConfigureAwait(false);
        var userId = data?["authenticatedUser"]?["id"]?.GetValue<string>();
        var userName = data?["authenticatedUser"]?["providerDisplayName"]?.GetValue<string>();

        // 2. Projects (paged)
        var projects = await ListProjectsAsync(client, ct).ConfigureAwait(false);

        // 3. Rights per project (Security API or read probes)
        var reader = new ProjectRightsReader(client);
        var rights = await reader.ReadAsync(projects, ct).ConfigureAwait(false);
        if (!reader.UsedSecurityApi)
        {
            notes.Add("Security-API nicht verfügbar – Projektrechte per Lese-Test ermittelt (Schreiben: n/a).");
        }

        // 4. PAT scope (collection-wide), probed on the project with the best rights so that a missing
        //    project right is not mistaken for a PAT limit
        var probe = projects
            .OrderByDescending(p => Rank(rights[p.Id][AccessArea.WorkItems]))
            .FirstOrDefault();
        var scope = probe is null
            ? new AreaRights()
            : await PatScopeProbe.ProbeAsync(client.ForProject(probe.Name), ct).ConfigureAwait(false);

        // 5. Effective = PAT scope combined with project right, per column
        var result = projects
            .Select(p => new DiscoveredProject(p.Id, p.Name, rights[p.Id], AreaRights.Combine(scope, rights[p.Id])))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DiscoveryResult(_time.GetUtcNow(), userId, userName, scope, result, reader.UsedSecurityApi, notes);
    }

    internal sealed record ProjectInfo(string Id, string Name);

    private static int Rank(AreaAccess wi) => (wi.Write == Check.Yes ? 2 : 0) + (wi.Read == Check.Yes ? 1 : 0) - (wi.Read == Check.No ? 4 : 0);

    internal static async Task<List<ProjectInfo>> ListProjectsAsync(AzdoClient client, CancellationToken ct)
    {
        var list = new List<ProjectInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? token = null;
        for (var page = 0; page < 1000; page++)
        {
            var url = $"_apis/projects?$top={PageSize}&api-version={AzdoClient.ApiVersion}" +
                      (token is null ? "" : "&continuationToken=" + Uri.EscapeDataString(token));
            var (body, next) = await client.GetPageAsync(url, ct).ConfigureAwait(false);
            foreach (var p in body?["value"]?.AsArray() ?? [])
            {
                var id = p?["id"]?.GetValue<string>();
                var name = p?["name"]?.GetValue<string>();
                if (id is not null && name is not null && seen.Add(id))
                {
                    list.Add(new ProjectInfo(id, name));
                }
            }

            if (next is null || next == token)
            {
                break;
            }

            token = next;
        }

        return list;
    }
}

/// <summary>PAT scope per area by read probes; Work Items write only via validateOnly (stores nothing).</summary>
public static class PatScopeProbe
{
    public static async Task<AreaRights> ProbeAsync(AzdoClient projectClient, CancellationToken ct)
    {
        var p = projectClient.ProjectUrl;
        var scope = new AreaRights();

        var wiRead = await Probe(() => projectClient.PostJsonAsync($"{p}/_apis/wit/wiql?$top=1&api-version={AzdoClient.ApiVersion}",
            new { query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project" }, ct)).ConfigureAwait(false);
        Check wiWrite;
        if (wiRead == ProbeResult.Ok)
        {
            var probe = await Probe(() => projectClient.JsonPatchAsync(HttpMethod.Post,
                $"{p}/_apis/wit/workitems/$Task?validateOnly=true&api-version={AzdoClient.ApiVersion}",
                new JsonArray(new JsonObject { ["op"] = "add", ["path"] = "/fields/System.Title", ["value"] = "Berechtigungsprüfung" }), ct)).ConfigureAwait(false);
            // A missing "Task" type (400/404) says nothing about the scope; only 401/403 deny writing.
            wiWrite = probe == ProbeResult.Denied ? Check.No : Check.Yes;
        }
        else
        {
            // Without reading work items there is no writing them either; otherwise untestable.
            wiWrite = wiRead == ProbeResult.Denied ? Check.No : Check.NotTestable;
        }

        scope[AccessArea.WorkItems] = new AreaAccess(ToCheck(wiRead), wiWrite);
        scope[AccessArea.Code] = new AreaAccess(ToCheck(await Probe(() => projectClient.GetAsync($"{p}/_apis/git/repositories?api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false)), Check.NotTestable);
        scope[AccessArea.Pipelines] = new AreaAccess(ToCheck(await Probe(() => projectClient.GetAsync($"{p}/_apis/build/builds?$top=1&api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false)), Check.NotTestable);
        scope[AccessArea.Test] = new AreaAccess(ToCheck(await Probe(() => projectClient.GetAsync($"{p}/_apis/test/runs?$top=1&api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false)), Check.NotTestable);
        scope[AccessArea.Iterations] = new AreaAccess(ToCheck(await Probe(() => projectClient.GetAsync($"{p}/_apis/work/teamsettings/iterations?api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false)), Check.NotTestable);
        return scope;
    }

    internal enum ProbeResult
    {
        Ok,
        Denied,
        Other,
    }

    /// <summary>Ok → ja, 401/403 → nein, any other failure → n/a.</summary>
    internal static Check ToCheck(ProbeResult r) => r switch
    {
        ProbeResult.Ok => Check.Yes,
        ProbeResult.Denied => Check.No,
        _ => Check.NotTestable,
    };

    internal static async Task<ProbeResult> Probe(Func<Task<JsonNode?>> call)
    {
        try
        {
            await call().ConfigureAwait(false);
            return ProbeResult.Ok;
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.Forbidden or AzdoErrorKind.Unauthorized)
        {
            return ProbeResult.Denied;
        }
        catch (AzdoApiException)
        {
            return ProbeResult.Other;
        }
    }
}

/// <summary>
/// The signed-in user's rights per project via the Security API, with bits resolved by action
/// name. Falls back to read probes per project when the Security API is not usable.
/// </summary>
public sealed class ProjectRightsReader(AzdoClient client)
{
    private const int TokensPerCall = 50;

    public bool UsedSecurityApi { get; private set; }

    internal async Task<Dictionary<string, AreaRights>> ReadAsync(IReadOnlyList<ProjectDiscovery.ProjectInfo> projects, CancellationToken ct)
    {
        if (projects.Count == 0)
        {
            return [];
        }

        try
        {
            var viaApi = await ReadViaSecurityApiAsync(projects, ct).ConfigureAwait(false);
            if (viaApi is not null)
            {
                UsedSecurityApi = true;
                return viaApi;
            }
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.Forbidden or AzdoErrorKind.Unauthorized or AzdoErrorKind.NotAvailable or AzdoErrorKind.NotFound or AzdoErrorKind.BadRequest)
        {
        }

        var result = new Dictionary<string, AreaRights>();
        foreach (var p in projects)
        {
            result[p.Id] = await ProbeProjectAsync(client.ForProject(p.Name), ct).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Dictionary<string, AreaRights>?> ReadViaSecurityApiAsync(IReadOnlyList<ProjectDiscovery.ProjectInfo> projects, CancellationToken ct)
    {
        var namespaces = await client.GetAsync($"_apis/securitynamespaces?api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
        var css = Namespace(namespaces, "CSS");
        var git = Namespace(namespaces, "Git Repositories");
        var project = Namespace(namespaces, "Project");
        var build = Namespace(namespaces, "Build");
        if (css is null || git is null)
        {
            return null;
        }

        // Area root node of each project for the CSS token.
        var areaTokens = new Dictionary<string, string>();
        foreach (var p in projects)
        {
            try
            {
                var node = await client.GetAsync($"{client.CollectionUrl}/{Uri.EscapeDataString(p.Name)}/_apis/wit/classificationnodes/areas?api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
                if (node?["identifier"]?.GetValue<string>() is { } id)
                {
                    areaTokens[p.Id] = "vstfs:///Classification/Node/" + id;
                }
            }
            catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.Forbidden or AzdoErrorKind.NotFound or AzdoErrorKind.NotAvailable)
            {
                // No work item access in that project.
            }
        }

        var wiRead = await CheckAsync(css, "WORK_ITEM_READ", areaTokens, ct).ConfigureAwait(false);
        var wiWrite = await CheckAsync(css, "WORK_ITEM_WRITE", areaTokens, ct).ConfigureAwait(false);
        var gitTokens = projects.ToDictionary(p => p.Id, p => "repoV2/" + p.Id);
        var gitRead = await CheckAsync(git, "GenericRead", gitTokens, ct).ConfigureAwait(false);
        var gitWrite = await CheckAsync(git, "GenericContribute", gitTokens, ct).ConfigureAwait(false);
        var projTokens = projects.ToDictionary(p => p.Id, p => "$PROJECT:vstfs:///Classification/TeamProject/" + p.Id);
        var testRead = project is null ? null : await CheckAsync(project, "VIEW_TEST_RESULTS", projTokens, ct).ConfigureAwait(false);
        var testWrite = await CheckAsync(css, "MANAGE_TEST_PLANS", areaTokens, ct).ConfigureAwait(false);
        var projRead = project is null ? null : await CheckAsync(project, "GENERIC_READ", projTokens, ct).ConfigureAwait(false);
        // Build permissions are set per project; the token is the project ID.
        var buildTokens = projects.ToDictionary(p => p.Id, p => p.Id);
        var buildRead = build is null ? null : await CheckAsync(build, "ViewBuilds", buildTokens, ct).ConfigureAwait(false);

        var result = new Dictionary<string, AreaRights>();
        foreach (var p in projects)
        {
            var r = new AreaRights();
            var wi = Pair(Bit(wiRead, p.Id), Bit(wiWrite, p.Id));
            r[AccessArea.WorkItems] = wi;
            r[AccessArea.Code] = Pair(Bit(gitRead, p.Id), Bit(gitWrite, p.Id));
            r[AccessArea.Test] = Pair(Bit(testRead, p.Id), Bit(testWrite, p.Id));
            r[AccessArea.Pipelines] = Pair(Bit(buildRead, p.Id), Check.NotTestable);
            var iterRead = projRead is null ? wi.Read : Access.Combine(Bit(projRead, p.Id), wi.Read);
            r[AccessArea.Iterations] = new AreaAccess(iterRead, iterRead == Check.No ? Check.No : Check.NotTestable);
            result[p.Id] = r;
        }

        return result;
    }

    /// <summary>Without reading an area there is no writing in it either.</summary>
    private static AreaAccess Pair(Check read, Check write) => new(read, read == Check.No ? Check.No : write);

    /// <summary>Answered permission → ja/nein; action not offered by this server → n/a.</summary>
    private static Check Bit(Dictionary<string, bool>? answers, string id) =>
        answers is null ? Check.NotTestable : answers.GetValueOrDefault(id) ? Check.Yes : Check.No;

    private sealed record Ns(string Id, Dictionary<string, int> Bits);

    private static Ns? Namespace(JsonNode? list, string name)
    {
        var ns = list?["value"]?.AsArray().FirstOrDefault(n => string.Equals(n?["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));
        if (ns?["namespaceId"]?.GetValue<string>() is not { } id)
        {
            return null;
        }

        var bits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in ns["actions"]?.AsArray() ?? [])
        {
            if (a?["name"]?.GetValue<string>() is { } n && a["bit"]?.GetValue<int>() is { } b)
            {
                bits[n] = b;
            }
        }

        return new Ns(id, bits);
    }

    /// <summary>projectId → allowed; null when the action does not exist in this server version.</summary>
    private async Task<Dictionary<string, bool>?> CheckAsync(Ns ns, string action, Dictionary<string, string> tokens, CancellationToken ct)
    {
        if (!ns.Bits.TryGetValue(action, out var bit))
        {
            return null;
        }

        var result = new Dictionary<string, bool>();
        foreach (var chunk in tokens.Chunk(TokensPerCall))
        {
            var joined = string.Join(',', chunk.Select(kv => kv.Value));
            var res = await client.GetAsync(
                $"_apis/permissions/{ns.Id}/{bit}?tokens={Uri.EscapeDataString(joined)}&alwaysAllowAdministrators=true&delimiter=,&api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
            var values = res?["value"]?.AsArray().Select(v => v?.GetValue<bool>() == true).ToList() ?? [];
            for (var i = 0; i < chunk.Length; i++)
            {
                result[chunk[i].Key] = i < values.Count && values[i];
            }
        }

        return result;
    }

    private static async Task<AreaRights> ProbeProjectAsync(AzdoClient pc, CancellationToken ct)
    {
        var p = pc.ProjectUrl;
        async Task<AreaAccess> ReadOnly(Func<Task<JsonNode?>> call)
        {
            var read = PatScopeProbe.ToCheck(await PatScopeProbe.Probe(call).ConfigureAwait(false));
            return new AreaAccess(read, read == Check.No ? Check.No : Check.NotTestable);
        }

        var r = new AreaRights();
        r[AccessArea.WorkItems] = await ReadOnly(() => pc.PostJsonAsync($"{p}/_apis/wit/wiql?$top=1&api-version={AzdoClient.ApiVersion}",
            new { query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project" }, ct)).ConfigureAwait(false);
        r[AccessArea.Code] = await ReadOnly(() => pc.GetAsync($"{p}/_apis/git/repositories?api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false);
        r[AccessArea.Pipelines] = await ReadOnly(() => pc.GetAsync($"{p}/_apis/build/builds?$top=1&api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false);
        r[AccessArea.Test] = await ReadOnly(() => pc.GetAsync($"{p}/_apis/test/runs?$top=1&api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false);
        r[AccessArea.Iterations] = await ReadOnly(() => pc.GetAsync($"{p}/_apis/work/teamsettings/iterations?api-version={AzdoClient.ApiVersion}", ct)).ConfigureAwait(false);
        return r;
    }
}
