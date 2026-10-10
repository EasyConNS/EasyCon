namespace EasyCon.Core.Capabilities;

/// <summary>
/// 截屏帧能力（D4：图像一律 Base64 PNG 字符串过接口；原生 image 类型留待 v3 再议）。
/// </summary>
public interface ICaptureSource
{
    /// <summary>
    /// 截取区域帧，返回 Base64 PNG；不可用返回 null。
    /// ROI 语义：负值=整帧（脚本 ABI 哨兵，仅脚本通路使用）；
    /// 0 尺寸 ROI 收敛为空，返回 <c>null</c>。新代码取整帧请用 <see cref="CaptureFullFrame"/>。
    /// </summary>
    string? CaptureFrame(int x, int y, int width, int height);

    /// <summary>整帧语义的显式入口：调用方无需知道负数哨兵约定。</summary>
    string? CaptureFullFrame() => CaptureFrame(-1, -1, -1, -1);

    /// <summary>
    /// 最近发布帧的单调序号（每发布一帧 +1）；源不提供序号时为 null。
    /// 仅服务 PC 侧的「等待新帧」与慢感知 everyFrames 判定，不进脚本语义、不上 MCU。
    /// </summary>
    long? FrameIndex => null;
}