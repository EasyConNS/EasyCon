using Avalonia.Threading;
using EasyCon.Core;
using EasyCon.Core.Services;
using EasyDevice;
using IDeviceService = EasyCon2.Avalonia.Services.IDeviceService;

namespace EasyCon2.Avalonia.Services;

public class DeviceService : IDeviceService, IDisposable
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

    /// <summary>连接状态可能已变化（连接/断开尝试后触发，含 Flow 桥接的外部连接）。非 UI 线程。</summary>
    public event Action? ConnectionStateChanged;

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
            ConnectionStateChanged?.Invoke();
            return true;
        }
        _logService.AddLog($"单片机连接失败: {result}");
        // 失败也广播：重连尝试可能已摘掉旧连接，订阅方按 IsConnected 同步
        ConnectionStateChanged?.Invoke();
        return false;
    }

    public void Disconnect()
    {
        _nintendoSwitch.Disconnect();
        _isConnected = false;
        ConnectionStateChanged?.Invoke();
    }

    /// <summary>应用退出时调用：断开串口连接（Reset 复位手柄 + 丢弃排队报文）。</summary>
    public void Dispose()
    {
        try
        {
            if (_isConnected)
            {
                _nintendoSwitch.Reset();
                _nintendoSwitch.Disconnect();
            }
        }
        catch
        {
            // 退出路径尽力而为；串口句柄最终由进程退出兜底
        }
    }

    public NintendoSwitch GetDevice() => _nintendoSwitch;

    public void Reset() => _nintendoSwitch.Reset();

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

    public bool RemoteStart()
    {
        if (!_isConnected) return false;
        return _nintendoSwitch.RemoteStart();
    }

    public bool RemoteStop()
    {
        if (!_isConnected) return false;
        return _nintendoSwitch.RemoteStop();
    }

    public bool Flash(byte[] asmBytes)
    {
        if (!_isConnected) return false;
        return _nintendoSwitch.Flash(asmBytes);
    }

    public int GetVersion()
    {
        if (!_isConnected) return -1;
        return _nintendoSwitch.GetVersion();
    }

    public bool UnPair()
    {
        if (!_isConnected) return false;
        return _nintendoSwitch.UnPair();
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