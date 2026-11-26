using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Permissions;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

internal static class PlanningTools
{
    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("get_iterations", ToolGroup.Planning, "Listet die Iterationen (Sprints) eines Teams.",
            Schema.Create().Str("team", "Teamname (Standard: Standardteam des Projekts)")
                .Str("timeframe", "Nur current liefert den aktuellen Sprint", false, "current").Project().Build(), GetIterations);

        yield return new("get_iteration_capacity", ToolGroup.Planning, "Liefert die Kapazität der Teammitglieder in einer Iteration.",
            Schema.Create().Str("iterationId", "ID (GUID) der Iteration, siehe get_iterations", required: true)
                .Str("team", "Teamname (Standard: Standardteam des Projekts)").Project().Build(), GetCapacity);
    }

    private static string TeamBase(ToolInvocation t) =>
        t.Args.String("team") is { Length: > 0 } team ? $"{t.Azdo.ProjectUrl}/{Uri.EscapeDataString(team)}" : t.Azdo.ProjectUrl;

    private static async Task<string> GetIterations(ToolInvocation t)
    {
        var tf = t.Args.String("timeframe") is { Length: > 0 } f ? "$timeframe=" + Uri.EscapeDataString(f) + "&" : "";
        var res = await t.Azdo.GetAsync($"{TeamBase(t)}/_apis/work/teamsettings/iterations?{tf}api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);
        var list = new JsonArray();
        foreach (var i in res?["value"]?.AsArray() ?? [])
        {
            list.Add(new JsonObject
            {
                ["id"] = i?["id"]?.DeepClone(),
                ["name"] = i?["name"]?.DeepClone(),
                ["path"] = i?["path"]?.DeepClone(),
                ["startDate"] = i?["attributes"]?["startDate"]?.DeepClone(),
                ["finishDate"] = i?["attributes"]?["finishDate"]?.DeepClone(),
                ["timeFrame"] = i?["attributes"]?["timeFrame"]?.DeepClone(),
            });
        }

        return Output(new JsonObject { ["count"] = list.Count, ["iterations"] = list });
    }

    private static async Task<string> GetCapacity(ToolInvocation t)
    {
        var iteration = t.Args.RequiredString("iterationId");
        if (!Guid.TryParse(iteration, out var id))
        {
            throw new ToolArgumentException("Parameter „iterationId“ muss eine GUID sein.");
        }

        var res = await t.Azdo.GetAsync($"{TeamBase(t)}/_apis/work/teamsettings/iterations/{id}/capacities?api-version={AzdoClient.ApiVersion}", t.CancellationToken).ConfigureAwait(false);

        // API 6.0 returns { value: [...] }, newer versions { teamMembers: [...] }.
        var members = res?["value"]?.AsArray() ?? res?["teamMembers"]?.AsArray() ?? [];
        var list = new JsonArray();
        foreach (var m in members)
        {
            list.Add(new JsonObject
            {
                ["member"] = m?["teamMember"]?["displayName"]?.DeepClone(),
                ["activities"] = m?["activities"]?.DeepClone(),
                ["daysOff"] = m?["daysOff"]?.DeepClone(),
            });
        }

        return Output(new JsonObject { ["iterationId"] = id.ToString(), ["count"] = list.Count, ["capacities"] = list });
    }
}

internal static class SystemTools
{
    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("list_projects", ToolGroup.System,
            "Listet die eingerichteten Projekte (Kurzname, Verbindung, Server, Standard, Schreibschalter, effektive Rechte je Bereich als read/write mit ja, nein, n/a = nicht prüfbar oder ? = noch nicht geprüft, Status, PAT-Status). Den Kurznamen als „project“ an andere Tools übergeben.",
            Schema.Create().Build(), ListProjects, NeedsConnection: false);

        yield return new("ping", ToolGroup.System, "Prüft, ob der Server einer Verbindung erreichbar ist und der PAT gilt.",
            Schema.Create().Project().Build(), Ping);
    }

    private static Task<string> ListProjects(ToolInvocation t)
    {
        var today = DateOnly.FromDateTime(t.Time.GetLocalNow().DateTime);
        var list = new JsonArray();
        foreach (var p in t.Store.AllProjects())
        {
            var c = p.Connection;
            var status = ExpiryClassifier.Classify(c, today, t.Store.CanReadPat(c));
            var rights = new JsonObject();
            foreach (var area in Access.Areas)
            {
                var a = p.Effective[area];
                rights[area.ToString()] = new JsonObject { ["read"] = a.Read.Display(), ["write"] = a.Write.Display() };
            }

            list.Add(new JsonObject
            {
                ["project"] = p.Alias,
                ["azdoProject"] = p.Project.Name,
                ["connection"] = c.Name,
                ["server"] = c.Host,
                ["serverType"] = c.ServerType == ServerType.Services ? "Azure DevOps Services" : "Azure DevOps Server",
                ["isDefault"] = p.Project.IsDefault,
                ["writeSwitch"] = p.Project.AllowWrite,
                ["visible"] = p.Project.Visible,
                ["effectiveRights"] = rights,
                ["status"] = ProjectAccessStatus.For(p, t.Permissions).Text,
                ["patStatus"] = status.StatusText,
                ["patExpires"] = status.ExpiryText,
            });
        }

        return Task.FromResult(Output(new JsonObject { ["count"] = list.Count, ["projects"] = list }));
    }

    private static async Task<string> Ping(ToolInvocation t)
    {
        var data = await t.Azdo.GetAsync("_apis/connectionData?connectOptions=none&lastChangeId=-1", t.CancellationToken).ConfigureAwait(false);
        var user = data?["authenticatedUser"]?["providerDisplayName"]?.GetValue<string>();
        return Output(new JsonObject
        {
            ["project"] = t.Azdo.ProjectName,
            ["connection"] = t.Azdo.Connection.Name,
            ["status"] = "OK",
            ["message"] = user is null ? "Server erreichbar, PAT gültig." : $"Server erreichbar, angemeldet als {user}.",
        });
    }
}

public static class ToolCatalog
{
    private static readonly Lazy<IReadOnlyList<ToolDefinition>> Tools = new(() =>
        WorkItemReadTools.All()
            .Concat(WorkItemWriteTools.All())
            .Concat(TraceTools.All())
            .Concat(GitTools.All())
            .Concat(AnalysisTools.PullRequests())
            .Concat(PipelineTools.All())
            .Concat(TestTools.All())
            .Concat(AnalysisTools.TestResults())
            .Concat(PlanningTools.All())
            .Concat(SystemTools.All())
            .ToList());

    public static IReadOnlyList<ToolDefinition> All => Tools.Value;

    public static ToolDefinition? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}
