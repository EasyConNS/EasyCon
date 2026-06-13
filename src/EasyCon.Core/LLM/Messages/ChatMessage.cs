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

    [JsonPropertyName("content")]
    public object? Content { get; set; }

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
    public static ChatMessage System(string text) => new("system", text);
}
