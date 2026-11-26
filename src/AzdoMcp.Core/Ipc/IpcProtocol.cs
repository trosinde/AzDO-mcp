using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzdoMcp.Core.Infrastructure;

namespace AzdoMcp.Core.Ipc;

/// <summary>One approval request as shown in the notification and on the Protokoll page.</summary>
public sealed record ApprovalRequest
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    public string Client { get; init; } = "";

    public string Tool { get; init; } = "";

    public string? Connection { get; init; }

    public int? WorkItemId { get; init; }

    /// <summary>e.g. "Claude Code möchte #48211 in SHOP ändern".</summary>
    public string Title { get; init; } = "";

    /// <summary>e.g. "update_work_item: Status Active → Resolved, Priorität 2 → 1".</summary>
    public string Summary { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public int ProcessId { get; init; }
}

public enum ApprovalOutcome
{
    Approved,
    Denied,
    TimedOut,
    ManagerUnavailable,
}

public static class MessageTypes
{
    // server → manager
    public const string Register = "register";
    public const string Heartbeat = "heartbeat";
    public const string ApproveRequest = "approve-request";
    public const string ApproveCancel = "approve-cancel";

    // manager → server
    public const string ApproveResponse = "approve-response";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Reload = "reload";
    public const string State = "state";
    public const string Show = "show";

    /// <summary>Exit gracefully so an update can replace azdo-mcp.exe.</summary>
    public const string Exit = "exit";
}

/// <summary>Envelope for all pipe messages; only the fields of the given type are set.</summary>
public sealed record IpcMessage
{
    public string Type { get; init; } = "";

    public string? RequestId { get; init; }

    public bool? Approved { get; init; }

    public bool? Paused { get; init; }

    public string? ClientName { get; init; }

    public int? ProcessId { get; init; }

    public string? Version { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? LastCall { get; init; }

    public ApprovalRequest? Request { get; init; }
}

/// <summary>4-byte little-endian length prefix + UTF-8 JSON.</summary>
public static class IpcFraming
{
    public const int MaxMessageBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(Json.Compact)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Encode(IpcMessage message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (json.Length > MaxMessageBytes)
        {
            throw new InvalidDataException("Nachricht zu groß.");
        }

        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>Reads one message; null at end of stream.</summary>
    public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEof(stream, header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes)
        {
            throw new InvalidDataException($"Ungültige Nachrichtenlänge {length}.");
        }

        var body = new byte[length];
        if (!await ReadExactlyOrEof(stream, body, ct).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Nachricht unvollständig.");
        }

        return JsonSerializer.Deserialize<IpcMessage>(body, Options);
    }

    private static async Task<bool> ReadExactlyOrEof(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Nachricht unvollständig.");
            }

            read += n;
        }

        return true;
    }
}

/// <summary>Serialises writes from several tasks onto one stream.</summary>
public sealed class IpcWriter(Stream stream)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SendAsync(IpcMessage message, CancellationToken ct = default)
    {
        var frame = IpcFraming.Encode(message);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(frame, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}

public interface IPipeClientTransport
{
    /// <summary>Connects to the Manager; null when it is not running.</summary>
    Task<Stream?> ConnectAsync(TimeSpan timeout, CancellationToken ct);
}

public static class PipeNames
{
    /// <summary>"AzdoMcp-&lt;SID&gt;"; AZDOMCP_PIPE overrides (tests, side-by-side installs).</summary>
    public static string ForCurrentUser()
    {
        var overridden = Environment.GetEnvironmentVariable("AZDOMCP_PIPE");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        if (OperatingSystem.IsWindows())
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            if (sid is not null)
            {
                return "AzdoMcp-" + sid;
            }
        }

        return "AzdoMcp-" + Environment.UserName;
    }
}

/// <summary>Client side of the Manager pipe; refuses pipes not owned by the current user.</summary>
public sealed class NamedPipeClientTransport(string pipeName) : IPipeClientTransport
{
    public string PipeName { get; } = pipeName;

    public async Task<Stream?> ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(timeout, ct).ConfigureAwait(false);
            return client;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }
}
