namespace EasyCon.Core.LLM.Mcp;

/// <summary>
/// mcp.json 的根配置类型。
/// </summary>
public class McpConfig
{
    /// <summary>MCP 配置节点。</summary>
    public McpSection Mcp { get; set; } = new();
}

/// <summary>
/// MCP 服务器集合。
/// </summary>
public class McpSection
{
    /// <summary>
    /// 服务器配置字典，key 为 slug（如 "filesystem"、"github"），value 为配置。
    /// </summary>
    public Dictionary<string, McpServerConfig> Servers { get; set; } = [];
}
