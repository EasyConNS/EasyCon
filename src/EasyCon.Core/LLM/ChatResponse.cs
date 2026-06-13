using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM;

public class ChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    public string Content { get; set; } = "";

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }

    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
