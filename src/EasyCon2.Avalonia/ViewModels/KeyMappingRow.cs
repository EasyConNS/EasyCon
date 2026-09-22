using CommunityToolkit.Mvvm.ComponentModel;
using EasyCon2.Avalonia.Controls;

namespace EasyCon2.Avalonia.ViewModels;

/// <summary>
/// 单个按键映射行的可观察模型 —— 对应 <see cref="EasyCon.Core.Config.KeyMappingConfig"/> 中的一个属性。
/// 每行由「键盘键帽 + Switch 按键图标」两个 44×44 图标组成，固定在 1440×960 设计画布上。
/// 键帽由当前 <see cref="Scancode"/> 反推：有对应图标则用图标，否则回退为文字键帽。
/// 颜色不再由本类持有 —— 由窗口样式根据 <see cref="IsMapped"/> / <see cref="IsUnmapped"/> / <see cref="IsHighlighted"/> 决定。
/// </summary>
public partial class KeyMappingRow : ObservableObject
{
    private const string UnboundIcon = "ico-unbound";
    private const string KeyIconPrefix = "key-";

    /// <summary>KeyMappingConfig 中的属性名（必须精确匹配其中一个属性）。</summary>
    public string PropertyName { get; init; } = "";

    /// <summary>显示名称（如 "A", "ZL", "LS↑"）。</summary>
    public string ActionName { get; init; } = "";

    /// <summary>Switch 按键图标符号 id（如 ico-btn-l），对应 Icons.json 中的同名条目。</summary>
    public string ButtonIcon { get; init; } = "";

    /// <summary>点击手柄图时需要高亮的 SVG 分组 id（如 face-a、stick-l、dpad-up）。</summary>
    public string ControllerId { get; init; } = "";

    /// <summary>行容器在 1440×960 画布上的 X 坐标。</summary>
    public double X { get; init; }

    /// <summary>行容器在 1440×960 画布上的 Y 坐标。</summary>
    public double Y { get; init; }

    /// <summary>键帽图标相对行容器的 X 偏移。</summary>
    public double KeyIconLeft { get; init; }

    /// <summary>Switch 图标相对行容器的 X 偏移。</summary>
    public double ButtonIconLeft { get; init; }

    /// <summary>SDL 扫描码（0 = 未绑定）。</summary>
    [ObservableProperty]
    private int _scancode;

    /// <summary>显示用的按键名（如 "L", "K", "—"）。</summary>
    [ObservableProperty]
    private string _displayKey = "—";

    /// <summary>是否正在等待按键输入。</summary>
    [ObservableProperty]
    private bool _isListening;

    /// <summary>是否为当前高亮行（监听中或刚完成绑定）。</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>是否禁用 —— 当前没有禁用项。</summary>
    [ObservableProperty]
    private bool _isDisabled;

    /// <summary>是否与其他行存在按键冲突（同一扫描码被多行占用）；仅作视觉提示，不阻止编辑。</summary>
    [ObservableProperty]
    private bool _isConflicted;

    /// <summary>已映射（扫描码非 0）。</summary>
    public bool IsMapped => Scancode != 0;

    /// <summary>未映射（扫描码为 0）。</summary>
    public bool IsUnmapped => Scancode == 0;

    /// <summary>键帽图标 id —— 由当前扫描码决定，未绑定用通用占位键帽。</summary>
    public string KeyIcon => Scancode == 0 ? UnboundIcon : KeyIconPrefix + Scancode;

    /// <summary>该绑定是否有对应图标；没有则回退为文字键帽。</summary>
    public bool HasKeyIcon => IconRegistry.Exists(KeyIcon);

    partial void OnScancodeChanged(int value)
    {
        OnPropertyChanged(nameof(IsMapped));
        OnPropertyChanged(nameof(IsUnmapped));
        OnPropertyChanged(nameof(KeyIcon));
        OnPropertyChanged(nameof(HasKeyIcon));
    }
}