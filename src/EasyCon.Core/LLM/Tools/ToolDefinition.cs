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