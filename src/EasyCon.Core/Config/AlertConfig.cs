using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Config;

public class AlertConfig
{
    public int timeout { get; set; } = 10;
    public List<AlertItem> alerts { get; set; } = [];
}

public class AlertItem
{
    [JsonRequired]
    public string name { get; set; } = "";

    public bool enable { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [DefaultValue("GET")]
    public string method { get; set; } = "GET";

    [JsonRequired]
    public string url { get; set; } = "";

    [JsonRequired]
    public string token { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? headers { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string body { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? variables { get; set; }
}