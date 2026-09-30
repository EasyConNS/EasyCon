using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Mcp;

/// <summary>
/// MCP 服务器传输协议类型。落盘为小写字符串（"stdio"），读取兼容任意大小写与数字。
/// </summary>
public enum McpTransport
{
    /// <summary>标准输入/输出（子进程型 MCP 服务器）。</summary>
    Stdio
    // 预留：Sse, Http — 后续扩展远程传输时使用
}

/// <summary>McpTransport 与小写字符串的互转；读取兼容任意大小写与历史文件中的数字。</summary>
public sealed class McpTransportConverter : JsonConverter<McpTransport>
{
    public override McpTransport Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            reader.Skip();
            return McpTransport.Stdio;
        }

        var text = reader.GetString();
        return Enum.TryParse<McpTransport>(text, ignoreCase: true, out var value)
            ? value
            : McpTransport.Stdio;
    }

    public override void Write(Utf8JsonWriter writer, McpTransport value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

/// <summary>
/// 单个 MCP 服务器的连接配置，对齐 Claude Desktop 的 mcpServers 约定。
/// </summary>
public class McpServerConfig
{
    /// <summary>显示名称（UI 友好）。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>传输协议。</summary>
    [JsonPropertyName("transport")]
    [JsonConverter(typeof(McpTransportConverter))]
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>stdio: 可执行文件路径（如 npx、node、python 等）。</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>stdio: 命令行参数。</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    /// <summary>stdio: 环境变量（追加到子进程环境）。</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>是否启用（false 时不启动子进程）。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
