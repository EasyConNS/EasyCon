using EasyDevice;

namespace EC.Avalonia.Services;

public interface IDeviceService
{
    bool IsConnected { get; }
    event Action? ConnectionLost;
    string[] GetAvailablePorts();
    NintendoSwitch.ConnectResult TryConnect(string port);
    void Disconnect();
    NintendoSwitch GetDevice();
}
