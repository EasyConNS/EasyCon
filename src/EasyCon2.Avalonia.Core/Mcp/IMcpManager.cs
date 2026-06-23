namespace EasyCon2.Avalonia.Core.Mcp;

/// <summary>
/// MCP 服务器管理器，负责连接生命周期和工具集变更事件。
/// </summary>
public interface IMcpManager : IDisposable
{
    /// <summary>当前所有连接（含 Connecting/Connected/Failed/Disconnected 状态）。</summary>
    IReadOnlyList<McpServerConnection> Connections { get; }

    /// <summary>连接/断开/重连后工具集发生变化时触发。</summary>
    event Action? ToolsChanged;

    /// <summary>启动时连接所有 enabled 服务器（单台失败不阻塞其余）。</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>配置变更后差量重连（移除/禁用 → Dispose，新增 → 连接）。</summary>
    Task RefreshAsync(CancellationToken ct);

    /// <summary>按 serverKey 查找连接（返回 null 表示不存在）。</summary>
    McpServerConnection? GetConnection(string serverKey);
}
