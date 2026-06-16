using System;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.Controls;

/// <summary>
/// 所有自定义窗口的基类，负责平台初始化、拖拽移动和调整大小。
/// - macOS: Full 装饰（原生交通灯），拖拽由系统处理
/// - Windows/Linux: BorderOnly 装饰，自绘系统按钮，通过 BeginMoveDrag 拖拽
/// </summary>
public class ChromelessWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    public ChromelessWindow()
    {
        Focusable = true;
        WindowFrameService.SetupWindow(this);
        SetPlatformClasses();
    }

    private void SetPlatformClasses()
    {
        Classes.Set("platform-macos", OperatingSystem.IsMacOS());
        Classes.Set("platform-windows", !OperatingSystem.IsMacOS());
        Classes.Set("platform-linux", OperatingSystem.IsLinux());
    }

    /// <summary>
    /// 用于标题栏 PointerPressed 事件，启动窗口拖拽移动。
    /// </summary>
    public void BeginMoveWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 1)
            BeginMoveDrag(e);
        e.Handled = true;
    }

    /// <summary>
    /// 双击标题栏切换最大化/还原。
    /// </summary>
    public void MaximizeOrRestoreWindow(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        e.Handled = true;
    }

    /// <summary>
    /// 仅 Linux 需要手动挂接调整大小边框。
    /// Windows (BorderOnly) 和 macOS (Full) 系统自带调整大小能力。
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (!OperatingSystem.IsLinux() || !CanResize)
            return;

        string[] borderNames =
        [
            "PART_BorderTopLeft", "PART_BorderTop", "PART_BorderTopRight",
            "PART_BorderLeft", "PART_BorderRight",
            "PART_BorderBottomLeft", "PART_BorderBottom", "PART_BorderBottomRight",
        ];

        foreach (var name in borderNames)
        {
            if (e.NameScope.Find<Border>(name) is { } border)
            {
                border.PointerPressed -= OnResizeBorderPressed;
                border.PointerPressed += OnResizeBorderPressed;

                // 提高角落边框层级，使其压在相邻边之上，否则角落会落到边框上，
                // 只剩横向/竖向光标、无法对角缩放。
                if (name is "PART_BorderTopLeft" or "PART_BorderTopRight"
                    or "PART_BorderBottomLeft" or "PART_BorderBottomRight")
                    border.ZIndex = 2;
            }
        }
    }

    private void OnResizeBorderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: WindowEdge edge } && CanResize)
            BeginResizeDrag(edge, e);
    }
}
