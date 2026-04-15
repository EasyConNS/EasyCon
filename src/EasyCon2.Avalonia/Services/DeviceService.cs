using Avalonia.Threading;
using EasyCon.Core;
using EasyDevice;

namespace EC.Avalonia.Services;

public class DeviceService : IDeviceService
{
    private readonly ILogService _logService;
    private readonly NintendoSwitch _nintendoSwitch = new();
    private bool _isConnected;

    public bool IsConnected => _isConnected;
    public event Action? ConnectionLost;

    public DeviceService(ILogService logService)
    {
        _logService = logService;
        _nintendoSwitch.StatusChanged += OnStatusChanged;
    }

    public string[] GetAvailablePorts() => ECCore.GetDeviceNames().ToArray();

    public NintendoSwitch.ConnectResult TryConnect(string port)
    {
        var result = _nintendoSwitch.TryConnect(port);
        if (result == NintendoSwitch.ConnectResult.Success)
            _isConnected = true;
        return result;
    }

    public void Disconnect()
    {
        _nintendoSwitch.Disconnect();
        _isConnected = false;
    }

    public NintendoSwitch GetDevice() => _nintendoSwitch;

    private void OnStatusChanged(Status status)
    {
        if (_isConnected && !_nintendoSwitch.IsConnected())
        {
            _isConnected = false;
            Dispatcher.UIThread.Post(() =>
            {
                ConnectionLost?.Invoke();
                _logService.AddLog("单片机已从外部断开");
            });
        }
    }
}
