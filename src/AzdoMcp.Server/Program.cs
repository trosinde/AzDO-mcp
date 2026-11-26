using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Core.Tools;
using AzdoMcp.Server;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

// stderr is read by MCP clients as UTF-8; stdout is owned by the MCP transport.
Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });

if (args.Contains("--version"))
{
    Console.Error.WriteLine($"{McpSetup.ServerName} {McpSetup.Version}");
    return 0;
}

if (args.FirstOrDefault() == "discover")
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("PAT-Speicherung benötigt Windows (DPAPI).");
        return 3;
    }

    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    var discoverStore = new ConnectionStore(AppPaths.Default(), SecretProtectors.CreateDefault());
    return await DiscoverCommand.RunAsync(args, discoverStore, new AzdoClientFactory(), Console.Out, Console.Error,
        permissions: new AzdoMcp.Core.Permissions.PermissionStore(AppPaths.Default()).Get());
}

if (!args.Contains("--stdio"))
{
    Console.Error.WriteLine($"""
        {McpSetup.ServerName} {McpSetup.Version} – MCP-Server für Azure DevOps

        Aufruf:  azdo-mcp.exe --stdio
                 azdo-mcp.exe discover <verbindung> [--apply]

        Der Server wird von einem MCP-Client (Claude Code, Claude Desktop, OpenCode,
        VS Code) gestartet. Verbindungen und PATs verwaltet der AzDO MCP Manager.
        """);
    return 2;
}

var paths = AppPaths.Default();
ISecretProtector protector;
try
{
    protector = SecretProtectors.CreateDefault();
}
catch (PlatformNotSupportedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

var store = new ConnectionStore(paths, protector);
var audit = new AuditLog(paths);
try
{
    paths.EnsureCreated();
    audit.Cleanup();
    if (store.AllProjects().Count == 0)
    {
        Console.Error.WriteLine("Hinweis: kein Projekt eingerichtet – AzDO MCP Manager öffnen.");
    }
}
catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException)
{
    // Keep stdout clean; the problem is reported again as a tool error on the first call.
    Console.Error.WriteLine($"Warnung: {ex.Message}");
}

await using var manager = new ManagerChannel(new NamedPipeClientTransport(PipeNames.ForCurrentUser()), version: McpSetup.Version);
manager.Start();

var executor = new ToolExecutor(store, new AzdoClientFactory(), audit, new PermissionStore(paths), manager);
var options = McpSetup.CreateOptions(executor);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var transport = new StdioServerTransport(McpSetup.ServerName, NullLoggerFactory.Instance);
await using var server = McpServer.Create(transport, options, NullLoggerFactory.Instance);
using var monitor = ToolListMonitor.ForServer(executor, paths, server);
manager.ReloadRequested += monitor.Trigger;
// Before an update the Manager asks running servers to exit so azdo-mcp.exe can be replaced;
// the MCP client starts the new version on its next use.
manager.ExitRequested += () =>
{
    Console.Error.WriteLine("AzDO MCP Manager installiert ein Update – Server wird beendet.");
    cts.Cancel();
};
try
{
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
}

return 0;
