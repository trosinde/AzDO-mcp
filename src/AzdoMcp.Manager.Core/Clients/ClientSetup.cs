using System.Diagnostics;
using System.Text.Json.Nodes;
using AzdoMcp.Manager.Core.Services;

namespace AzdoMcp.Manager.Core.Clients;

public enum ClientKind
{
    ClaudeCode,
    ClaudeDesktop,
    OpenCode,
    VsCode,
}

public sealed record ClientState(ClientKind Kind, string Name, bool IsConfigured, string? ConfigPath, string? Problem = null, string? Warning = null);

public sealed record WriteResult(string Path, string? BackupPath);

/// <summary>
/// Registers azdo-mcp.exe in MCP clients. Writes only the "azure-devops" entry, keeps
/// everything else, backs the file up first and never writes a PAT or URL.
/// </summary>
public sealed class ClientSetupService(ManagerEnvironment env, TimeProvider? time = null)
{
    public const string ServerName = "azure-devops";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public static string DisplayName(ClientKind kind) => kind switch
    {
        ClientKind.ClaudeCode => "Claude Code",
        ClientKind.ClaudeDesktop => "Claude Desktop",
        ClientKind.OpenCode => "OpenCode",
        ClientKind.VsCode => "VS Code / GitHub Copilot",
        _ => kind.ToString(),
    };

    public string ExePath => env.ServerExePath;

    public string ClaudeDesktopConfig => Path.Combine(env.AppData, "Claude", "claude_desktop_config.json");

    public string OpenCodeConfig
    {
        get
        {
            var dir = Path.Combine(env.UserProfile, ".config", "opencode");
            var json = Path.Combine(dir, "opencode.json");
            return File.Exists(json) && !File.Exists(Path.Combine(dir, "opencode.jsonc")) ? json : Path.Combine(dir, "opencode.jsonc");
        }
    }

    public string VsCodeUserConfig => Path.Combine(env.AppData, "Code", "User", "mcp.json");

    public string ClaudeCodeConfig => Path.Combine(env.UserProfile, ".claude.json");

    public static string VsCodeWorkspaceConfig(string folder) => Path.Combine(folder, ".vscode", "mcp.json");

    /// <summary>Command for Claude Code (user scope).</summary>
    public string ClaudeCodeCommand => $"claude mcp add --scope user {ServerName} -- \"{ExePath}\" --stdio";

    public IReadOnlyList<ClientState> DetectAll() =>
        [Detect(ClientKind.ClaudeCode), Detect(ClientKind.ClaudeDesktop), Detect(ClientKind.OpenCode), Detect(ClientKind.VsCode)];

    public ClientState Detect(ClientKind kind)
    {
        var (path, entryPath) = kind switch
        {
            ClientKind.ClaudeCode => (ClaudeCodeConfig, new[] { "mcpServers", ServerName }),
            ClientKind.ClaudeDesktop => (ClaudeDesktopConfig, new[] { "mcpServers", ServerName }),
            ClientKind.OpenCode => (OpenCodeConfig, new[] { "mcp", ServerName }),
            _ => (VsCodeUserConfig, new[] { "servers", ServerName }),
        };

        if (!File.Exists(path))
        {
            return new(kind, DisplayName(kind), false, path);
        }

        try
        {
            JsonNode? node = JsoncEditor.Parse(File.ReadAllText(path));
            foreach (var key in entryPath)
            {
                node = node?[key];
            }

            var configured = node is not null && node.ToJsonString().Contains("azdo-mcp", StringComparison.OrdinalIgnoreCase);
            string? warning = null;
            if (kind == ClientKind.OpenCode && JsoncEditor.Parse(File.ReadAllText(path))?["mcp"]?["servers"] is not null)
            {
                warning = OpenCodeWrapperWarning;
            }

            return new(kind, DisplayName(kind), configured, path, Warning: warning);
        }
        catch (Exception ex) when (ex is ClientConfigException or IOException or InvalidOperationException)
        {
            return new(kind, DisplayName(kind), false, path, ex.Message);
        }
    }

    public const string OpenCodeWrapperWarning = "„servers“ unter „mcp“ ist kein gültiger Eintrag – Prompt nutzen oder Datei korrigieren";

    public JsonObject ClaudeDesktopEntry() => new() { ["command"] = ExePath, ["args"] = new JsonArray("--stdio") };

    public JsonObject OpenCodeEntry() => new() { ["type"] = "local", ["command"] = new JsonArray(ExePath, "--stdio"), ["enabled"] = true };

    public JsonObject VsCodeEntry() => new() { ["type"] = "stdio", ["command"] = ExePath, ["args"] = new JsonArray("--stdio") };

    public WriteResult WriteClaudeDesktop() => WriteEntry(ClaudeDesktopConfig, ["mcpServers", ServerName], ClaudeDesktopEntry());

    public WriteResult WriteOpenCode(string? path = null) => WriteEntry(path ?? OpenCodeConfig, ["mcp", ServerName], OpenCodeEntry());

