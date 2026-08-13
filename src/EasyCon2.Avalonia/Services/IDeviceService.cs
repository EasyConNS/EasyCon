using EasyDevice;

namespace EasyCon2.Avalonia.Services;

public interface IDeviceService
{
    bool IsConnected { get; }
    bool ShowDebugInfo { get; set; }
    event Action? ConnectionLost;
    string[] GetAvailablePorts();
    bool TryConnect(string port);
    string? AutoConnect();
    void Disconnect();
    NintendoSwitch GetDevice();

    /// <summary>
    /// 释放所有按键并清空待发送队列，脚本终止时调用。
    /// </summary>
    void Reset();

    // 远程控制
    bool RemoteStart();
    bool RemoteStop();

    // 烧录
    bool Flash(byte[] asmBytes);
    int GetVersion();

    // 配对
    bool UnPair();
}