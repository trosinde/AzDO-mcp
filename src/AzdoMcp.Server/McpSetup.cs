using AzdoMcp.Core;
using System.Reflection;
using AzdoMcp.Core.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AzdoMcp.Server;

/// <summary>Maps the Core tool catalogue onto the MCP SDK's low-level handlers.</summary>
internal static class McpSetup
{
    public const string ServerName = "azdo-mcp";

    public static string Version =>
        typeof(McpSetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(McpSetup).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static McpServerOptions CreateOptions(ToolExecutor executor, Func<IEnumerable<ToolDefinition>>? listTools = null)
    {
        listTools ??= executor.VisibleTools;

        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = ServerName, Title = "Azure DevOps (AzDO MCP Manager)", Version = Version },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } },
            ServerInstructions = executor.Instructions(),
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = listTools().Select(ToProtocol).ToList(),
                }),
                CallToolHandler = async (request, ct) =>
                {
                    var p = request.Params ?? throw new ArgumentException("Parameter fehlen.");
                    var client = ClientNames.Display(request.Server.ClientInfo?.Name);
                    var args = p.Arguments?.ToDictionary(kv => kv.Key, kv => kv.Value);
                    var result = await executor.ExecuteAsync(p.Name, args, client, ct).ConfigureAwait(false);
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = result.Text }],
                        IsError = result.IsError,
                    };
                },
            },
        };
    }

    public static Tool ToProtocol(ToolDefinition t) => new()
    {
        Name = t.Name,
        Description = $"{t.Description} [{t.Group.Display()}]",
        InputSchema = t.InputSchemaElement,
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = !t.IsWrite,
            DestructiveHint = t.IsWrite,
            OpenWorldHint = t.NeedsConnection,
        },
    };
}
