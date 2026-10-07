namespace EasyCon.Core.Capabilities;

/// <summary>
/// 截屏帧能力（D4：图像一律 Base64 PNG 字符串过接口；原生 image 类型留待 v3 再议）。
/// </summary>
public interface ICaptureSource
{
    /// <summary>截取区域帧，返回 Base64 PNG；负宽高按宿主约定取整帧。不可用返回 null。</summary>
    string? CaptureFrame(int x, int y, int width, int height);

    /// <summary>
    /// 最近发布帧的单调序号（每发布一帧 +1）；源不提供序号时为 null。
    /// 仅服务 PC 侧的「等待新帧」与慢感知 everyFrames 判定，不进脚本语义、不上 MCU。
    /// </summary>
    long? FrameIndex => null;
}