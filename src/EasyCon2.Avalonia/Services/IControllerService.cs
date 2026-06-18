using Avalonia.Controls;

namespace EasyCon2.Avalonia.Services;

public interface IControllerService : IDisposable
{
    bool IsConnected { get; }
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    void SetOwnerWindow(Window owner);
    event Action? AvailableSourcesChanged;
    event Action? Disconnected;
}