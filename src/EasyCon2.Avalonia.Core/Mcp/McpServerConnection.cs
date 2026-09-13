using EasyCon.Core.LLM.Mcp;
using ModelContextProtocol.Client;

namespace EasyCon2.Avalonia.Core.Mcp;

/// <summary>
/// MCP 服务器连接状态。
/// </summary>
public enum McpConnectionStatus
{
    /// <summary>正在连接（子进程启动中）。</summary>
    Connecting,

    /// <summary>已连接，可调用工具/资源/提示。</summary>
    Connected,

    /// <summary>连接失败（子进程崩溃或初始化超时）。</summary>
    Failed,

    /// <summary>已断开（用户禁用或配置刷新时主动关闭）。</summary>
    Disconnected
}

/// <summary>
/// 单个 MCP 服务器的连接状态持有者。
/// </summary>
public sealed class McpServerConnection : IAsyncDisposable
{
    /// <summary>配置字典中的 key（如 "filesystem"）。</summary>
    public string ServerKey { get; }

    /// <summary>服务器配置。</summary>
    public McpServerConfig Config { get; }

    /// <summary>当前连接状态。</summary>
    public McpConnectionStatus Status { get; set; } = McpConnectionStatus.Connecting;

    /// <summary>连接失败时的错误信息。</summary>
    public string? Error { get; set; }

    /// <summary>服务器提供的工具列表（连接成功后填充）。</summary>
    public IList<McpClientTool> Tools { get; set; } = [];

    /// <summary>内部会话（连接成功后非空）。</summary>
    internal IMcpSession? Session { get; set; }

    public McpServerConnection(string serverKey, McpServerConfig config)
    {
        ServerKey = serverKey;
        Config = config;
    }

    public async ValueTask DisposeAsync()
    {
        if (Session is not null)
        {
            await Session.DisposeAsync();
            Session = null;
        }
        Status = McpConnectionStatus.Disconnected;
    }
}