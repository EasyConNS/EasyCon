using Avalonia;
using Avalonia.Controls;

namespace EasyCon2.Avalonia.Controls;

/// <summary>
/// macOS 交通灯按钮（关闭/最小化/最大化）的占位控件。
/// 在 macOS 上测量为 76×24（标准交通灯区域），其他平台测量为 0×0。
/// </summary>
public class MacOSTrafficLightsSpacer : Control
{
    public MacOSTrafficLightsSpacer()
    {
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return OperatingSystem.IsMacOS()
            ? new Size(76, 24)
            : new Size(0, 0);
    }
}
