using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using AzdoMcp.Server;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace AzdoMcp.Tests;

/// <summary>Talks raw JSON-RPC to the server over in-memory streams, like a client over stdio.</summary>
public sealed class ServerProtocolTests : IAsyncDisposable
{
    private readonly Harness _h = new("default", "SHOP");
    private readonly Pipe _toServer = new();
    private readonly Pipe _fromServer = new();
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
    private readonly McpServer _server;
    private readonly Task _run;
    private readonly StreamReader _reader;
    private int _id;

    public ServerProtocolTests()
    {
        var transport = new StreamServerTransport(_toServer.Reader.AsStream(), _fromServer.Writer.AsStream(), McpSetup.ServerName, NullLoggerFactory.Instance);
        _server = McpServer.Create(transport, McpSetup.CreateOptions(_h.Executor), NullLoggerFactory.Instance);
        _run = _server.RunAsync(_cts.Token);
        _reader = new StreamReader(_fromServer.Reader.AsStream(), Encoding.UTF8);
    }

    private async Task Send(JsonObject message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
        await _toServer.Writer.WriteAsync(bytes, _cts.Token);
    }

    private async Task<JsonNode> Request(string method, JsonObject? @params = null)
    {
        var id = ++_id;
        await Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params ?? new JsonObject() });
        while (true)
        {
            var line = await _reader.ReadLineAsync(_cts.Token) ?? throw new EndOfStreamException();
            var msg = JsonNode.Parse(line)!; // every stdout line must be JSON-RPC
            Assert.Equal("2.0", msg["jsonrpc"]!.GetValue<string>());
            if (msg["id"]?.GetValue<int>() == id)
            {
                return msg;
            }
        }
    }

    private async Task Initialize()
    {
        var init = await Request("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "claude-code", ["version"] = "1.0" },
        });
        Assert.Equal("azdo-mcp", init["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(init["result"]!["capabilities"]!["tools"]);
        var instructions = init["result"]!["instructions"]!.GetValue<string>();
        Assert.Contains("SHOP = SHOP (SHOP)", instructions);
        Assert.Contains("ist dieses Azure-DevOps-Projekt gemeint", instructions);
        await Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
    }

    [Fact]
    public async Task Initialize_and_list_tools()
    {
        await Initialize();
        var list = await Request("tools/list");
        var tools = list["result"]!["tools"]!.AsArray();
        Assert.Equal(31, tools.Count);
        var wi = tools.Single(t => t!["name"]!.GetValue<string>() == "get_work_item")!;
        Assert.Equal("object", wi["inputSchema"]!["type"]!.GetValue<string>());
        Assert.NotNull(wi["inputSchema"]!["properties"]!["project"]);
    }

    [Fact]
    public async Task Call_tool_uses_client_name_from_initialize()
    {
        await Initialize();
        var call = await Request("tools/call", new JsonObject { ["name"] = "list_projects", ["arguments"] = new JsonObject() });
        Assert.NotEqual(true, call["result"]!["isError"]?.GetValue<bool>());
        Assert.Contains("SHOP", call["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("Claude Code", _h.Records().Single().Client);
    }

    [Fact]
    public async Task Tool_errors_are_results_not_protocol_errors()
    {
        await Initialize();
        var call = await Request("tools/call", new JsonObject { ["name"] = "get_work_item", ["arguments"] = new JsonObject { ["id"] = 1, ["project"] = "NOPE" } });
        Assert.Null(call["error"]);
        Assert.True(call["result"]!["isError"]!.GetValue<bool>());

        var again = await Request("tools/call", new JsonObject { ["name"] = "list_projects", ["arguments"] = new JsonObject() });
        Assert.NotNull(again["result"]);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _toServer.Writer.Complete();
        try
        {
            await _run;
        }
        catch (OperationCanceledException)
        {
        }

        await _server.DisposeAsync();
        _h.Dispose();
        _cts.Dispose();
    }
}
