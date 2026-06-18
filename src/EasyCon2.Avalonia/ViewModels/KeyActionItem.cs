using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyCon2.Avalonia.ViewModels;

/// <summary>
/// 单个按键映射项的可观察模型 —— 对应 KeyMappingConfig 中的一个属性。
/// </summary>
public partial class KeyActionItem : ObservableObject
{
    /// <summary>显示名称（如 "A", "ZL", "LS↑"）</summary>
    public string ActionName { get; init; } = "";

    /// <summary>KeyMappingConfig 中的属性名</summary>
    public string PropertyName { get; init; } = "";

    /// <summary>热区在 Canvas 上的 X 坐标</summary>
    public double X { get; init; }

    /// <summary>热区在 Canvas 上的 Y 坐标</summary>
    public double Y { get; init; }

    /// <summary>热区宽度</summary>
    public double Width { get; init; }

    /// <summary>热区高度</summary>
    public double Height { get; init; }

    /// <summary>SDL 扫描码（0 = 未绑定）</summary>
    [ObservableProperty]
    private int _scancode;

    /// <summary>显示用的按键名（如 "L", "K", "—"）</summary>
    [ObservableProperty]
    private string _displayKey = "—";

    /// <summary>是否正在等待按键输入</summary>
    [ObservableProperty]
    private bool _isListening;
}