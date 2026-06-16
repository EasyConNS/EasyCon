namespace EasyCon.Core.LLM.Models;

public class ProviderConfig
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Api { get; set; } = "openai-completions";
    public List<ModelInfo> Models { get; set; } = [];
}