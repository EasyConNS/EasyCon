using System.Text.Json.Serialization;

namespace EasyCon.Core.LLM.Messages;

public class ContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageUrlPart? ImageUrl { get; set; }

    public static ContentPart FromText(string text) => new() { Type = "text", Text = text };

    public static ContentPart FromImageUrl(string url) => new()
    {
        Type = "image_url",
        ImageUrl = new ImageUrlPart { Url = url }
    };

    public static ContentPart FromImageBase64(string mediaType, string base64Data) => new()
    {
        Type = "image_url",
        ImageUrl = new ImageUrlPart { Url = $"data:{mediaType};base64,{base64Data}" }
    };
}

public class ImageUrlPart
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}