using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace AzdoMcp.Tests;

/// <summary>
/// In-memory Azure DevOps collection for discovery tests. Placeholder data only.
/// Default projects: Alpha (user may write), Beta (read only), Gamma (no access).
/// </summary>
internal sealed class FakeAzdo : HttpMessageHandler
{
    public const string CssNs = "83e28ad4-2d72-4ceb-97b0-c7726d5502c3";
    public const string GitNs = "2e9eb7ed-3c0a-47d4-87c1-0ffdd275fd87";
    public const string ProjectNs = "52d39943-cb85-4d7f-8fa8-c6baac873819";
    public const string BuildNs = "33344d9c-fc72-4d6f-aba5-fa317101a7e9";

    public sealed record FakeProject(string Id, string Name, bool WiRead, bool WiWrite, bool GitRead, bool GitWrite, bool TestRead);

    public List<FakeProject> Projects { get; } =
    [
        new("id-alpha", "Alpha", true, true, true, true, true),
        new("id-beta", "Beta", true, false, true, false, true),
        new("id-gamma", "Gamma", false, false, false, false, false),
    ];

    /// <summary>Projects per page; the fake ignores $top to force paging.</summary>
    public int PageSize { get; set; } = 2;

    public bool SecurityApi { get; set; } = true;

    /// <summary>PAT allows creating work items (validateOnly answers 200).</summary>
    public bool PatWorkItemsWrite { get; set; } = true;

    public bool PatTest { get; set; } = true;

    /// <summary>PAT allows reading builds.</summary>
    public bool PatBuild { get; set; } = true;

    /// <summary>Projects (by name) whose user may not view builds although Git is readable.</summary>
    public HashSet<string> BuildDenied { get; } = [];

    /// <summary>teamsettings/iterations answers 404 (no default team).</summary>
    public bool IterationsMissing { get; set; }

    public string User { get; set; } = "Max Muster";

    public List<(HttpMethod Method, string Url, string? Auth)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
        lock (Requests)
        {
            Requests.Add((request.Method, url, request.Headers.Authorization?.ToString()));
        }
        if (request.Content is not null)
        {
            await request.Content.ReadAsStringAsync(ct);
        }

        if (url.Contains("/_apis/connectionData"))
        {
            return Json(new JsonObject { ["authenticatedUser"] = new JsonObject { ["id"] = "user-1", ["providerDisplayName"] = User } });
        }

