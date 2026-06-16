namespace EasyCon.Core.LLM.Models;

public class ModelInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>是否支持视觉（图片输入）。默认 false。</summary>
    public bool Vision { get; set; }
}