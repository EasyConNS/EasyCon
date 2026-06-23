using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Tools;

/// <summary>
/// 工具定义，对应 OpenAI tools 数组中的一项。
/// </summary>
public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; set; } = new();

    /// <summary>
    /// 快捷构造函数式工具定义。
    /// </summary>
    public static ToolDefinition Create(string name, string description, JsonSchema parameters)
    {
        return new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = name,
                Description = description,
                Parameters = parameters
            }
        };
    }
}

/// <summary>
/// 函数定义。
/// </summary>
public class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonSchema? Parameters { get; set; }
}

/// <summary>
/// JSON Schema 描述，用于工具参数定义。
/// </summary>
/// <remarks>
/// 默认按 typed 形状（type/properties/required）序列化，覆盖内置工具的扁平参数定义。
/// 若设置了 <see cref="Raw"/>（例如来自 MCP 服务器的完整 inputSchema），
/// 序列化时原样透传该 <see cref="JsonElement"/>，以保留嵌套对象/数组/oneOf 等结构。
/// </remarks>
[JsonConverter(typeof(JsonSchemaConverter))]
public class JsonSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonSchemaProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Required { get; set; }

    /// <summary>
    /// 透传的原始 schema。非空时序列化原样写出，忽略上面的 typed 字段。
    /// 用于 MCP 等外部来源提供的、含嵌套结构的完整 JSON Schema。
    /// </summary>
    internal JsonElement? Raw { get; init; }

    /// <summary>用原始 JSON Schema 构造（原样透传给模型，不丢精度）。</summary>
    public static JsonSchema FromRaw(JsonElement raw) => new() { Raw = raw };
}

/// <summary>
/// <see cref="JsonSchema"/> 的自定义序列化：有 <see cref="JsonSchema.Raw"/> 时原样写出，
/// 否则按 typed 形状序列化（与历史行为完全一致，向后兼容）。
/// </summary>
file sealed class JsonSchemaConverter : JsonConverter<JsonSchema>
{
    public override JsonSchema? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return new JsonSchema { Raw = doc.RootElement.Clone() };
    }

    public override void Write(Utf8JsonWriter writer, JsonSchema value, JsonSerializerOptions options)
    {
        if (value.Raw is { } raw)
        {
            raw.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        if (value.Properties is { } props && props.Count > 0)
        {
            writer.WritePropertyName("properties");
            JsonSerializer.Serialize(writer, props, options);
        }
        if (value.Required is { } required && required.Count > 0)
        {
            writer.WriteStartArray("required");
            foreach (var r in required)
                writer.WriteStringValue(r);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// 单个参数属性。
/// </summary>
public class JsonSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }
}