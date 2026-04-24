namespace EasyCon.Core.Services;

public interface IDeviceService
{
    bool IsConnected { get; }
    bool ShowDebugInfo { get; set; }
    event Action<bool>? StatusChanged;
    string[] GetAvailablePorts();
    bool TryConnect(string port);
    string? AutoConnect();
    void Disconnect();
    bool RemoteStart();
    bool RemoteStop();
    int GetVersion();
    bool Flash(byte[] data);
    void Reset();
    GamePadAdapter CreateGamePadAdapter();
}