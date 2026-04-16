using EasyCon.Capture;

namespace EasyCon2.Avalonia.Services;

public interface ICaptureService
{
    bool IsConnected { get; }
    event Action? ConnectionLost;
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    OpenCVCapture? GetCapture();
}
