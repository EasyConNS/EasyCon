namespace EasyCon.Core.LLM.Mcp;

/// <summary>
/// MCP 服务器传输协议类型。
/// </summary>
public enum McpTransport
{
    /// <summary>标准输入/输出（子进程型 MCP 服务器）。</summary>
    Stdio
    // 预留：Sse, Http — 后续扩展远程传输时使用
}

/// <summary>
/// 单个 MCP 服务器的连接配置，对齐 Claude Desktop 的 mcpServers 约定。
/// </summary>
public class McpServerConfig
{
    /// <summary>显示名称（UI 友好）。</summary>
    public string Name { get; set; } = "";

    /// <summary>传输协议。</summary>
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>stdio: 可执行文件路径（如 npx、node、python 等）。</summary>
    public string Command { get; set; } = "";

    /// <summary>stdio: 命令行参数。</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>stdio: 环境变量（追加到子进程环境）。</summary>
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>是否启用（false 时不启动子进程）。</summary>
    public bool Enabled { get; set; } = true;
}
