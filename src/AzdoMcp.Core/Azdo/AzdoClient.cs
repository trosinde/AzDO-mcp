using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzdoMcp.Core.Connections;

namespace AzdoMcp.Core.Azdo;

public enum AzdoErrorKind
{
    Unauthorized,
    Forbidden,
    NotAvailable,
    NotFound,
    BadRequest,
    Unreachable,
    Timeout,
    Other,
}

/// <summary>An Azure DevOps call failed. <see cref="Exception.Message"/> is German, short and free of secrets.</summary>
public sealed class AzdoApiException(AzdoErrorKind kind, int? status, string message) : Exception(message)
{
    public AzdoErrorKind Kind { get; } = kind;

    public int? StatusCode { get; } = status;

    /// <summary>Short text for the protocol column "Ergebnis".</summary>
    public string ShortResult => Kind switch
    {
        AzdoErrorKind.Unauthorized => "401 PAT abgelaufen",
        AzdoErrorKind.Forbidden => "403 Scope fehlt",
        AzdoErrorKind.NotAvailable => "nicht verfügbar",
        AzdoErrorKind.Unreachable => "nicht erreichbar",
        AzdoErrorKind.Timeout => "Zeitüberschreitung",
        _ => StatusCode is { } s ? $"{s} Fehler" : "Fehler",
    };
}

/// <summary>Creates <see cref="AzdoClient"/>s sharing one HTTP handler.</summary>
public sealed class AzdoClientFactory
{
    private readonly HttpClient _http;

    public AzdoClientFactory(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        CallTimeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    public TimeSpan CallTimeout { get; }

    /// <param name="projectName">Azure DevOps project for project-scoped URLs; null for collection-level calls.</param>
    public AzdoClient Create(Connection connection, string pat, string? projectName = null, TimeSpan? timeout = null) =>
        new(_http, connection, projectName, pat, timeout ?? CallTimeout);

    public AzdoClient Create(ProjectRef project, string pat, TimeSpan? timeout = null) =>
        Create(project.Connection, pat, project.Project.Name, timeout);
}

/// <summary>Thin REST wrapper for one connection. URLs are absolute or relative to the collection.</summary>
public sealed class AzdoClient
{
    public const string ApiVersion = "6.0";
    public const string CommentsApiVersion = "6.0-preview.3";

    private readonly HttpClient _http;
    private readonly string _pat;
    private readonly TimeSpan _timeout;
    private readonly AuthenticationHeaderValue _auth;

