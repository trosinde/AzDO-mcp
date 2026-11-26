using AzdoMcp.Core.Connections;

namespace AzdoMcp.Core.Tools;

/// <summary>
/// Instructions sent at MCP initialize: the Kurznamen of all visible projects, so the AI knows from the
/// first message that "SHOP" means an Azure DevOps project. Contains no PAT and no collection URL.
/// </summary>
public static class ServerInstructions
{
    public static string Build(IReadOnlyList<ProjectRef> projects)
    {
        var shown = projects.Where(p => p.Project.Visible).ToList();
        if (shown.Count == 0)
        {
            return "Azure-DevOps-Werkzeuge. Noch kein Projekt eingerichtet – im AzDO MCP Manager unter „Verbindungen & PATs“ eine Verbindung anlegen und Projekte erkennen.";
        }

        var lines = new List<string> { "Azure-DevOps-Projekte (Kurzname = Projektname (Verbindung)):" };
        lines.AddRange(shown.Select(p => $"{p.Alias} = {p.Project.Name} ({p.Connection.Name})"));
        if (shown.FirstOrDefault(p => p.Project.IsDefault) is { } standard)
        {
            lines.Add($"Standardprojekt: {standard.Alias}");
        }

        var first = shown[0].Alias;
        lines.Add($"Nennt der Nutzer einen dieser Kurznamen, ist dieses Azure-DevOps-Projekt gemeint: die Tools direkt mit project=\"<Kurzname>\" aufrufen, ohne nachzufragen. Beispiel: „gib mir aus {first} das Work Item 4711“ → get_work_item mit project=\"{first}\", id=4711.");
        lines.Add("Für Kennzahlen und Übersichten analyze_pull_requests bzw. analyze_test_results nutzen statt vieler Einzelabfragen; trace_links verfolgt Verbindungen zwischen Work Items, Pull Requests, Commits, Builds und Tests.");
        return string.Join("\n", lines);
    }
}
