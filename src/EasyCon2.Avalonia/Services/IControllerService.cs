namespace EasyCon2.Avalonia.Services;

public interface IControllerService : IDisposable
{
    bool IsConnected { get; }
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    event Action? AvailableSourcesChanged;
    event Action? Disconnected;
}