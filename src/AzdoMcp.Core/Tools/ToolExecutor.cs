using System.Diagnostics;
using System.Text.Json;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Core.Permissions;

namespace AzdoMcp.Core.Tools;

public sealed record ToolCallResult(string Text, bool IsError);

/// <summary>
/// Resolves the connection, applies permissions (block / approval / pause), runs one tool,
/// maps errors and writes the audit record.
/// </summary>
public sealed class ToolExecutor
{
    public const string PausedText = "Vom Benutzer pausiert";
    public const string DeniedText = "Vom Benutzer abgelehnt";
    public const string TimedOutText = "Keine Antwort – abgelehnt";
    public const string NoManagerText = "AzDO MCP Manager nicht gestartet – Freigabe nicht möglich";

    private readonly ConnectionStore _store;
    private readonly AzdoClientFactory _clients;
    private readonly AuditLog _audit;
    private readonly PermissionStore _permissions;
    private readonly IManagerChannel _manager;
    private readonly TimeProvider _time;

    public ToolExecutor(ConnectionStore store, AzdoClientFactory clients, AuditLog audit,
        PermissionStore? permissions = null, IManagerChannel? manager = null, TimeProvider? time = null)
    {
        _store = store;
        _clients = clients;
        _audit = audit;
        _permissions = permissions ?? new PermissionStore(store.Paths);
        _manager = manager ?? new NoManagerChannel();
        _time = time ?? TimeProvider.System;
    }

    public PermissionStore Permissions => _permissions;

    /// <summary>Instructions for MCP initialize with the current Kurznamen.</summary>
    public string Instructions()
    {
        try
        {
            return ServerInstructions.Build(_store.AllProjects());
        }
        catch (ConfigurationException)
        {
            return ServerInstructions.Build([]);
        }
    }

    /// <summary>Tools to offer in tools/list for the current configuration.</summary>
    public IReadOnlyList<ToolDefinition> VisibleTools()
    {
        try
        {
            return ToolListing.Visible(_permissions.Get(), _store.AllProjects());
        }
        catch (ConfigurationException)
        {
            return ToolCatalog.All;
        }
    }