        if (url.Contains("/_apis/projects"))
        {
            var start = 0;
            var q = request.RequestUri!.Query;
            var tokenIndex = q.IndexOf("continuationToken=", StringComparison.Ordinal);
            if (tokenIndex >= 0)
            {
                start = int.Parse(q[(tokenIndex + "continuationToken=".Length)..].Split('&')[0]);
            }

            var page = Projects.Skip(start).Take(PageSize).ToList();
            var res = Json(new JsonObject { ["value"] = new JsonArray(page.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name }).ToArray()) });
            if (start + PageSize < Projects.Count)
            {
                res.Headers.Add("x-ms-continuationtoken", (start + PageSize).ToString());
            }

            return res;
        }

        if (url.Contains("/_apis/securitynamespaces"))
        {
            if (!SecurityApi)
            {
                return Status(HttpStatusCode.Forbidden);
            }

            return Json(new JsonObject
            {
                ["value"] = new JsonArray(
                    Ns(CssNs, "CSS", ("GENERIC_READ", 1), ("WORK_ITEM_READ", 16), ("WORK_ITEM_WRITE", 32), ("MANAGE_TEST_PLANS", 64)),
                    Ns(GitNs, "Git Repositories", ("GenericRead", 2), ("GenericContribute", 4)),
                    Ns(ProjectNs, "Project", ("GENERIC_READ", 1), ("VIEW_TEST_RESULTS", 512)),
                    Ns(BuildNs, "Build", ("ViewBuilds", 1))),
            });
        }

        if (url.Contains("/_apis/permissions/"))
        {
            var parts = request.RequestUri!.AbsolutePath.Split('/');
            var ns = parts[^2];
            var bit = int.Parse(parts[^1]);
            var tokens = Uri.UnescapeDataString(request.RequestUri.Query.Split("tokens=")[1].Split('&')[0]).Split(',');
            var values = tokens.Select(t => (JsonNode)Allowed(ns, bit, t)).ToArray();
            return Json(new JsonObject { ["value"] = new JsonArray(values) });
        }

        // Project-scoped calls: /DefaultCollection/<project>/_apis/...
        var project = Projects.FirstOrDefault(p => url.Contains("/" + p.Name + "/_apis/", StringComparison.Ordinal));
        if (project is null)
        {
            return Status(HttpStatusCode.NotFound, html: true);
        }

        if (url.Contains("/_apis/wit/classificationnodes/areas"))
        {
            return project.WiRead ? Json(new JsonObject { ["identifier"] = "area-" + project.Id }) : Status(HttpStatusCode.Forbidden);
        }

        if (url.Contains("/_apis/wit/wiql"))
        {
            return project.WiRead ? Json(new JsonObject { ["workItems"] = new JsonArray() }) : Status(HttpStatusCode.Forbidden);
        }

        if (url.Contains("validateOnly=true"))
        {
            return PatWorkItemsWrite && project.WiWrite ? Json(new JsonObject { ["id"] = -1 }) : Status(HttpStatusCode.Forbidden);
        }

        if (url.Contains("/_apis/git/repositories"))
        {
            return project.GitRead ? Json(new JsonObject { ["value"] = new JsonArray() }) : Status(HttpStatusCode.Forbidden);
        }

        if (url.Contains("/_apis/build/builds"))
        {
            return PatBuild && project.GitRead && !BuildDenied.Contains(project.Name) ? Json(new JsonObject { ["value"] = new JsonArray() }) : Status(HttpStatusCode.Forbidden);
        }

        if (url.Contains("/_apis/test/runs"))
        {
            return PatTest && project.TestRead ? Json(new JsonObject { ["value"] = new JsonArray() }) : Status(HttpStatusCode.Forbidden);
        }

        if (url.Contains("/_apis/work/teamsettings/iterations"))
        {
            if (IterationsMissing)
            {
                return Status(HttpStatusCode.NotFound);
            }

            return project.WiRead ? Json(new JsonObject { ["value"] = new JsonArray() }) : Status(HttpStatusCode.Forbidden);
        }

        return Status(HttpStatusCode.NotFound, html: true);
    }

    private bool Allowed(string ns, int bit, string token)
    {
        FakeProject? ById() => Projects.FirstOrDefault(p => token.EndsWith(p.Id, StringComparison.Ordinal));
        if (ns == CssNs)
        {
            var p = Projects.FirstOrDefault(x => token.EndsWith("area-" + x.Id, StringComparison.Ordinal));
            return p is not null && bit switch { 16 => p.WiRead, 32 => p.WiWrite, 64 => p.WiWrite, _ => p.WiRead };
        }

        if (ns == GitNs)
        {
            var p = ById();
            return p is not null && (bit == 4 ? p.GitWrite : p.GitRead);
        }

        if (ns == BuildNs)
        {
            var p = Projects.FirstOrDefault(x => token == x.Id);
            return p is not null && p.GitRead && !BuildDenied.Contains(p.Name);
        }

        var proj = ById();
        return proj is not null && (bit == 512 ? proj.TestRead : proj.WiRead || proj.GitRead);
    }

    private static JsonNode Ns(string id, string name, params (string Name, int Bit)[] actions) => new JsonObject
    {
        ["namespaceId"] = id,
        ["name"] = name,
        ["actions"] = new JsonArray(actions.Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["bit"] = a.Bit }).ToArray()),
    };

    private static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status, bool html = false) =>
        new(status) { Content = html ? new StringContent("<html/>", Encoding.UTF8, "text/html") : new StringContent("""{"message":"denied"}""", Encoding.UTF8, "application/json") };
}
