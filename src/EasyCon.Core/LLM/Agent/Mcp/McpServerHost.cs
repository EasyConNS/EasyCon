using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.Logging;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent.Mcp;

/// <summary>
/// 把 ToolRegistry（IAiTool）导出为 MCP 工具并承载 MCP Server。
/// GUI 用 HTTP 传输（loopback /mcp），CLI 用 stdio 传输——同一套工具，能力完全对等。
/// 危险工具（RequiresConfirmation）在导出时过滤：MCP 调用方无法参与 GUI 的人工确认，fail-closed。
/// </summary>
public static class McpServerHost
{
    /// <summary>把注册表中可导出的工具包装为 McpServerTool 列表。</summary>
    public static List<McpServerTool> ExportTools(ToolRegistry registry)
    {
        var tools = new List<McpServerTool>();
        foreach (var (_, tool) in registry.Snapshot())
        {
            if (tool.RequiresConfirmation)
                continue;
            tools.Add(McpServerTool.Create(new AiToolAIFunction(tool)));
        }
        return tools;
    }

    private static McpServerOptions BuildOptions(string name, string version, List<McpServerTool> tools) => new()
    {
        ServerInfo = new Implementation { Name = name, Version = version },
        ToolCollection = [.. tools]
    };

    /// <summary>启动 stdio 传输的 MCP Server 并开始运行（阻塞直到 stdin 关闭）。</summary>
    public static async Task RunStdioAsync(ToolRegistry registry, string name, string version, CancellationToken ct)
    {
        var options = BuildOptions(name, version, ExportTools(registry));
        var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync(ct);
    }

    /// <summary>
    /// 启动 loopback HTTP 传输的 MCP Server（streamable HTTP，无状态模式）。
    /// 返回停止句柄；进程退出时由调用方释放。
    /// </summary>
    public static async Task<McpHttpServer> StartHttpAsync(
        ToolRegistry registry, string name, string version, int port, CancellationToken ct)
    {
        var tools = ExportTools(registry);
        var http = new McpHttpServer(name, version, tools, port);
        await http.StartAsync(ct);
        return http;
    }
}

/// <summary>loopback HTTP MCP Server：HttpListener + StreamableHttpServerTransport（无状态）。</summary>
public sealed class McpHttpServer : IAsyncDisposable
{
    private readonly string _name;
    private readonly string _version;
    private readonly List<McpServerTool> _tools;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    internal McpHttpServer(string name, string version, List<McpServerTool> tools, int port)
    {
        _name = name;
        _version = version;
        _tools = tools;
        _port = port;
    }

    /// <summary>监听地址。</summary>
    public string Url => $"http://127.0.0.1:{_port}/mcp";

    /// <summary>启动监听循环（后台）。</summary>
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/mcp/");
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        CoreLog.Info($"[McpServer] HTTP MCP 已监听 {Url}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                CoreLog.Info($"[McpServer] 请求接收失败: {ex.Message}");
                continue;
            }
            _ = HandleAsync(ctx, ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            switch (ctx.Request.HttpMethod)
            {
                case "POST":
                    await HandlePostAsync(ctx, ct);
                    break;
                case "GET":
                    await HandleGetAsync(ctx, ct);
                    break;
                case "DELETE":
                    WriteEmpty(ctx, 200);
                    break;
                default:
                    WriteEmpty(ctx, 405);
                    break;
            }
        }
        catch (Exception ex)
        {
            CoreLog.Info($"[McpServer] 请求处理失败: {ex.Message}");
            try { WriteEmpty(ctx, 500); } catch { /* 连接已断开 */ }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private async Task HandlePostAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        JsonRpcMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions);
        }
        catch (JsonException)
        {
            message = null;
        }
        if (message is null)
        {
            WriteEmpty(ctx, 400);
            return;
        }

        // 无状态模式：每个 POST 一个独立 transport，会话不开（简单且无泄漏面）
        await using var transport = new StreamableHttpServerTransport { Stateless = true };
        var options = BuildOptions();
        var server = McpServer.Create(transport, options);
        _ = server.RunAsync(CancellationToken.None);

        await using var responseStream = new MemoryStream();
        var hasResponse = await transport.HandlePostRequestAsync(message, responseStream, ct);
        if (hasResponse && responseStream.Length > 0)
        {
            var bytes = responseStream.ToArray();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        }
        else
        {
            // 通知类消息无响应体：202 Accepted
            WriteEmpty(ctx, 202);
        }
    }

    private async Task HandleGetAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        // 无状态模式不支持 GET SSE 流：明确拒绝并提示用 POST
        ctx.Response.StatusCode = 405;
        ctx.Response.ContentType = "text/plain";
        var bytes = "text/event-stream not supported in stateless mode; use POST"u8.ToArray();
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
    }

    private McpServerOptions BuildOptions() => new()
    {
        ServerInfo = new Implementation { Name = _name, Version = _version },
        ToolCollection = [.. _tools]
    };

    private static void WriteEmpty(HttpListenerContext ctx, int status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentLength64 = 0;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_listener is { IsListening: true })
            _listener.Stop();
        _listener?.Close();
        await Task.CompletedTask;
    }
}

/// <summary>
/// IAiTool → AIFunction 桥接：schema 用工具自带 JSON Schema 原样透传，
/// 调用结果以纯文本回传（多模态附加内容在外部 agent 场景降级为提示文本）。
/// </summary>
public sealed class AiToolAIFunction : AIFunction
{
    private readonly IAiTool _tool;

    /// <summary>包装一个 IAiTool。</summary>
    public AiToolAIFunction(IAiTool tool) => _tool = tool;

    /// <inheritdoc />
    public override string Name => _tool.Name;

    /// <inheritdoc />
    public override string Description => _tool.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema =>
        JsonSerializer.SerializeToElement(_tool.Parameters);

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in arguments)
        {
            if (value is JsonElement el)
                args[key] = el.Clone();
            else if (value is not null)
                args[key] = JsonSerializer.SerializeToElement(value);
        }

        var result = await _tool.ExecuteAsync(args, cancellationToken);
        var text = result.Status == ToolResultStatus.Success
            ? result.Content
            : $"{result.Content}\n[状态] {result.Status}";
        return result.AttachedMessage is not null
            ? text + "\n[提示] 本工具还产生了图片附加内容，HTTP MCP 通道不支持图片，请改用 GUI 内置 Agent 查看。"
            : text;
    }
}