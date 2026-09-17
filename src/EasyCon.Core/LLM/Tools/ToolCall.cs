using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Tools;

/// <summary>
/// 模型返回的工具调用（非流式完整结果）。
/// </summary>
public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionCall Function { get; set; } = new();
}

/// <summary>
/// 函数调用信息，含名称和参数 JSON。
/// </summary>
public class FunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 原始参数 JSON 字符串。模型可能返回 malformed JSON，通过 <see cref="ParseArguments"/> 安全解析。
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";

    /// <summary>
    /// 预防性解析 Arguments JSON 为字典。
    /// 解析失败时返回空字典，不会抛异常。
    /// </summary>
    public Dictionary<string, JsonElement> ParseArguments()
    {
        if (string.IsNullOrWhiteSpace(Arguments))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Arguments) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 尝试解析 Arguments JSON，返回是否成功及结果。
    /// </summary>
    public bool TryParseArguments(out Dictionary<string, JsonElement> args)
    {
        args = ParseArguments();
        return !string.IsNullOrWhiteSpace(Arguments) && args.Count > 0;
    }
}