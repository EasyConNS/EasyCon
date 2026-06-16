using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Tools;

/// <summary>
/// 流式响应中的工具调用增量片段。
/// 同一个 index 的片段需要拼接：首个包含 id/name，后续只包含 arguments 分片。
/// </summary>
public class ToolCallDelta
{
    /// <summary>
    /// 工具调用索引，用于将分片归组到同一个 ToolCall。
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FunctionCallDelta? Function { get; set; }
}

/// <summary>
/// 函数调用增量，arguments 为分片需拼接。
/// </summary>
public class FunctionCallDelta
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }
}