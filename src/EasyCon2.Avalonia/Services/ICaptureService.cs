using EasyCon.Capture;

namespace EasyCon2.Avalonia.Services;

public interface ICaptureService
{
    bool IsConnected { get; }
    string CaptureType { get; set; }
    event Action? ConnectionLost;
    event Action? ConnectionRestored;
    /// <summary>
    /// 连接状态可能已变化（任意来源的连接/断开成功后触发，含 Flow/agent 经设备桥的外部连接）。
    /// 可能在非 UI 线程触发；订阅方以 <see cref="IsConnected"/> 为唯一事实源做同步。
    /// </summary>
    event Action? ConnectionStateChanged;
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    /// <summary>
    /// 异步断开：采集循环可能阻塞在读取上（Dispose 最长等待数秒），
    /// UI 侧应优先使用本方法避免冻结界面。
    /// </summary>
    Task DisconnectAsync();
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