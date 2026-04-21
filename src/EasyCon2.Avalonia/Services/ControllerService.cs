namespace EasyCon2.Avalonia.Services;

public class ControllerService : IControllerService
{
    private static readonly string[] Sources = ["键盘", "Pro手柄"];
    private bool _isConnected;

    public bool IsConnected => _isConnected;

    public string[] GetAvailableSources() => Sources;

    public bool TryConnect(string sourceName)
    {
        Thread.Sleep(1500);
        var success = Random.Shared.Next(3) < 2;
        if (success)
            _isConnected = true;
        return success;
    }

    public void Disconnect()
    {
        _isConnected = false;
    }
}