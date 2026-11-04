using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon.Capture;

/// <summary>与像素一起发布的帧身份。时间表示宿主收到帧的单调时间，不是硬件曝光时间。</summary>
public sealed record FrameMetadata(
    string SourceId,
    long SourceEpoch,
    long FrameId,
    long CapturedAtTicks,
    long ClockFrequency,
    Size OriginalSize,
    Size CanonicalSize,
    string NormalizationId,
    string PixelFormat = "BGR8")
{
    public static FrameMetadata FromImage(Mat image) => new("image", 0, 0,
        Stopwatch.GetTimestamp(), Stopwatch.Frequency, image.Size(), image.Size(), "identity",
        image.Type() == MatType.CV_8UC1 ? "Gray8" : image.Type() == MatType.CV_8UC3 ? "BGR8"
            : image.Type() == MatType.CV_8UC4 ? "BGRA8" : image.Type().ToString());
}

/// <summary>采集端口；通用帧生产者不依赖具体摄像头，也不依赖 Maa。</summary>
public interface IFrameSource : IDisposable
{
    bool IsOpened { get; }
    Mat GetMatFrame();
    void SetProperties(int width, int height);
}