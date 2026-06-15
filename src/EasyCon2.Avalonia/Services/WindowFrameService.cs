using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

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
        window.WindowDecorations = WindowDecorations.BorderOnly;
        window.SystemDecorations = WindowDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;

        if (OperatingSystem.IsMacOS())
        {
            window.WindowDecorations = WindowDecorations.Full;
            window.SystemDecorations = WindowDecorations.Full;
        }

        window.BorderThickness = new Thickness(1);
        UpdateWindowStatePadding(window);
    }

    public static void UpdateWindowStatePadding(Window window)
    {
        // Windows和Linux使用相同的padding逻辑
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
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
