using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// 将 MCP 服务器的单个工具适配为 IAiTool。
/// 工具名格式: {serverKey}__{toolName}，确保跨服务器/内置工具去重。
/// </summary>
public sealed class McpToolAdapter : IAiTool
{
    private readonly McpServerConnection _connection;
    private readonly McpClientTool _tool;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonSchema _parameters;

    /// <summary>OpenAI 工具名长度上限（function name ≤ 64 chars）。</summary>
    private const int MaxToolNameLength = 64;

    public McpToolAdapter(McpServerConnection connection, McpClientTool tool)
    {
        _connection = connection;
        _tool = tool;

        // 构造唯一名称：mcp__serverKey__toolName
        var rawName = $"mcp__{connection.ServerKey}__{tool.Name}";
        _name = rawName.Length <= MaxToolNameLength
            ? rawName
            : rawName[..MaxToolNameLength]; // 截断兜底

        _description = string.IsNullOrWhiteSpace(tool.Description)
            ? $"MCP tool from {connection.ServerKey}"
            : tool.Description;

        // 透传 MCP 的 inputSchema（可能含嵌套结构）
        _parameters = JsonSchema.FromRaw(tool.ProtocolTool.InputSchema);
    }

    public string Name => _name;
    public string Description => _description;
    public JsonSchema Parameters => _parameters;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (_connection.Session is null)
            return ToolResult.Error($"[错误] MCP 服务器 '{_connection.ServerKey}' 未连接");

        try
        {
            // 将 JsonElement 参数转换为 object? 字典（SDK 入参要求）
            var dict = args.ToDictionary(
                kv => kv.Key,
                kv => (object?)kv.Value.Deserialize<object>());

            var result = await _connection.Session.CallToolAsync(_tool.Name, dict, ct);

            if (result.IsError == true)
            {
                var errorText = JoinTextContent(result.Content);
                return ToolResult.Error(string.IsNullOrWhiteSpace(errorText) ? "MCP 工具返回错误" : errorText);
            }

            var text = JoinTextContent(result.Content);
            var image = TryBuildImageMessage(result.Content);

            return image is null
                ? ToolResult.Ok(string.IsNullOrWhiteSpace(text) ? "(无输出)" : text)
                : ToolResult.Ok(text, image);
        }
        catch (OperationCanceledException)
        {
            throw; // 让上层处理超时/取消
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[MCP 工具调用异常] {ex.Message}");
        }
    }

    /// <summary>拼接所有 TextContentBlock 的文本。</summary>
    private static string JoinTextContent(IList<ContentBlock>? content)
    {
        if (content is null || content.Count == 0)
            return "";

        var parts = content
            .OfType<TextContentBlock>()
            .Select(b => b.Text)
            .Where(s => !string.IsNullOrEmpty(s));

        return string.Join("\n", parts);
    }

    /// <summary>从结果中提取第一个 ImageContentBlock，构建多模态 ChatMessage（与 GetFrameTool 一致）。</summary>
    private static ChatMessage? TryBuildImageMessage(IList<ContentBlock>? content)
    {
        if (content is null) return null;

        var imageBlock = content.OfType<ImageContentBlock>().FirstOrDefault();
        if (imageBlock is null) return null;

        // ImageContentBlock.Data 是 ReadOnlyMemory<byte>，需转换为 base64 字符串
        var data = imageBlock.Data;
        if (data.Length == 0) return null;

        var base64 = Convert.ToBase64String(data.Span);
        var mimeType = imageBlock.MimeType ?? "image/png";

        // 使用 user 角色消息注入图片（与 GetFrameTool 一致）
        var imageMessage = ChatMessage.User([
            ContentPart.FromText("[MCP 返回图片]"),
            ContentPart.FromImageBase64(mimeType, base64)
        ]);

        return imageMessage;
    }
}
