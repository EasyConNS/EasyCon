namespace EasyCon.Core.LLM.Models;

/// <summary>
/// 扁平化的模型条目，聚合模型信息与其所属供应商。
/// 用于 UI 展示：主文本为模型名称，副文本为供应商。
/// </summary>
public class ModelEntry
{
    public string ProviderKey { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string ProviderLabel { get; init; } = "";
}
