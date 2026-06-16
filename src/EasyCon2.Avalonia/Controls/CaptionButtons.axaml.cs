using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Controls;

/// <summary>
/// 可复用的窗口标题栏系统按钮（最小化 / 最大化·还原 / 关闭）。
/// 通过 TopLevel 获取宿主窗口，无外部依赖。
/// </summary>
public partial class CaptionButtons : UserControl
{
    public static readonly StyledProperty<bool> IsCloseButtonOnlyProperty =
        AvaloniaProperty.Register<CaptionButtons, bool>(nameof(IsCloseButtonOnly));

    /// <summary>
    /// 为 true 时隐藏最小化和最大化按钮，仅保留关闭按钮（适用于对话框）。
    /// </summary>
    public bool IsCloseButtonOnly
    {
        get => GetValue(IsCloseButtonOnlyProperty);
        set => SetValue(IsCloseButtonOnlyProperty, value);
    }

    public CaptionButtons()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 由宿主窗口在 WindowState 变化时调用，
    /// 更新最大化/还原按钮图标和窗口 CSS 类。
    /// </summary>
    public void UpdateState(WindowState state)
    {
        var maximized = state == WindowState.Maximized;

        if (TopLevel.GetTopLevel(this) is Window window)
            window.Classes.Set("maximized", maximized);

        if (PART_MaxRestore != null)
            PART_MaxRestore.Content = maximized ? "❐" : "□";
    }

    private Window? GetHostWindow()
        => TopLevel.GetTopLevel(this) as Window;

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (GetHostWindow() is { } w)
            w.WindowState = WindowState.Minimized;
        e.Handled = true;
    }

    private void OnMaximizeRestore(object? sender, RoutedEventArgs e)
    {
        if (GetHostWindow() is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        e.Handled = true;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        GetHostWindow()?.Close();
        e.Handled = true;
    }
}