    internal AzdoClient(HttpClient http, Connection connection, string? projectName, string pat, TimeSpan timeout)
    {
        _http = http;
        _pat = pat;
        _timeout = timeout;
        Connection = connection;
        ProjectName = projectName;
        _auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));
    }

    public Connection Connection { get; }

    public string? ProjectName { get; }

    /// <summary>{collection}/{project}</summary>
    public string ProjectUrl => ProjectName is null
        ? throw new InvalidOperationException("Kein Projekt gewählt.")
        : $"{CollectionUrl}/{Uri.EscapeDataString(ProjectName)}";

    /// <summary>Same connection and PAT, other project.</summary>
    public AzdoClient ForProject(string projectName) => new(_http, Connection, projectName, _pat, _timeout);

    public string CollectionUrl => Connection.CollectionBase;

    public Task<JsonNode?> GetAsync(string url, CancellationToken ct) => SendAsync(HttpMethod.Get, url, null, ct);

    public Task<JsonNode?> PostJsonAsync(string url, object body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, url, JsonContent(body, "application/json"), ct);

    public Task<JsonNode?> PatchJsonAsync(string url, object body, CancellationToken ct) =>
        SendAsync(HttpMethod.Patch, url, JsonContent(body, "application/json"), ct);

    public Task<JsonNode?> JsonPatchAsync(HttpMethod method, string url, JsonArray operations, CancellationToken ct) =>
        SendAsync(method, url, JsonContent(operations, "application/json-patch+json"), ct);

    public Task<JsonNode?> PostBytesAsync(string url, byte[] content, CancellationToken ct)
    {
        var c = new ByteArrayContent(content);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return SendAsync(HttpMethod.Post, url, c, ct);
    }

    public async Task<JsonNode?> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct) =>
        (await SendWithHeadersAsync(method, url, content, ct).ConfigureAwait(false)).Body;

    /// <summary>GET returning the body and the continuation token header (paging).</summary>
    public async Task<(JsonNode? Body, string? ContinuationToken)> GetPageAsync(string url, CancellationToken ct)
    {
        var (body, headers) = await SendWithHeadersAsync(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
        var token = headers.TryGetValues("x-ms-continuationtoken", out var values) ? values.FirstOrDefault() : null;
        return (body, string.IsNullOrWhiteSpace(token) ? null : token);
    }

    private async Task<(JsonNode? Body, HttpResponseHeaders Headers)> SendWithHeadersAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        var absolute = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{CollectionUrl}/{url.TrimStart('/')}";
        using var request = new HttpRequestMessage(method, absolute) { Content = content };
        request.Headers.Authorization = _auth;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AzdoApiException(AzdoErrorKind.Timeout, null, $"Zeitüberschreitung nach {_timeout.TotalSeconds:0} s – Server antwortet nicht.");
        }
        catch (HttpRequestException ex)
        {
            throw new AzdoApiException(AzdoErrorKind.Unreachable, null, Scrub($"Server nicht erreichbar: {ex.Message}"));
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(body))
                {
                    return (null, response.Headers);
                }

                if (IsHtml(response, body))
                {
                    // On-prem servers redirect unauthenticated calls to an HTML sign-in page.
                    throw new AzdoApiException(AzdoErrorKind.Unauthorized, 401, "401 PAT abgelaufen oder ungültig.");
                }

                try
                {
                    return (JsonNode.Parse(body), response.Headers);
                }
                catch (JsonException)
                {
                    throw new AzdoApiException(AzdoErrorKind.Other, (int)response.StatusCode, "Antwort des Servers ist kein JSON.");
                }
            }

            throw MapError(response, body);
        }
    }

    private AzdoApiException MapError(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var serverMessage = ExtractMessage(body);
        var typeKey = ExtractTypeKey(body);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return new(AzdoErrorKind.Unauthorized, status, "401 PAT abgelaufen oder ungültig.");
            case HttpStatusCode.Forbidden:
                return new(AzdoErrorKind.Forbidden, status, Scrub("403 Scope fehlt" + (serverMessage is null ? "." : $": {serverMessage}")));
            case HttpStatusCode.NotFound when serverMessage is null || IsHtml(response, body):
                return new(AzdoErrorKind.NotAvailable, status, "Auf diesem Server nicht verfügbar.");
            case HttpStatusCode.NotFound:
                return new(AzdoErrorKind.NotFound, status, Scrub($"404 Nicht gefunden: {serverMessage}"));
            case HttpStatusCode.BadRequest when typeKey is not null && typeKey.Contains("VersionOutOfRange", StringComparison.OrdinalIgnoreCase):
                return new(AzdoErrorKind.NotAvailable, status, "Auf diesem Server nicht verfügbar (API-Version zu neu).");
            case HttpStatusCode.BadRequest:
                return new(AzdoErrorKind.BadRequest, status, Scrub($"400 Ungültige Anfrage: {serverMessage ?? "keine Details"}"));
            default:
                return new(AzdoErrorKind.Other, status, Scrub($"{status} {response.ReasonPhrase}: {serverMessage ?? "keine Details"}"));
        }
    }

    private string Scrub(string text) => SecretScrubber.Scrub(text, _pat);

    private static bool IsHtml(HttpResponseMessage response, string body) =>
        response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true ||
        body.TrimStart().StartsWith('<');

    private static string? ExtractMessage(string body) => ExtractField(body, "message");

    private static string? ExtractTypeKey(string body) => ExtractField(body, "typeKey") ?? ExtractField(body, "typeName");

    private static string? ExtractField(string body, string name)
    {
        if (string.IsNullOrWhiteSpace(body) || body.TrimStart()[0] != '{')
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body)?[name]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static StringContent JsonContent(object body, string mediaType)
    {
        var json = body is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(body);
        return new StringContent(json, Encoding.UTF8, mediaType);
    }
}
