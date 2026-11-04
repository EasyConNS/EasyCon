using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Mcp;

/// <summary>
/// mcp.json 的根配置类型。JsonPropertyName 钉死小写 schema：
/// 序列化选项无命名策略，不钉的话落盘会被写成 PascalCase。
/// </summary>
public class McpConfig
{
    /// <summary>MCP 配置节点。</summary>
    [JsonPropertyName("mcp")]
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
    [JsonPropertyName("servers")]
    public Dictionary<string, McpServerConfig> Servers { get; set; } = [];
}