using Avalonia.Threading;
using EasyCon.Core;
using EasyCon.Core.Maa;
using EasyCon.Core.Services;
using EasyDevice;
using IDeviceService = EasyCon2.Avalonia.Services.IDeviceService;

namespace EasyCon2.Avalonia.Services;

public class DeviceService : IDeviceService, IDisposable
{
    private readonly ILogService _logService;
    private readonly IControllerSession _nintendoSwitch;
    private bool _isConnected;
    private bool _showDebugInfo;

    public bool IsConnected => _nintendoSwitch.InputConnected;
    public bool SupportsLegacyCommands => _nintendoSwitch.SupportsLegacyCommands;
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

    public DeviceService(ILogService logService, IControllerSession controller)
    {
        _logService = logService;
        _nintendoSwitch = controller;
        _nintendoSwitch.StatusChanged += OnStatusChanged;
    }

    public string[] GetAvailablePorts() => ECCore.GetDeviceNames().ToArray();

    public bool TryConnect(string port)
    {
        var result = _nintendoSwitch.TryConnect(port);
        if (result == ControllerConnectResult.Success)
        {
            _isConnected = true;
            _logService.AddLog($"手柄协议: {_nintendoSwitch.FirmwareName} {_nintendoSwitch.FirmwareVersion}");
            if (_nintendoSwitch.ConsoleConnected == false)
                _logService.AddLog("已连接手柄板，NS 尚未接管手柄；请检查 USB 连接或在 NS 的更改握法/顺序中配对。");
            ConnectionStateChanged?.Invoke();
            return true;
        }
        _isConnected = false;
        _logService.AddLog($"单片机连接失败: {result} {_nintendoSwitch.LastConnectionError}");
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
            _nintendoSwitch.Dispose();
        }
        catch
        {
            // 退出路径尽力而为；串口句柄最终由进程退出兜底
        }
    }

    public IControllerSession GetDevice() => _nintendoSwitch;

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
        if (!RequireLegacyCommands()) return false;
        return _nintendoSwitch.RemoteStart();
    }

    public bool RemoteStop()
    {
        if (!RequireLegacyCommands()) return false;
        return _nintendoSwitch.RemoteStop();
    }

    public bool Flash(byte[] asmBytes)
    {
        if (!RequireLegacyCommands()) return false;
        return _nintendoSwitch.Flash(asmBytes);
    }

    public int GetVersion()
    {
        if (!RequireLegacyCommands()) return -1;
        return _nintendoSwitch.GetVersion();
    }

    public bool UnPair()
    {
        if (!RequireLegacyCommands()) return false;
        return _nintendoSwitch.UnPair();
    }

    private bool RequireLegacyCommands()
    {
        if (!IsConnected) return false;
        if (SupportsLegacyCommands) return true;
        _logService.AddLog("pokeldn-pad 支持在线按键和电脑端脚本；不支持伊机控离线烧录、远程运行及解除配对。请使用运行脚本/停止脚本，配对在 NS 上完成。");
        return false;
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