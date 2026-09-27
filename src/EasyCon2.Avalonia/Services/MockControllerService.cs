using Avalonia.Controls;

namespace EasyCon2.Avalonia.Services;

public sealed class MockControllerService : IControllerService
{
    public bool IsConnected => false;
    public event Action? AvailableSourcesChanged;
    public event Action? Disconnected;

    public IReadOnlyList<ControlSourceInfo> GetAvailableSources()
        => [new ControlSourceInfo("键盘", ControllerService.KeyboardSourceId)];

    public bool TryConnect(string sourceId) => false;

    public void Disconnect() { }

    public void SetOwnerWindow(Window owner) { }

    public void Dispose() { }
}