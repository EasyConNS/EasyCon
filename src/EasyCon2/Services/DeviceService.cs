using EasyCon.Core;
using EasyDevice;
using System.Media;

namespace EasyCon2.Services;

public class DeviceService
{
    private readonly NintendoSwitch _ns = new();

    /// <summary>连接状态变化</summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>调试日志</summary>
    public event Action<string>? Log;

    /// <summary>状态栏消息</summary>
    public event Action<string>? StatusChanged;

    public bool IsConnected => _ns.IsConnected();
    public NintendoSwitch Device => _ns;
    public bool DebugLogEnabled { get; set; }

    public RecordState RecordState => _ns.recordState;

    public DeviceService()
    {
        _ns.StatusChanged += status =>
        {
            ConnectionStateChanged?.Invoke(_ns.IsConnected());
        };

        _ns.Log += message =>
        {
            if (DebugLogEnabled)
                Log?.Invoke($"NS LOG >> {message}");
        };

        _ns.BytesSent += (port, bytes) =>
        {
            if (DebugLogEnabled)
                Log?.Invoke($"{port} >> {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");
        };

        _ns.BytesReceived += (port, bytes) =>
        {
            if (DebugLogEnabled)
                Log?.Invoke($"{port} << {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");
        };
    }

    /// <summary>
    /// 获取可用串口列表
    /// </summary>
    public string[] GetPortNames() => [.. ECCore.GetDeviceNames()];

    /// <summary>
    /// 自动搜索并连接设备
    /// </summary>
    public async Task<(bool success, string? connectedPort)> AutoConnectAsync()
    {
        var ports = GetPortNames();
        string? connectedPort = null;
        bool success = false;
        StatusChanged?.Invoke("尝试连接...");

        await Task.Run(() =>
        {
            foreach (var portName in ports)
            {
                var r = _ns.TryConnect(portName);
                if (DebugLogEnabled)
                    Log?.Invoke($"{portName} {r.GetName()}");

                if (r == NintendoSwitch.ConnectResult.Success)
                {
                    connectedPort = portName;
                    success = true;
                    break;
                }
                // 等待 1 秒再尝试下一个端口
                Thread.Sleep(1000);
            }
        });

        if (success)
        {
            StatusChanged?.Invoke("连接成功");
            SystemSounds.Beep.Play();
            ConnectionStateChanged?.Invoke(true);
        }
        else
        {
            StatusChanged?.Invoke("连接失败");
            SystemSounds.Hand.Play();
        }

        return (success, connectedPort);
    }

    /// <summary>
    /// 手动连接指定端口
    /// </summary>
    public async Task<bool> ManualConnectAsync(string port)
    {
        var result = await Task.Run(() => _ns.TryConnect(port));

        if (result == NintendoSwitch.ConnectResult.Success)
        {
            StatusChanged?.Invoke("连接成功");
            SystemSounds.Beep.Play();
            ConnectionStateChanged?.Invoke(true);
            return true;
        }

        StatusChanged?.Invoke("连接失败");
        SystemSounds.Hand.Play();
        return false;
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        _ns.Disconnect();
        ConnectionStateChanged?.Invoke(false);
    }

    // ---- 烧录/远程控制 ----

    public bool RemoteStart() => _ns.RemoteStart();

    public bool RemoteStop() => _ns.RemoteStop();

    public bool Flash(byte[] data) => _ns.Flash(data);

    public int GetVersion() => _ns.GetVersion();

    public bool UnPair() => _ns.UnPair();

    // ---- 脚本录制 ----

    public void StartRecord() => _ns.StartRecord();

    public void StopRecord() => _ns.StopRecord();

    public void PauseRecord() => _ns.PauseRecord();

    public string GetRecordScript() => _ns.GetRecordScript();
}