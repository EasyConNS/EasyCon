using OpenCvSharp;

namespace EasyCon2.Avalonia.Services;

public interface ICaptureService
{
    bool IsConnected { get; }
    event Action? ConnectionLost;
    event Action? ConnectionRestored;
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    /// <summary>
    /// 获取一帧图像。返回的 Mat 是独立的副本，调用者可以安全使用和释放。
    /// 线程安全。
    /// </summary>
    Mat? GetMatFrame();
    /// <summary>
    /// 设置采集参数（分辨率等）。
    /// </summary>
    void SetCaptureProperties(int width, int height);
}