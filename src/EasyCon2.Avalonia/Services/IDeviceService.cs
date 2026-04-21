using EasyDevice;

namespace EasyCon2.Avalonia.Services;

public interface IDeviceService
{
    bool IsConnected { get; }
    event Action? ConnectionLost;
    string[] GetAvailablePorts();
    bool TryConnect(string port);
    string? AutoConnect();
    void Disconnect();
    NintendoSwitch GetDevice();
}