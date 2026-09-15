namespace EasyCon.Core.Capabilities;

/// <summary>
/// 截屏帧能力（D4：图像一律 Base64 PNG 字符串过接口；原生 image 类型留待 v3 再议）。
/// </summary>
public interface ICaptureSource
{
    /// <summary>截取区域帧，返回 Base64 PNG；负宽高按宿主约定取整帧。不可用返回 null。</summary>
    string? CaptureFrame(int x, int y, int width, int height);
}