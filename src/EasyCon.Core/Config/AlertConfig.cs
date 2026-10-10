using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Config;

public class AlertConfig
{
    public int schema_version { get; set; }
    public int timeout { get; set; } = 10;
    public List<AlertItem> alerts { get; set; } = [];
}

public class AlertItem
{
    public const string WebhookProvider = "webhook";
    public const string QqProvider = "qq";
    public const string QqDefaultName = "qq bot";

    public string id { get; set; } = "";
    public string provider { get; set; } = WebhookProvider;

    [JsonRequired]
    public string name { get; set; } = "";

    public bool enable { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [DefaultValue("GET")]
    public string method { get; set; } = "GET";

    public string url { get; set; } = "";

    public string token { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? headers { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string body { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? variables { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QQNotificationSettings? qq { get; set; }

    [JsonIgnore]
    public bool IsQq => string.Equals(provider, QqProvider, StringComparison.OrdinalIgnoreCase);

    public static AlertItem CreateQq() => new()
    {
        id = Guid.NewGuid().ToString("N"),
        provider = QqProvider,
        name = QqDefaultName,
        qq = new QQNotificationSettings(),
    };

    public AlertItem Clone() => new()
    {
        id = id,
        provider = provider,
        name = name,
        enable = enable,
        method = method,
        url = url,
        token = token,
        body = body,
        headers = headers == null ? null : new Dictionary<string, string>(headers),
        variables = variables == null ? null : new Dictionary<string, string>(variables),
        qq = qq?.Clone(),
    };
}