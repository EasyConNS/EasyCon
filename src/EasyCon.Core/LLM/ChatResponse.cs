using System.Text.Json.Serialization;
using EasyCon.Core.LLM.Tools;

namespace EasyCon.Core.LLM;

public class ChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    public string Content { get; set; } = "";

    /// <summary>
    /// 思考/推理内容。不同供应商字段名可能不同：
    /// DeepSeek 等使用 reasoning_content，部分平台使用 reasoning。
    /// </summary>
    public string? ThinkingContent { get; set; }

    /// <summary>
    /// 模型返回的工具调用列表。模型决定调用工具时填充，此时 Content 通常为空。
    /// </summary>
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// 是否包含工具调用。
    /// </summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }

    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
