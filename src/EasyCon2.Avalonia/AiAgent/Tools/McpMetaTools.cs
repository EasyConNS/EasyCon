using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Mcp;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace EasyCon2.Avalonia.AiAgent.Tools;

/// <summary>
/// list_mcp_resources：列出所有已连接 MCP 服务器提供的资源。
/// </summary>
public sealed class ListMcpResourcesTool : IAiTool
{
    private readonly IMcpManager _manager;

    public ListMcpResourcesTool(IMcpManager manager) => _manager = manager;

    public string Name => "list_mcp_resources";
    public string Description => "列出所有已连接的 MCP 服务器提供的资源（文件、数据库、API 等）。返回每个资源的 URI、名称、描述和 MIME 类型。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["server"] = new() { Type = "string", Description = "可选：只列出指定服务器的资源（传 serverKey）" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var serverFilter = args.TryGetValue("server", out var s) ? s.GetString() : null;
        var connections = _manager.Connections
            .Where(c => c.Status == McpConnectionStatus.Connected)
            .Where(c => serverFilter is null || c.ServerKey == serverFilter)
            .ToList();

        if (connections.Count == 0)
            return ToolResult.Ok("当前无已连接的 MCP 服务器。");

        var allResources = new List<object>();

        foreach (var conn in connections)
        {
            if (conn.Session is null) continue;

            try
            {
                var resources = await conn.Session.ListResourcesAsync(ct);
                foreach (var r in resources)
                {
                    allResources.Add(new
                    {
                        server = conn.ServerKey,
                        uri = r.Uri,
                        name = r.Name,
                        description = r.Description ?? "",
                        mimeType = r.MimeType ?? "unknown"
                    });
                }
            }
            catch (Exception ex)
            {
                allResources.Add(new { server = conn.ServerKey, error = ex.Message });
            }
        }

        var json = JsonSerializer.Serialize(allResources, new JsonSerializerOptions { WriteIndented = true });
        return ToolResult.Ok($"发现 {allResources.Count} 个资源：\n{json}");
    }
}

/// <summary>
/// read_mcp_resource：读取指定 URI 的 MCP 资源内容。
/// </summary>
public sealed class ReadMcpResourceTool : IAiTool
{
    private readonly IMcpManager _manager;

    public ReadMcpResourceTool(IMcpManager manager) => _manager = manager;

    public string Name => "read_mcp_resource";
    public string Description => "读取指定 URI 的 MCP 资源内容。支持文本资源（直接返回）和二进制资源（图片会附加到对话中）。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["uri"] = new() { Type = "string", Description = "资源 URI（从 list_mcp_resources 获取）" },
            ["server"] = new() { Type = "string", Description = "可选：指定服务器 key（多个服务器有相同 URI 时使用）" }
        },
        Required = ["uri"]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("uri", out var uriElem) || uriElem.GetString() is not string uri)
            return ToolResult.Error("缺少必需参数: uri");

        var serverFilter = args.TryGetValue("server", out var s) ? s.GetString() : null;

        var conn = FindConnectedConnection(serverFilter);
        if (conn is null)
            return ToolResult.Error($"未找到可读取资源的已连接服务器{(serverFilter is not null ? $" '{serverFilter}'" : "")}");

        try
        {
            var result = await conn.Session!.ReadResourceAsync(uri, ct);
            if (result.Contents is null || result.Contents.Count == 0)
                return ToolResult.Ok("资源内容为空。");

            var (text, imageMessage) = ExtractContents(result, uri);
            return ShapeResult(text, imageMessage);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[读取资源失败] {ex.Message}");
        }
    }

    /// <summary>取第一个已连接、会话就绪且匹配 server 过滤的连接（谓词与原内联实现一致）。</summary>
    private McpServerConnection? FindConnectedConnection(string? serverFilter) =>
        _manager.Connections
            .FirstOrDefault(c => c.Status == McpConnectionStatus.Connected
                              && (serverFilter is null || c.ServerKey == serverFilter)
                              && c.Session is not null);

    /// <summary>拼接文本内容并提取首个二进制资源为图片消息（与原内联实现逐字一致）。</summary>
    private static (string Text, ChatMessage? ImageMessage) ExtractContents(ReadResourceResult result, string uri)
    {
        var textParts = new List<string>();
        ChatMessage? imageMessage = null;

        foreach (var content in result.Contents)
        {
            if (content is TextResourceContents textContent)
            {
                textParts.Add(textContent.Text ?? "");
            }
            else if (content is BlobResourceContents blobContent)
            {
                var blob = blobContent.Blob;
                if (blob.Length > 0)
                {
                    var mime = blobContent.MimeType ?? "image/png";
                    var base64 = Convert.ToBase64String(blob.Span);
                    imageMessage = ChatMessage.User([
                        ContentPart.FromText($"[资源 {uri}]"),
                        ContentPart.FromImageBase64(mime, base64)
                    ]);
                }
            }
        }

        return (string.Join("\n", textParts), imageMessage);
    }

    /// <summary>图片消息优先返回，否则回退纯文本（空文本文案与原实现一致）。</summary>
    private static ToolResult ShapeResult(string text, ChatMessage? imageMessage) =>
        imageMessage is not null
            ? ToolResult.Ok(string.IsNullOrWhiteSpace(text) ? "(资源为图片)" : text, imageMessage)
            : ToolResult.Ok(string.IsNullOrWhiteSpace(text) ? "(资源内容为空)" : text);
}

