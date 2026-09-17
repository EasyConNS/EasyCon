using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;

namespace EasyCon2.Avalonia.Services;

internal static class WindowFrameService
{
    public static AppBuilder UsePlatformWindowFrame(this AppBuilder builder)
    {
        if (OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Window.WindowStateProperty.Changed.AddClassHandler<Window>((window, _) => FixDwmFrameOnWindows(window));
            Control.LoadedEvent.AddClassHandler<Window>((window, _) => FixDwmFrameOnWindows(window));
        }

        return builder;
    }

    public static void SetupWindow(Window window)
    {
        window.ExtendClientAreaToDecorationsHint = true;

        if (OperatingSystem.IsMacOS())
        {
            // macOS: Full 装饰，系统绘制原生交通灯按钮和窗口标题
            window.SystemDecorations = WindowDecorations.Full;
            window.WindowDecorations = WindowDecorations.Full;
        }
        else
        {
            // Windows/Linux: BorderOnly，无原生系统按钮，由应用自绘
            window.SystemDecorations = WindowDecorations.BorderOnly;
            window.WindowDecorations = WindowDecorations.BorderOnly;
        }

        // Linux 无边框窗口拿不到合成器原生阴影：开启透明，
        // 由 MainWindow.axaml 中的 BoxShadow 自绘窗口外阴影。
        if (OperatingSystem.IsLinux())
            window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        window.BorderThickness = new Thickness(1);
        UpdateWindowStatePadding(window);
    }

    public static void UpdateWindowStatePadding(Window window)
    {
        // 供 XAML 样式按窗口状态切换自绘阴影（最大化时收回外阴影与留白）。
        window.Classes.Set("maximized", window.WindowState == WindowState.Maximized);

        // Linux: 阴影留白完全交给 XAML 中的 Border Margin；窗口 Padding 始终为 0，
        // 否则透明窗口在最大化时会出现外部留白。
        if (OperatingSystem.IsLinux())
        {
            window.Padding = new Thickness(0);
            window.BorderThickness = window.WindowState == WindowState.Maximized
                ? new Thickness(0)
                : new Thickness(1);
            return;
        }

        if (!OperatingSystem.IsWindows())
            return;

        if (window.WindowState == WindowState.Maximized)
        {
            window.BorderThickness = new Thickness(0);
            window.Padding = new Thickness(8, 6, 8, 8);
        }
        else
        {
            window.BorderThickness = new Thickness(1);
            window.Padding = new Thickness(0);
        }
    }

    private static void FixDwmFrameOnWindows(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        Dispatcher.UIThread.Post(() =>
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null)
                return;

            var margins = new Margins
            {
                Left = 1,
                Right = 1,
                Top = 1,
                Bottom = 1
            };

            DwmExtendFrameIntoClientArea(handle.Handle, ref margins);
        }, DispatcherPriority.Render);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}