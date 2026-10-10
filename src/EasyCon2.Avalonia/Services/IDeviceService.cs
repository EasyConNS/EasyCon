using EasyDevice;

namespace EasyCon2.Avalonia.Services;

public interface IDeviceService
{
    bool IsConnected { get; }
    bool ShowDebugInfo { get; set; }
    event Action? ConnectionLost;
    /// <summary>
    /// 连接状态可能已变化（任意来源的连接/断开尝试后触发，含 Flow/agent 经设备桥的外部连接）。
    /// 可能在非 UI 线程触发；订阅方以 <see cref="IsConnected"/> 为唯一事实源做同步。
    /// </summary>
    event Action? ConnectionStateChanged;
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