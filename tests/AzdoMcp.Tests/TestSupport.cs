using System.Net;
using System.Security.Cryptography;
using System.Text;
using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;

namespace AzdoMcp.Tests;

/// <summary>Placeholder values only – no real servers or tokens in tests.</summary>
/// <summary>Shorthands for Lesen/Schreiben values.</summary>
internal static class Acc
{
    /// <summary>ja/ja</summary>
    public static readonly AreaAccess W = new(Check.Yes, Check.Yes);

    /// <summary>ja/nein – reading allowed, writing checked and denied.</summary>
    public static readonly AreaAccess Ro = new(Check.Yes, Check.No);

    /// <summary>ja/n/a – reading allowed, writing not testable.</summary>
    public static readonly AreaAccess Rn = new(Check.Yes, Check.NotTestable);

    /// <summary>nein/nein</summary>
    public static readonly AreaAccess N = new(Check.No, Check.No);

    public static AreaRights Of(AreaAccess wi, AreaAccess? code = null, AreaAccess? test = null, AreaAccess? iter = null, AreaAccess? pipelines = null)
    {
        var r = new AreaRights();
        r[AccessArea.WorkItems] = wi;
        r[AccessArea.Code] = code ?? Rn;
        r[AccessArea.Pipelines] = pipelines ?? Rn;
        r[AccessArea.Test] = test ?? Rn;
        r[AccessArea.Iterations] = iter ?? Rn;
        return r;
    }

    public static AreaRights All(AreaAccess a) => AreaRights.All(a.Read, a.Write);
}

internal static class Fake
{
    public const string Collection = "https://azdo.example.local/DefaultCollection";
    public const string Pat = "fakepat0123456789abcdefghijklmnopqrstuvwxyz0123";
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "azdomcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public AppPaths AppPaths => new(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Reversible fake for DPAPI; rejects blobs it did not create.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    private static readonly byte[] Magic = "FAKE"u8.ToArray();

    public int FailOnCall { get; set; } = -1;

    private int _calls;

    public byte[] Protect(byte[] plain)
    {
        if (_calls++ == FailOnCall)
        {
            throw new CryptographicException("simulated failure");
        }

        return [.. Magic, .. plain.Select(b => (byte)(b ^ 0x5A))];
    }

    public byte[] Unprotect(byte[] blob)
    {
        if (blob.Length < Magic.Length || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException("not ours");
        }

        return blob.Skip(Magic.Length).Select(b => (byte)(b ^ 0x5A)).ToArray();
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Url, string? Body, string? ContentType, string? Authorization);

/// <summary>Routes requests by method + URL substring to canned responses.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly List<(HttpMethod? Method, string Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Requests without the project pre-check of work item IDs (fields=System.TeamProject).</summary>
    public List<RecordedRequest> Calls => Requests.Where(r => !r.Url.Contains("fields=System.TeamProject", StringComparison.Ordinal)).ToList();

    public Func<HttpRequestMessage, Exception?>? Throw { get; set; }

    public TimeSpan Delay { get; set; }

    public FakeHttp On(string match, string json, HttpStatusCode status = HttpStatusCode.OK, HttpMethod? method = null)
    {
        _routes.Add((method, match, _ => Json(json, status)));
        return this;
    }

    public FakeHttp OnRaw(string match, Func<HttpRequestMessage, HttpResponseMessage> respond, HttpMethod? method = null)
    {
        _routes.Add((method, match, respond));
        return this;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Html(HttpStatusCode status) =>
        new(status) { Content = new StringContent("<html><body>Not found</body></html>", Encoding.UTF8, "text/html") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // Tools load details in parallel (Shape.ForEachAsync), so recording must be thread-safe.
        lock (Requests)
        {
            Requests.Add(new(request.Method, request.RequestUri!.ToString(), body, request.Content?.Headers.ContentType?.MediaType, request.Headers.Authorization?.ToString()));
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (Throw?.Invoke(request) is { } ex)
        {
            throw ex;
        }

        // Last registered route wins, so tests can override defaults.
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            var r = _routes[i];
            if ((r.Method is null || r.Method == request.Method) &&
                Uri.UnescapeDataString(request.RequestUri!.ToString()).Contains(r.Match, StringComparison.OrdinalIgnoreCase))
            {
                return r.Respond(request);
            }
        }

        return Html(HttpStatusCode.NotFound);
    }
}

/// <summary>Store + executor wired with fakes.</summary>
internal sealed class Harness : IDisposable
{
    public Harness(params string[] shortNames)
    {
        Store = new ConnectionStore(Dir.AppPaths, Protector);
        Audit = new AuditLog(Dir.AppPaths);
        Permissions = new PermissionStore(Dir.AppPaths);
        Executor = new ToolExecutor(Store, new AzdoClientFactory(Http, TimeSpan.FromSeconds(5)), Audit, Permissions, Manager);
        foreach (var name in shortNames)
        {
            Add(name);
        }
    }

