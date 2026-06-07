using Avalonia.Threading;
using EasyCon.Core;
using EasyDevice;

namespace EasyCon2.Avalonia.Services;

public class DeviceService : IDeviceService
{
    private readonly ILogService _logService;
    private readonly NintendoSwitch _nintendoSwitch = new();
    private bool _isConnected;
    private bool _showDebugInfo;

    public bool IsConnected => _isConnected;
    public bool ShowDebugInfo
    {
        get => _showDebugInfo;
        set
        {
            if (_showDebugInfo == value)
                return;

            _showDebugInfo = value;
            if (value)
            {
                _nintendoSwitch.Log += OnNSLog;
                _nintendoSwitch.BytesSent += OnNSBytesSent;
                _nintendoSwitch.BytesReceived += OnNSBytesReceived;
            }
            else
            {
                _nintendoSwitch.Log -= OnNSLog;
                _nintendoSwitch.BytesSent -= OnNSBytesSent;
                _nintendoSwitch.BytesReceived -= OnNSBytesReceived;
            }
        }
    }

    public event Action? ConnectionLost;

    public DeviceService(ILogService logService)
    {
        _logService = logService;
        _nintendoSwitch.StatusChanged += OnStatusChanged;
    }

    public string[] GetAvailablePorts() => ECCore.GetDeviceNames().ToArray();

    public bool TryConnect(string port)
    {
        var result = _nintendoSwitch.TryConnect(port);
        if (result == NintendoSwitch.ConnectResult.Success)
        {
            _isConnected = true;
            return true;
        }
        _logService.AddLog($"单片机连接失败: {result}");
        return false;
    }

    public void Disconnect()
    {
        _nintendoSwitch.Disconnect();
        _isConnected = false;
    }

    public NintendoSwitch GetDevice() => _nintendoSwitch;

    public string? AutoConnect()
    {
        var ports = GetAvailablePorts();
        foreach (var port in ports)
        {
            if (TryConnect(port))
                return port;
            Thread.Sleep(1000);
        }
        return null;
    }

    private void OnNSLog(string message)
    {
        _logService.AddLog($"NS LOG >> {message}");
    }

    private void OnNSBytesSent(string port, byte[] bytes)
    {
        _logService.AddLog($"{port} >> {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");
    }

    private void OnNSBytesReceived(string port, byte[] bytes)
    {
        _logService.AddLog($"{port} << {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");
    }

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