    public async Task<ToolCallResult> ExecuteAsync(string toolName, IReadOnlyDictionary<string, JsonElement>? arguments, string clientName, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var tool = ToolCatalog.Find(toolName);
        var args = new ToolArgs(arguments);
        ProjectRef? target = null;
        int? workItemId = null;
        string? pat = null;

        string? requestId = null;
        _manager.SetClientName(clientName);
        _manager.NotifyCall();

        ToolCallResult Finish(string text, bool isError, AuditOutcome outcome, string result)
        {
            if (pat is not null)
            {
                text = SecretScrubber.Scrub(text, pat);
            }

            TryAudit(new AuditRecord
            {
                Time = _time.GetUtcNow(),
                Client = clientName,
                Tool = toolName,
                Connection = target?.Alias,
                DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Outcome = outcome,
                Result = result,
                IsWrite = tool?.IsWrite ?? false,
                WorkItemId = workItemId,
                RequestId = requestId,
                ProcessId = Environment.ProcessId,
            });
            return new ToolCallResult(text, isError);
        }

        if (tool is null)
        {
            return Finish($"Unbekanntes Tool „{toolName}“.", true, AuditOutcome.Error, "unbekanntes Tool");
        }

        if (_manager.IsPaused)
        {
            return Finish(PausedText + " – im AzDO MCP Manager fortsetzen.", true, AuditOutcome.Blocked, "pausiert");
        }

        try
        {
            AzdoClient? client = null;
            if (tool.NeedsConnection)
            {
                target = Resolve(args.String("project"), out var error);
                if (target is null)
                {
                    return Finish(error!, true, AuditOutcome.Error, "keine Verbindung");
                }

            }

            PermissionDecision decision;
            try
            {
                decision = PermissionEvaluator.Evaluate(tool, target, _permissions.Get());
            }
            catch (ConfigurationException ex)
            {
                // Fail closed: an unreadable permission file never grants rights.
                return Finish($"Rechte nicht lesbar – AzDO MCP Manager öffnen. {ex.Message}", true, AuditOutcome.Blocked, "gesperrt");
            }

            if (decision.Mode == PermissionMode.Blocked)
            {
                workItemId = SafeId(args);
                return Finish(decision.Reason + ".", true, AuditOutcome.Blocked, "gesperrt");
            }

            if (target is not null)
            {
                pat = _store.GetPat(target.Connection);
                client = _clients.Create(target, pat);
            }

            // Work item IDs are collection-wide: an item of another project is refused before any
            // approval request or write, so project settings cannot be bypassed.
            if (target is not null && client is not null && WorkItemIdTools.Contains(tool.Name) && SafeId(args) is { } checkId)
            {
                if (await ForeignProjectAsync(client, target, checkId, ct).ConfigureAwait(false) is { } foreign)
                {
                    workItemId = checkId;
                    return Finish(foreign, true, AuditOutcome.Blocked, "abgelehnt");
                }
            }

            if (decision.Mode == PermissionMode.Ask)
            {
                var denied = await AskAsync(tool, args, target, client, clientName, started, ct).ConfigureAwait(false);
                requestId = denied.RequestId;
                workItemId = SafeId(args);
                if (denied.Result is { } refusal)
                {
                    return Finish(refusal.Text, true, AuditOutcome.Denied, refusal.Audit);
                }
            }

            var invocation = new ToolInvocation
            {
                Args = args,
                Client = client,
                Store = _store,
                Time = _time,
                Permissions = _permissions.Get(),
                ProjectAlias = target?.Alias,
                Target = target,
                CancellationToken = ct,
            };

            try
            {
                var text = await tool.Handler(invocation).ConfigureAwait(false);
                workItemId = invocation.WorkItemId;
                MarkAuth(target?.Connection, failed: false);
                return Finish(text, false, AuditOutcome.Ok, "OK");
            }
            finally
            {
                workItemId ??= invocation.WorkItemId;
            }
        }
        catch (AzdoApiException ex)
        {
            if (ex.Kind == AzdoErrorKind.Unauthorized)
            {
                MarkAuth(target?.Connection, failed: true);
            }

            return Finish(ex.Message, true, AuditOutcome.Error, ex.ShortResult);
        }
        catch (ToolArgumentException ex)
        {
            return Finish(ex.Message, true, AuditOutcome.Error, "ungültige Parameter");
        }
        catch (SecretUnreadableException ex)
        {
            return Finish(ex.Message, true, AuditOutcome.Error, SecretUnreadableException.ShortText);
        }
        catch (ConfigurationException ex)
        {
            return Finish($"Konfiguration ungültig – AzDO MCP Manager öffnen. {ex.Message}", true, AuditOutcome.Error, "Konfiguration ungültig");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Finish("Abgebrochen.", true, AuditOutcome.Error, "abgebrochen");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return Finish($"Interner Fehler ({ex.GetType().Name}): {ex.Message}", true, AuditOutcome.Error, "interner Fehler");
        }
    }

    private async Task<(string RequestId, (string Text, string Audit)? Result)> AskAsync(
        ToolDefinition tool, ToolArgs args, ProjectRef? target, AzdoClient? client, string clientName, long started, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var request = new ApprovalRequest
        {
            Client = clientName,
            Tool = tool.Name,
            Connection = target?.Alias,
            WorkItemId = SafeId(args),
            Title = ChangeSummary.Title(clientName, tool, args, target?.Alias),
            Summary = await ChangeSummary.BuildAsync(tool, args, client, ct).ConfigureAwait(false),
            CreatedAt = now,
            ExpiresAt = now + _manager.ApprovalTimeout,
            ProcessId = Environment.ProcessId,
        };

        TryAudit(new AuditRecord
        {
            Time = now,
            Client = clientName,
            Tool = tool.Name,
            Connection = target?.Alias,
            DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Outcome = AuditOutcome.Pending,
            Result = "wartet auf Freigabe",
            IsWrite = tool.IsWrite,
            WorkItemId = request.WorkItemId,
            RequestId = request.RequestId,
            ProcessId = Environment.ProcessId,
        });

        var outcome = await _manager.RequestApprovalAsync(request, ct).ConfigureAwait(false);
        return (request.RequestId, outcome switch
        {
            ApprovalOutcome.Approved => null,
            ApprovalOutcome.Denied => (DeniedText + ".", "abgelehnt"),
            ApprovalOutcome.TimedOut => (TimedOutText + " (nach " + _manager.ApprovalTimeout.TotalMinutes.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " min).", "abgelehnt"),
            _ => (NoManagerText + ". Den Manager starten oder das Tool unter „Tools & Rechte“ auf „Erlaubt“ stellen.", "abgelehnt – Manager nicht gestartet"),
        });
    }

