namespace EasyCon.Core.Config;

public class AlertConfig
{
    public int timeout { get; set; } = 10;
    public List<AlertItem> alerts { get; set; } = [];
}

public class AlertItem
{
    public string name { get; set; } = "";
    public bool enable { get; set; }
    public string method { get; set; } = "GET";
    public string url { get; set; } = "";
    public string token { get; set; } = "";
    public Dictionary<string, string> headers { get; set; }
    public string body { get; set; }
    public Dictionary<string, string> variables { get; set; }
}
