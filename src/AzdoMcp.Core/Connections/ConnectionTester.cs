using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Discovery;

namespace AzdoMcp.Core.Connections;

/// <param name="Code">Pill text: "OK", "401", "403", "Fehler", "Hinweis".</param>
public sealed record TestArea(string Name, StatusLevel Level, string Code, string Text, string? Hint = null);

public sealed record ConnectionTestResult(
    DateTimeOffset TestedAt,
    bool Reachable,
    bool Authenticated,
    string? UserName,
    string? ApiVersion,
    IReadOnlyList<TestArea> Areas,
    IReadOnlyList<string> Projects,
    AreaRights? PatScope)
{
    public bool AllOk => Areas.All(a => a.Level == StatusLevel.Ok);

    public bool Succeeded => Reachable && Authenticated;
}

/// <summary>
/// "Verbindung testen": reachability, login, expiry and the PAT scope per area.
/// The only non-read request is the validateOnly Work Items probe, which stores nothing.
/// </summary>
public sealed class ConnectionTester(AzdoClientFactory factory, TimeProvider? time = null)
{
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<ConnectionTestResult> TestAsync(Connection connection, string pat, CancellationToken ct = default)
    {
        var client = factory.Create(connection, pat, timeout: StepTimeout);
        var areas = new List<TestArea>();
        var now = _time.GetUtcNow();

        string? user;
        try
        {
            var data = await client.GetAsync("_apis/connectionData?connectOptions=none&lastChangeId=-1", ct).ConfigureAwait(false);
            user = data?["authenticatedUser"]?["providerDisplayName"]?.GetValue<string>()
                   ?? data?["authenticatedUser"]?["customDisplayName"]?.GetValue<string>();
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.Unreachable or AzdoErrorKind.Timeout)
        {
            areas.Add(new("Server", StatusLevel.Error, "Fehler", "Server nicht erreichbar", ex.Message));
            return new(now, false, false, null, null, areas, [], null);
        }
        catch (AzdoApiException ex) when (ex.Kind == AzdoErrorKind.Unauthorized)
        {
            areas.Add(new("Server", StatusLevel.Ok, "OK", "Server erreichbar"));
            areas.Add(new("Anmeldung", StatusLevel.Error, "401", "Anmeldung fehlgeschlagen", "PAT abgelaufen oder ungültig"));
            return new(now, true, false, null, null, areas, [], null);
        }
        catch (AzdoApiException ex)
        {
            areas.Add(new("Server", StatusLevel.Error, ex.StatusCode?.ToString() ?? "Fehler", "Server antwortet mit Fehler", ex.Message));
            return new(now, true, false, null, null, areas, [], null);
        }

        var apiVersion = await DetectApiVersionAsync(client, ct).ConfigureAwait(false);
        areas.Add(new("Server", StatusLevel.Ok, "OK", apiVersion is null ? "Server erreichbar" : $"Server erreichbar · API {apiVersion}"));
        areas.Add(new("Anmeldung", StatusLevel.Ok, "OK", user is null ? "Angemeldet" : $"Angemeldet als {user}"));

        List<ProjectDiscovery.ProjectInfo> projects;
        try
        {
            projects = await ProjectDiscovery.ListProjectsAsync(client, ct).ConfigureAwait(false);
        }
        catch (AzdoApiException ex)
        {
            areas.Add(new("Projekte", StatusLevel.Error, ex.StatusCode?.ToString() ?? "Fehler", "Projekte nicht lesbar", ex.Message));
            return new(now, true, true, user, apiVersion, areas, [], null);
        }

        // Probe on a configured project if possible, else on the first visible one.
        // A project right must not be mistaken for a PAT limit: prefer a configured project where the
        // user is known to write work items, then one where they read, then the default project.
        var probeName = connection.Projects
                            .Where(p => p.Visible && projects.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                            .OrderByDescending(ProbeRank)
                            .FirstOrDefault()?.Name
                        ?? projects.FirstOrDefault()?.Name;
        AreaRights scope;
        if (probeName is null)
        {
            scope = new AreaRights();
            areas.Add(new("Projekte", StatusLevel.Warning, "Hinweis", "Mit diesem PAT ist kein Projekt sichtbar"));
        }
        else
        {
            scope = await PatScopeProbe.ProbeAsync(client.ForProject(probeName), ct).ConfigureAwait(false);
        }

        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        if (connection.PatExpires is not null && ExpiryClassifier.Classify(connection, today).Level != StatusLevel.Ok)
        {
            var days = ExpiryClassifier.DaysLeft(connection, today) ?? 0;
            areas.Add(days < 0
                ? new("Ablauf", StatusLevel.Error, "abgelaufen", "PAT ist abgelaufen")
                : new("Ablauf", StatusLevel.Warning, "Hinweis", days == 1 ? "PAT läuft in 1 Tag ab" : $"PAT läuft in {days} Tagen ab"));
        }

        return new(now, true, true, user, apiVersion, areas, projects.Select(p => p.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(), scope);
    }

    private static int ProbeRank(ProjectEntry p)
    {
        var wi = p.ProjectRights?[AccessArea.WorkItems] ?? AreaAccess.Unknown;
        return (wi.Write == Check.Yes ? 4 : 0) + (wi.Read == Check.Yes ? 2 : 0) + (p.IsDefault ? 1 : 0) - (wi.Read == Check.No ? 8 : 0);
    }

    /// <summary>Stores the PAT scope and the test time on the connection.</summary>
    public static void Apply(ConnectionStore store, Connection connection, ConnectionTestResult result)
    {
        store.Update(connection.Id, c =>
        {
            c.LastTested = result.TestedAt;
            if (result.Succeeded)
            {
                if (result.PatScope is not null)
                {
                    c.PatScope = result.PatScope.Clone();
                }

                c.LastAuthFailure = null;
            }
            else if (result.Reachable && result.Areas.Any(a => a.Code == "401"))
            {
                c.LastAuthFailure = result.TestedAt;
            }
        });
    }

    private static async Task<string?> DetectApiVersionAsync(AzdoClient client, CancellationToken ct)
    {
        foreach (var v in new[] { "7.1", "7.0", "6.0" })
        {
            try
            {
                await client.GetAsync($"_apis/projects?$top=1&api-version={v}", ct).ConfigureAwait(false);
                return v;
            }
            catch (AzdoApiException)
            {
            }
        }

        return null;
    }
}
