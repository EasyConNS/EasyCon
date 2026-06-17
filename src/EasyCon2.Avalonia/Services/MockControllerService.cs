using Avalonia.Controls;

namespace EasyCon2.Avalonia.Services;

public sealed class MockControllerService : IControllerService
{
    public bool IsConnected => false;
    public event Action? AvailableSourcesChanged;
    public event Action? Disconnected;

    public string[] GetAvailableSources() => [];

    public bool TryConnect(string sourceName) => false;

    public void Disconnect() { }

    public void SetOwnerWindow(Window owner) { }

    public void Dispose() { }
}
