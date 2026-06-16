using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// IWindowService 实现 —— 负责所有子窗口的创建与生命周期管理。
/// </summary>
public class WindowService : IWindowService
{
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;
    private Window? _espConfigWindow;

    public WindowService(IDeviceService deviceService, ILogService logService, IDialogService dialogService)
    {
        _deviceService = deviceService;
        _logService = logService;
        _dialogService = dialogService;
    }

    public void ShowESPConfigWindow()
    {
        if (_espConfigWindow != null)
        {
            if (_espConfigWindow.WindowState == WindowState.Minimized)
                _espConfigWindow.WindowState = WindowState.Normal;
            _espConfigWindow.Activate();
            return;
        }

        try
        {
            var vm = new ESPConfigViewModel(_deviceService, _logService, _dialogService);
            _espConfigWindow = new ESPConfigWindow { DataContext = vm };
            _espConfigWindow.Closed += (_, _) => _espConfigWindow = null;
            _espConfigWindow.Show();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开手柄设置失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void ShowAlertConfigWindow()
    {
        try
        {
            new AlertConfigWindow().Show();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开推送配置失败: {ex.Message}");
        }
    }

    public void ShowKeyMappingWindow()
    {
        var owner = GetMainWindow();
        if (owner == null) return;
        var keyMappingWindow = new KeyMappingWindow();
        keyMappingWindow.ShowDialog(owner);
    }

    public void ShowScriptSyntaxWindow()
    {
        var textBox = new TextBox
        {
            Text = Resources.scriptdoc,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Microsoft YaHei UI, Consolas"),
            FontSize = 13,
            Padding = new global::Avalonia.Thickness(12)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Disabled);

        var window = new Window
        {
            Title = "脚本语法",
            Width = 820,
            Height = 640,
            MinWidth = 520,
            MinHeight = 360,
            Content = textBox
        };
        window.Show();
    }

    private static Window? GetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}