    public WriteResult WriteVsCode(string? workspaceFolder = null) =>
        WriteEntry(workspaceFolder is null ? VsCodeUserConfig : VsCodeWorkspaceConfig(workspaceFolder), ["servers", ServerName], VsCodeEntry());

    private const string FileRules = "Lege vorher eine Sicherungskopie der Datei an, ändere sonst nichts (auch keine Tokens, Header oder anderen Server) und achte auf gültiges JSON.";

    /// <summary>
    /// Prompt to paste into an AI assistant that sets up the server itself. Built from the same entry the
    /// one-click write uses; contains no PAT, collection URL or project name.
    /// </summary>
    public string Prompt(ClientKind kind)
    {
        static string Entry(JsonObject o) => $"\"{ServerName}\": {o.ToJsonString()}";
        return kind switch
        {
            ClientKind.ClaudeCode =>
                $"Richte den MCP-Server „{ServerName}“ für mich ein: Führe {ClaudeCodeCommand} aus. Gibt es den Eintrag schon, " +
                $"entferne ihn vorher mit claude mcp remove --scope user {ServerName}. Prüfe danach mit claude mcp list, dass „{ServerName}“ verbunden ist. " +
                "Trage keine Tokens oder PATs ein – die verwaltet der AzDO MCP Manager.",
            ClientKind.ClaudeDesktop =>
                $"Bearbeite die Datei {ClaudeDesktopConfig}: Lege unter „mcpServers“ den Eintrag {Entry(ClaudeDesktopEntry())} an oder ersetze ihn. " +
                FileRules + " Danach Claude Desktop beenden und neu starten.",
            ClientKind.OpenCode =>
                $"Bearbeite die Datei {OpenCodeConfig}: Unter „mcp“ muss direkt der Eintrag {Entry(OpenCodeEntry())} stehen – anlegen oder ersetzen. " +
                "Server stehen direkt unter „mcp“: Gibt es dort einen Zwischenschlüssel „servers“, verschiebe dessen Einträge unverändert eine Ebene nach oben und entferne „servers“. " +
                FileRules + " Danach OpenCode neu starten.",
            _ =>
                $"Bearbeite meine VS-Code-Benutzerdatei {VsCodeUserConfig}: Lege unter „servers“ den Eintrag {Entry(VsCodeEntry())} an oder ersetze ihn. " +
                FileRules + $" Danach in VS Code „MCP: List Servers“ öffnen und „{ServerName}“ starten.",
        };
    }

    /// <summary>Who the prompt is for.</summary>
    public static string PromptTarget(ClientKind kind) => kind switch
    {
        ClientKind.ClaudeCode => "In Claude Code einfügen",
        ClientKind.ClaudeDesktop => "für einen Assistenten mit Dateizugriff (z. B. Claude Code)",
        ClientKind.OpenCode => "In OpenCode einfügen",
        _ => "Im Agent-Modus von GitHub Copilot einfügen",
    };

    /// <summary>Question to check the setup after restarting the client.</summary>
    public static string TestQuestion(string? kurzname) => kurzname is null
        ? "Testfrage nach dem Neustart: „Welche Azure-DevOps-Projekte kennst du?“"
        : $"Testfrage nach dem Neustart: „Welche Azure-DevOps-Projekte kennst du?“ oder „Zeig die offenen Bugs in {kurzname}“";

    /// <summary>Adds or replaces the entry; backs up an existing file; refuses invalid JSON.</summary>
    public WriteResult WriteEntry(string path, IReadOnlyList<string> entryPath, JsonNode entry)
    {
        var exists = File.Exists(path);
        var original = exists ? File.ReadAllText(path) : "";
        var updated = JsoncEditor.SetProperty(original, entryPath, entry);

        string? backup = null;
        if (exists)
        {
            backup = $"{path}.azdo-mcp-backup-{_time.GetLocalNow():yyyyMMddHHmmss}";
            File.Copy(path, backup, overwrite: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".azdo-mcp-tmp";
        File.WriteAllText(tmp, updated);
        File.Move(tmp, path, overwrite: true);
        return new WriteResult(path, backup);
    }

    /// <summary>Finds the claude CLI on PATH (claude.exe or claude.cmd).</summary>
    public static string? FindClaudeCli(string? pathVariable = null)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat" })
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Runs "claude mcp add …". Returns (success, output).</summary>
    public async Task<(bool Ok, string Output)> RunClaudeCodeAsync(CancellationToken ct = default)
    {
        var cli = FindClaudeCli();
        if (cli is null)
        {
            return (false, "Claude-Code-CLI („claude“) nicht gefunden. Befehl kopieren und in einem Terminal ausführen.");
        }

        var psi = new ProcessStartInfo(cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "mcp", "add", "--scope", "user", ServerName, "--", ExePath, "--stdio" })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("claude konnte nicht gestartet werden.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var output = ((await stdout) + (await stderr)).Trim();
        return (p.ExitCode == 0, output);
    }
}