/// <summary>
/// list_mcp_prompts：列出所有已连接 MCP 服务器提供的提示模板。
/// </summary>
public sealed class ListMcpPromptsTool : IAiTool
{
    private readonly IMcpManager _manager;

    public ListMcpPromptsTool(IMcpManager manager) => _manager = manager;

    public string Name => "list_mcp_prompts";
    public string Description => "列出所有已连接的 MCP 服务器提供的提示模板（prompts）。返回每个 prompt 的名称、描述和参数列表。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["server"] = new() { Type = "string", Description = "可选：只列出指定服务器的 prompts（传 serverKey）" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var serverFilter = args.TryGetValue("server", out var s) ? s.GetString() : null;
        var connections = SelectConnectedConnections(serverFilter);

        if (connections.Count == 0)
            return ToolResult.Ok("当前无已连接的 MCP 服务器。");

        var allPrompts = await CollectPromptsAsync(connections, ct);

        var json = JsonSerializer.Serialize(allPrompts, new JsonSerializerOptions { WriteIndented = true });
        return ToolResult.Ok($"发现 {allPrompts.Count} 个提示模板：\n{json}");
    }

    /// <summary>取已连接且匹配 server 过滤的连接列表（谓词与原内联实现一致）。</summary>
    private List<McpServerConnection> SelectConnectedConnections(string? serverFilter) =>
        _manager.Connections
            .Where(c => c.Status == McpConnectionStatus.Connected)
            .Where(c => serverFilter is null || c.ServerKey == serverFilter)
            .ToList();

    /// <summary>逐服务器拉取 prompts；单服务器失败降级为该服务器的 error 条目（与原内联实现一致）。</summary>
    private async Task<List<object>> CollectPromptsAsync(List<McpServerConnection> connections, CancellationToken ct)
    {
        var allPrompts = new List<object>();

        foreach (var conn in connections)
        {
            if (conn.Session is null) continue;

            try
            {
                var prompts = await conn.Session.ListPromptsAsync(ct);
                foreach (var p in prompts)
                {
                    allPrompts.Add(new
                    {
                        server = conn.ServerKey,
                        name = p.Name,
                        description = p.Description ?? "",
                        arguments = p.ProtocolPrompt?.Arguments?.Select(a => new { name = a.Name, description = a.Description ?? "", required = a.Required }).ToList() ?? []
                    });
                }
            }
            catch (Exception ex)
            {
                allPrompts.Add(new { server = conn.ServerKey, error = ex.Message });
            }
        }

        return allPrompts;
    }
}

/// <summary>
/// get_mcp_prompt：获取指定 MCP 提示模板并渲染为对话消息。
/// </summary>
public sealed class GetMcpPromptTool : IAiTool
{
    private readonly IMcpManager _manager;

    public GetMcpPromptTool(IMcpManager manager) => _manager = manager;

    public string Name => "get_mcp_prompt";
    public string Description => "获取指定的 MCP 提示模板（prompt），可传入参数进行渲染。返回渲染后的对话消息列表，你可以直接基于这些内容继续对话。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["name"] = new() { Type = "string", Description = "提示模板名称（从 list_mcp_prompts 获取）" },
            ["server"] = new() { Type = "string", Description = "可选：指定服务器 key（多个服务器有相同 prompt 时使用）" },
            ["arguments"] = new() { Type = "object", Description = "可选：传递给模板的参数对象" }
        },
        Required = ["name"]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("name", out var nameElem) || nameElem.GetString() is not string promptName)
            return ToolResult.Error("缺少必需参数: name");

        var serverFilter = args.TryGetValue("server", out var s) ? s.GetString() : null;

        var conn = FindConnectedConnection(serverFilter);
        if (conn is null)
            return ToolResult.Error($"未找到可用的已连接服务器{(serverFilter is not null ? $" '{serverFilter}'" : "")}");

        var promptArgs = ParsePromptArguments(args);

        try
        {
            var result = await conn.Session!.GetPromptAsync(promptName, promptArgs, ct);
            return ToolResult.Ok(RenderPromptResult(result));
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[获取提示模板失败] {ex.Message}");
        }
    }

    /// <summary>取第一个已连接、会话就绪且匹配 server 过滤的连接（谓词与原内联实现一致）。</summary>
    private McpServerConnection? FindConnectedConnection(string? serverFilter) =>
        _manager.Connections
            .FirstOrDefault(c => c.Status == McpConnectionStatus.Connected
                              && (serverFilter is null || c.ServerKey == serverFilter)
                              && c.Session is not null);

    /// <summary>解析 arguments 参数对象；缺省或非对象时返回 null（与原内联实现一致）。</summary>
    private static IReadOnlyDictionary<string, object?>? ParsePromptArguments(Dictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("arguments", out var argsElem) || argsElem.ValueKind != JsonValueKind.Object)
            return null;

        return argsElem.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Deserialize<object>());
    }

    /// <summary>把 GetPrompt 结果渲染为可读文本（格式与原内联实现逐字一致）。</summary>
    private static string RenderPromptResult(GetPromptResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Description))
            parts.Add($"[{result.Description}]");

        if (result.Messages is not null)
        {
            foreach (var msg in result.Messages)
            {
                var role = msg.Role.ToString().ToLowerInvariant();
                var text = msg.Content switch
                {
                    TextContentBlock tcb => tcb.Text,
                    _ => $"[{msg.Content?.GetType().Name ?? "unknown"}]"
                };
                parts.Add($"{role}: {text}");
            }
        }

        return string.Join("\n\n", parts);
    }
}