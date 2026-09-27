using Avalonia.Controls;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 控制源条目。<see cref="DisplayName"/> 用于下拉显示，
/// <see cref="SourceId"/> 用于连接（结构化传递，避免从显示文本反解析设备 id）。
/// </summary>
public sealed record ControlSourceInfo(string DisplayName, string SourceId);

public interface IControllerService : IDisposable
{
    bool IsConnected { get; }
    IReadOnlyList<ControlSourceInfo> GetAvailableSources();
    bool TryConnect(string sourceId);
    void Disconnect();
    void SetOwnerWindow(Window owner);
    event Action? AvailableSourcesChanged;
    event Action? Disconnected;
}