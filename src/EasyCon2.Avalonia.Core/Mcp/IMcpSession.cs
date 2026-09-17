using EasyCon.Core.LLM.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EasyCon2.Avalonia.Core.Mcp;

/// <summary>
/// MCP 客户端会话抽象，用于测试缝（包装 SDK 的 McpClient）。
/// 生产实现 <see cref="McpSessionFactory"/> 调用真实的 SDK API；
/// 测试实现可返回预设的工具列表和调用结果。
/// </summary>
public interface IMcpSession : IAsyncDisposable
{
    /// <summary>列出服务器提供的所有工具。</summary>
    ValueTask<IList<McpClientTool>> ListToolsAsync(CancellationToken ct);

    /// <summary>调用指定工具并返回结果。</summary>
    ValueTask<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> args, CancellationToken ct);

    /// <summary>列出服务器提供的所有资源。</summary>
    ValueTask<IList<McpClientResource>> ListResourcesAsync(CancellationToken ct);

    /// <summary>读取指定 URI 的资源。</summary>
    ValueTask<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken ct);

    /// <summary>列出服务器提供的所有提示模板。</summary>
    ValueTask<IList<McpClientPrompt>> ListPromptsAsync(CancellationToken ct);

    /// <summary>获取指定提示模板（可带参数）。</summary>
    ValueTask<GetPromptResult> GetPromptAsync(string promptName, IReadOnlyDictionary<string, object?>? args, CancellationToken ct);
}

/// <summary>
/// MCP 会话工厂，生产实现使用 SDK 的 <c>McpClient.CreateAsync</c>；
/// 测试实现可返回预设的 <see cref="IMcpSession"/>。
/// </summary>
public interface IMcpSessionFactory
{
    Task<IMcpSession> CreateAsync(
        McpTransport transport,
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        CancellationToken ct);
}