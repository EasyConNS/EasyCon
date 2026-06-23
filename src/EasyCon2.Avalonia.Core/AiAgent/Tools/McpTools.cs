using EasyCon2.Avalonia.Core.Mcp;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// MCP 工具注册入口。
/// 将所有已连接 MCP 服务器的工具包装为 McpToolAdapter 并注册到 ToolRegistry，
/// 同时注册 4 个元工具（list/read_mcp_resources, list/get_mcp_prompts）。
/// </summary>
public static class McpTools
{
    /// <summary>
    /// 注册所有 MCP 工具（服务器工具 + 4 个元工具）。
    /// 在 AiAgentViewModel 构造时调用，或在 ToolsChanged 事件时重新调用。
    /// </summary>
    public static void RegisterAll(ToolRegistry registry, IMcpManager manager)
    {
        // 1. 注册每个已连接服务器的工具
        foreach (var conn in manager.Connections)
        {
            if (conn.Status != McpConnectionStatus.Connected || conn.Session is null)
                continue;

            foreach (var tool in conn.Tools)
            {
                registry.Register(new McpToolAdapter(conn, tool));
            }
        }

        // 2. 注册 4 个元工具（跨服务器 fan-out）
        registry.Register(new ListMcpResourcesTool(manager));
        registry.Register(new ReadMcpResourceTool(manager));
        registry.Register(new ListMcpPromptsTool(manager));
        registry.Register(new GetMcpPromptTool(manager));
    }
}