    public TempDir Dir { get; } = new();

    public FakeProtector Protector { get; } = new();

    public FakeHttp Http { get; } = new();

    public ConnectionStore Store { get; }

    public AuditLog Audit { get; }

    public PermissionStore Permissions { get; }

    /// <summary>Approves every request unless a test changes <see cref="FakeManager.Outcome"/>.</summary>
    public FakeManager Manager { get; } = new();

    public ToolExecutor Executor { get; }

    /// <summary>One connection with one project whose alias is <paramref name="alias"/> (no caps: rights unknown, write switch on).</summary>
    public Connection Add(string alias, bool isDefault = false, string? project = null) =>
        Store.Save(new Connection
        {
            Name = alias,
            CollectionUrl = Fake.Collection,
            Projects = [new ProjectEntry { Name = project ?? alias, Alias = alias, IsDefault = isDefault, AllowWrite = true }],
        }, Fake.Pat);

    public ProjectRef Proj(string alias) => Store.FindProject(alias) ?? throw new InvalidOperationException(alias);

    public Connection Conn(string alias) => Proj(alias).Connection;

    /// <summary>Changes the project with that alias (and saves).</summary>
    public void SetProject(string alias, Action<ProjectEntry> change)
    {
        var r = Proj(alias);
        Store.Update(r.Connection.Id, c => change(c.Projects.Single(p => p.Key == r.Project.Key)));
    }

    public void SetConn(string alias, Action<Connection> change) => Store.Update(Conn(alias).Id, change);

    public Task<ToolCallResult> Call(string tool, object? args = null, string client = "Claude Code")
    {
        var json = System.Text.Json.JsonSerializer.SerializeToElement(args ?? new { });
        var dict = json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return Executor.ExecuteAsync(tool, dict, client);
    }

    public IReadOnlyList<AuditRecord> Records() =>
        Audit.Read(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    public void Dispose() => Dir.Dispose();
}

internal sealed class FakeManager : IManagerChannel
{
    public ApprovalOutcome Outcome { get; set; } = ApprovalOutcome.Approved;

    public bool IsPaused { get; set; }

    public TimeSpan ApprovalTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public List<ApprovalRequest> Requests { get; } = [];

    public void SetClientName(string clientName)
    {
    }

    public void NotifyCall()
    {
    }

    public Task<ApprovalOutcome> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Outcome);
    }
}

/// <summary>In-memory stand-in for the named pipe: both ends of the Manager connection.</summary>
internal sealed class InMemoryPipeHub : AzdoMcp.Manager.Core.Ipc.IPipeServerTransport, IPipeClientTransport
{
    private readonly System.Threading.Channels.Channel<Stream> _incoming = System.Threading.Channels.Channel.CreateUnbounded<Stream>();

    /// <summary>False simulates "Manager not running".</summary>
    public bool Available { get; set; } = true;

    public Task<Stream?> ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (!Available)
        {
            return Task.FromResult<Stream?>(null);
        }

        var a = new System.IO.Pipelines.Pipe();
        var b = new System.IO.Pipelines.Pipe();
        _incoming.Writer.TryWrite(new DuplexStream(b.Reader.AsStream(), a.Writer.AsStream()));
        return Task.FromResult<Stream?>(new DuplexStream(a.Reader.AsStream(), b.Writer.AsStream()));
    }

    public async Task<Stream> AcceptAsync(CancellationToken ct) => await _incoming.Reader.ReadAsync(ct);
}

internal sealed class DuplexStream(Stream read, Stream write) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => write.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => write.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => write.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            write.Dispose();
            read.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class Eventually
{
    public static async Task True(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                Assert.Fail("Bedingung nicht rechtzeitig erfüllt.");
            }

            await Task.Delay(20);
        }
    }
}
