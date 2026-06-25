using EasyCon.Core.LLM.Tools;
using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Messages;

/// <summary>
/// OpenAI Chat Completions 兼容的消息模型。
/// Content 可以是纯文本字符串，也可以是 List&lt;ContentPart&gt; 多模态内容。
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>
    /// 消息内容。可为字符串、List&lt;ContentPart&gt; 多模态数组，或 null（assistant tool_calls 消息）。
    /// 显式标注 Never 确保 null 也序列化为 "content": null，部分供应商严格要求此格式。
    /// </summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Content { get; set; }

    /// <summary>
    /// assistant 角色携带的工具调用列表。仅当模型决定调用工具时填充。
    /// </summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// tool 角色回传工具结果时，需匹配对应 tool_call_id。
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    /// <summary>
    /// tool 角色对应的函数名（部分供应商需要）。
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    public ChatMessage() { }

    public ChatMessage(string role, string text)
    {
        Role = role;
        Content = text;
    }

    public ChatMessage(string role, List<ContentPart> parts)
    {
        Role = role;
        Content = parts;
    }

    // 快捷构造
    public static ChatMessage User(string text) => new("user", text);
    public static ChatMessage User(List<ContentPart> parts) => new("user", parts);
    public static ChatMessage Assistant(string text) => new("assistant", text);

    /// <summary>
    /// 构造带工具调用的 assistant 消息（用于回放历史）。
    /// </summary>
    public static ChatMessage Assistant(List<ToolCall> toolCalls) => new()
    {
        Role = "assistant",
        Content = null,
        ToolCalls = toolCalls
    };

    public static ChatMessage System(string text) => new("system", text);

    /// <summary>
    /// 构造 tool 角色消息，回传工具执行结果。
    /// </summary>
    public static ChatMessage Tool(string toolCallId, string result, string? functionName = null) => new()
    {
        Role = "tool",
        Content = result,
        ToolCallId = toolCallId,
        Name = functionName
    };
}