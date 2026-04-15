using EasyCon.Capture;

namespace EC.Avalonia.Services;

public interface ICaptureService
{
    bool IsConnected { get; }
    event Action? ConnectionLost;
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    OpenCVCapture? GetCapture();
}
