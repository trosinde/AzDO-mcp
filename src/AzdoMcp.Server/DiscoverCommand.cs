using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Discovery;
using AzdoMcp.Core.Permissions;

namespace AzdoMcp.Server;

/// <summary>
/// azdo-mcp discover &lt;verbindung&gt; [--apply] – CLI counterpart of the Manager's project discovery.
/// Without --apply nothing is changed. Never prints the PAT.
/// </summary>
internal static class DiscoverCommand
{
    public static async Task<int> RunAsync(string[] args, ConnectionStore store, AzdoClientFactory clients, TextWriter output, TextWriter error, CancellationToken ct = default, PermissionSettings? permissions = null)
    {
        var rest = args.SkipWhile(a => a != "discover").Skip(1).ToList();
        var apply = rest.Remove("--apply");
        var name = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        IReadOnlyList<Connection> all;
        try
        {
            all = store.GetAll();
        }
        catch (ConfigurationException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        var connection = name is null ? null : store.FindConnection(name);
        if (connection is null)
        {
            error.WriteLine(name is null ? "Aufruf: azdo-mcp discover <verbindung> [--apply]" : $"Unbekannte Verbindung „{name}“.");
            error.WriteLine(all.Count == 0 ? "Keine Verbindung eingerichtet." : "Verbindungen: " + string.Join(", ", all.Select(c => c.Name)));
            return 1;
        }

        string pat;
        try
        {
            pat = store.GetPat(connection);
        }
        catch (SecretUnreadableException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        DiscoveryResult result;
        try
        {
            result = await new ProjectDiscovery(clients).DiscoverAsync(connection, pat, ct).ConfigureAwait(false);
        }
        catch (AzdoApiException ex)
        {
            error.WriteLine(SecretScrubber.Scrub(ex.Message, pat));
            return 1;
        }

        var diff = DiscoveryMerger.Diff(connection, result);
        output.WriteLine($"Verbindung {connection.Name} · {connection.Host}{(result.UserName is null ? "" : " · angemeldet als " + result.UserName)}");
        output.WriteLine("PAT-Scope (Lesen/Schreiben): " + string.Join(" · ", Access.Areas.Select(a => $"{a.Display()} {Cell(result.PatScope[a])}")));
        foreach (var note in result.Notes)
        {
            output.WriteLine("Hinweis: " + note);
        }

        output.WriteLine();
        output.WriteLine("Rechte je Bereich als Lesen/Schreiben: ja, nein, n/a = nicht prüfbar, ? = noch nicht geprüft.");
        output.WriteLine($"{"Projekt",-32} {"Work Items",-13} {"Code",-13} {"Pipelines",-13} {"Test",-13} {"Iterationen",-13} Status");
        foreach (var d in result.Projects)
        {
            var status = diff.New.Contains(d) ? "neu"
                : diff.Changed.Any(c => c.Now == d) ? "geändert"
                : "eingerichtet";
            if (!diff.New.Contains(d) && connection.Projects.FirstOrDefault(p => p.AzdoId == d.AzdoId || p.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase)) is { } entry)
            {
                var updated = entry.Clone();
                updated.ProjectRights = d.ProjectRights;
                var scoped = connection.Clone();
                scoped.PatScope = result.PatScope;
                status += " · " + ProjectAccessStatus.For(new ProjectRef(scoped, updated), permissions ?? new PermissionSettings()).Text;
            }
            output.WriteLine($"{Trim(d.Name, 32),-32} {Cell(d.Effective[AccessArea.WorkItems]),-13} {Cell(d.Effective[AccessArea.Code]),-13} {Cell(d.Effective[AccessArea.Pipelines]),-13} {Cell(d.Effective[AccessArea.Test]),-13} {Cell(d.Effective[AccessArea.Iterations]),-13} {status}");
        }

        foreach (var m in diff.Missing)
        {
            output.WriteLine($"{Trim(m.Name, 32),-32} {"",-13} {"",-13} {"",-13} {"",-13} {"",-13} nicht mehr sichtbar");
        }

        output.WriteLine();
        if (!apply)
        {
            output.WriteLine($"{diff.New.Count} neu, {diff.Changed.Count} geändert, {diff.Missing.Count} nicht mehr sichtbar. Nichts geändert – mit --apply übernehmen.");
            return 0;
        }

        var choices = DiscoveryMerger.DefaultChoices(diff, store.SuggestAlias);
        var merged = DiscoveryMerger.Apply(connection, result, choices);
        store.Save(merged.Connection, null);
        output.WriteLine($"Übernommen: {choices.Count} neue Projekte (Lesen erlaubt, Schreiben nachfragen, Schreibschalter aus), Rechte von {diff.Changed.Count + diff.Unchanged.Count} Projekten aktualisiert, {diff.Missing.Count} als nicht mehr sichtbar markiert.");
        return 0;
    }

    private static string Cell(AreaAccess a) => $"{a.Read.Display()}/{a.Write.Display()}";

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
