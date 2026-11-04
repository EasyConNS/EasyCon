using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Mcp;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using System.Text;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// MCP Server 导出与 HTTP 传输：IAiTool 桥接、危险工具过滤、loopback /mcp 协议响应。
/// </summary>
[TestFixture]
public class McpServerHostTests
{
    private class EchoTool : IAiTool
    {
        public string Name => "echo_test";
        public string Description => "回显输入（测试）";
        public JsonSchema Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["text"] = new JsonSchemaProperty { Type = "string", Description = "要回显的文本" }
            },
            Required = ["text"]
        };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok(args.TryGetValue("text", out var t) ? t.GetString() ?? "" : ""));
    }

    private class DangerousTool : IAiTool
    {
        public string Name => "danger_test";
        public string Description => "危险工具（测试）";
        public JsonSchema Parameters => new() { Type = "object", Properties = new() };
        public bool RequiresConfirmation => true;
        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok("should not run"));
    }

    private static ToolRegistry CreateRegistry()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        registry.Register(new DangerousTool());
        return registry;
    }

    [Test]
    public async Task ExportTools_FiltersDangerousTools()
    {
        var tools = McpServerHost.ExportTools(CreateRegistry());
        var names = tools.Select(t => t.ProtocolTool.Name).ToList();
        Assert.That(names, Does.Contain("echo_test"));
        Assert.That(names, Does.Not.Contain("danger_test"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task HttpServer_RespondsToInitializeAndToolsListAndCall()
    {
        var port = 22000 + Random.Shared.Next(2000);
        await using var server = await McpServerHost.StartHttpAsync(
            CreateRegistry(), "easycon-test", "1.0", port, CancellationToken.None);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/mcp") };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

        // initialize
        var initBody = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "test", version = "0" }
            }
        });
        var init = await http.PostAsync("", new StringContent(initBody, Encoding.UTF8, "application/json"));
        Assert.That((int)init.StatusCode, Is.EqualTo(200));
        var initJson = await init.Content.ReadAsStringAsync();
        Assert.That(initJson, Does.Contain("easycon-test"));

        // tools/list
        var listBody = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var list = await http.PostAsync("", new StringContent(listBody, Encoding.UTF8, "application/json"));
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.That(listJson, Does.Contain("echo_test"));
        Assert.That(listJson, Does.Not.Contain("danger_test"));

        // tools/call
        var callBody = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "echo_test", arguments = new { text = "hello-mcp" } }
        });
        var call = await http.PostAsync("", new StringContent(callBody, Encoding.UTF8, "application/json"));
        var callJson = await call.Content.ReadAsStringAsync();
        Assert.That(callJson, Does.Contain("hello-mcp"));
    }
}