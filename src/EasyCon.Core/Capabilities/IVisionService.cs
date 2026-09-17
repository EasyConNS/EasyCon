namespace EasyCon.Core.Capabilities;

/// <summary>图像处理/标签匹配能力（EzCv 实现）。</summary>
public interface IVisionService
{
    /// <summary>标签匹配：返回匹配到的标签索引，未找到返回 -1。</summary>
    int MatchLabel(string labelName);

    /// <summary>ROI 裁剪（Base64 PNG 入出）；不支持返回 null。</summary>
    string? Crop(string imageBase64, int x, int y, int width, int height);
}