    private static int? SafeId(ToolArgs args)
    {
        try
        {
            return args.Int("id");
        }
        catch (ToolArgumentException)
        {
            return null;
        }
    }

    /// <summary>Finds the configured project for a "project" argument; error text lists the configured aliases.</summary>
    private static readonly HashSet<string> WorkItemIdTools =
    [
        "get_work_item", "get_linked_work_items", "get_work_item_revisions",
        "update_work_item", "add_work_item_comment", "update_work_item_comment", "add_work_item_relation", "add_work_item_attachment",
    ];

    /// <summary>Error text when the item lies in another project; null when it belongs to the target or cannot be read.</summary>
    private async Task<string?> ForeignProjectAsync(AzdoClient client, ProjectRef target, int id, CancellationToken ct)
    {
        string? owner;
        try
        {
            var item = await client.GetAsync($"{client.CollectionUrl}/_apis/wit/workitems/{id}?fields=System.TeamProject&api-version={AzdoClient.ApiVersion}", ct).ConfigureAwait(false);
            owner = item?["fields"]?["System.TeamProject"]?.GetValue<string>();
        }
        catch (AzdoApiException ex) when (ex.Kind is AzdoErrorKind.NotFound or AzdoErrorKind.Forbidden or AzdoErrorKind.BadRequest or AzdoErrorKind.NotAvailable)
        {
            return null; // the tool itself reports "not found" / "no access"
        }

        if (owner is null || string.Equals(owner, target.Project.Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var configured = target.Connection.Projects.FirstOrDefault(p => string.Equals(p.Name, owner, StringComparison.OrdinalIgnoreCase));
        return configured is null
            ? $"Work Item {id} gehört nicht zu {target.Alias}"
            : $"Work Item {id} gehört nicht zu {target.Alias} – es liegt in {configured.Alias}";
    }

    public ProjectRef? Resolve(string? alias, out string? error)
    {
        var all = _store.AllProjects();
        if (all.Count == 0)
        {
            error = "Kein Projekt eingerichtet – im AzDO MCP Manager unter „Verbindungen & PATs“ eine Verbindung anlegen und Projekte erkennen.";
            return null;
        }

        var found = _store.FindProject(alias);
        if (found is not null)
        {
            error = null;
            return found;
        }

        var names = string.Join(", ", all.Select(p => p.Alias));
        error = string.IsNullOrWhiteSpace(alias)
            ? $"Kein Standardprojekt festgelegt. „project“ angeben: {names}."
            : $"Unbekannter Kurzname „{alias}“. Eingerichtet: {names}.";
        return null;
    }

    private void MarkAuth(Connection? connection, bool failed)
    {
        if (connection is null || (connection.LastAuthFailure is not null) == failed)
        {
            return;
        }

        try
        {
            _store.Update(connection.Id, c => c.LastAuthFailure = failed ? _time.GetUtcNow() : null);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // Status is informational; never fail a call because of it.
        }
    }

    private void TryAudit(AuditRecord record)
    {
        try
        {
            _audit.Append(record);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Protokoll nicht schreibbar: {ex.Message}");
        }
    }
}

public static class ClientNames
{
    /// <summary>Maps MCP clientInfo names to the names shown in the Manager.</summary>
    public static string Display(string? clientInfoName)
    {
        if (string.IsNullOrWhiteSpace(clientInfoName))
        {
            return "unbekannt";
        }

        var n = clientInfoName.Trim().ToLowerInvariant();
        return n switch
        {
            _ when n.Contains("claude-code") || n == "claude code" => "Claude Code",
            _ when n.Contains("claude") => "Claude Desktop",
            _ when n.Contains("opencode") => "OpenCode",
            _ when n.Contains("visual studio code") || n.Contains("vscode") || n.Contains("copilot") => "VS Code / Copilot",
            _ => clientInfoName.Trim(),
        };
    }
}
