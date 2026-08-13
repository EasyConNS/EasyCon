using EasyCon.Capture;

namespace EasyCon2.Avalonia.Services;

public interface ICaptureService
{
    bool IsConnected { get; }
    string CaptureType { get; set; }
    event Action? ConnectionLost;
    event Action? ConnectionRestored;
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    /// <summary>
    /// 获取最新一帧的租约。未连接或尚无帧时返回 null。
    /// 调用者须持有租约直至不再使用其中的 Mat（含其 ROI 视图）。
    /// </summary>
    FrameLease? AcquireLatestFrame();
    /// <summary>
    /// 设置采集参数（分辨率等）。
    /// </summary>
    void SetCaptureProperties(int width, int height);
}