using System.Text.Json.Serialization;

namespace EasyCon2.Avalonia.Model;

public class AmiiboInfo
{
    [JsonPropertyName("amiiboSeries")]
    public string AmiiboSeries { get; set; } = "";

    [JsonPropertyName("character")]
    public string Character { get; set; } = "";

    [JsonPropertyName("gameSeries")]
    public string GameSeries { get; set; } = "";

    [JsonPropertyName("head")]
    public string Head { get; set; } = "";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tail")]
    public string Tail { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("release")]
    public Dictionary<string, string>? Release { get; set; }
}