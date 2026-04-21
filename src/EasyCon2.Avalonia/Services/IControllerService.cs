namespace EasyCon2.Avalonia.Services;

public interface IControllerService
{
    bool IsConnected { get; }
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
}