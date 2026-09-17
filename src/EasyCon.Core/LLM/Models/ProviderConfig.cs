namespace EasyCon.Core.LLM.Models;

public class ProviderConfig
{
    /// <summary>供应商显示名称（UI 展示用，缺省时回退到字典 key）。</summary>
    public string Name { get; set; } = "";
    /// <summary>供应商主页 URL（可选）。</summary>
    public string HomePage { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Api { get; set; } = "openai-completions";
    public List<ModelInfo> Models { get; set; } = [];
}