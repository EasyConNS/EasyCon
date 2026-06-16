using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM;

public class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; set; }

    /// <summary>
    /// 工具定义列表，模型可据此决定是否调用工具。
    /// </summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// 工具选择策略。
    /// "auto"（默认自动选择）、"none"（不调用工具）序列化为纯字符串；
    /// 指定函数名时序列化为 { "type":"function","function":{"name":"xxx"} }。
    /// </summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(ToolChoiceConverter))]
    public ToolChoice? ToolChoice { get; set; }
}

/// <summary>
/// 工具选择策略。
/// </summary>
public class ToolChoice
{
    public string Type { get; set; } = "function";

    public ToolChoiceFunction? Function { get; set; }

    /// <summary>"auto"</summary>
    public static ToolChoice Auto => new() { Type = "auto" };

    /// <summary>"none"</summary>
    public static ToolChoice None => new() { Type = "none" };

    /// <summary>强制调用指定函数</summary>
    public static ToolChoice Required(string functionName) => new()
    {
        Type = "function",
        Function = new ToolChoiceFunction { Name = functionName }
    };
}

public class ToolChoiceFunction
{
    public string Name { get; set; } = "";
}

/// <summary>
/// ToolChoice 自定义序列化：
/// "auto"/"none" → 纯字符串；指定函数 → 对象。
/// </summary>
public class ToolChoiceConverter : JsonConverter<ToolChoice>
{
    public override ToolChoice? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return s switch
            {
                "auto" => ToolChoice.Auto,
                "none" => ToolChoice.None,
                _ => new ToolChoice { Type = s ?? "auto" }
            };
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "function" : "function";
        var fnName = root.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var n)
            ? n.GetString()
            : null;
        return fnName is not null ? ToolChoice.Required(fnName) : new ToolChoice { Type = type };
    }

    public override void Write(Utf8JsonWriter writer, ToolChoice value, JsonSerializerOptions options)
    {
        if (value.Function is null)
        {
            // "auto" / "none" 序列化为纯字符串
            writer.WriteStringValue(value.Type);
        }
        else
        {
            // 指定函数序列化为对象
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            writer.WritePropertyName("function");
            JsonSerializer.Serialize(writer, value.Function, options);
            writer.WriteEndObject();
        }
    }
}