using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using EasyCon.Core.Notifications;
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
    /// <summary>
    /// 应用主窗口引用，由 App.axaml.cs 在初始化时注入。
    /// 所有子窗口将此作为 Owner，确保 Z-order 和模态行为正确。
    /// </summary>
    public static Window? MainWindow { get; set; }

    private readonly IDeviceService _deviceService;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;
    private readonly QQNotificationService? _qqNotificationService;
    private Window? _espConfigWindow;
    private Window? _qqNotificationWindow;

    public WindowService(IDeviceService deviceService, ILogService logService, IDialogService dialogService,
        QQNotificationService? qqNotificationService = null)
    {
        _deviceService = deviceService;
        _logService = logService;
        _dialogService = dialogService;
        _qqNotificationService = qqNotificationService;
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
            _espConfigWindow.Show(MainWindow);
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
            new AlertConfigWindow().Show(MainWindow);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开推送配置失败: {ex.Message}");
        }
    }

    public void ShowQqNotificationWindow()
    {
        if (_qqNotificationWindow != null)
        {
            if (_qqNotificationWindow.WindowState == WindowState.Minimized)
                _qqNotificationWindow.WindowState = WindowState.Normal;
            _qqNotificationWindow.Activate();
            return;
        }
        if (_qqNotificationService == null)
        {
            _logService.AddLog("QQ 通知服务尚未初始化");
            return;
        }

        try
        {
            _qqNotificationWindow = new QQNotificationWindow(_qqNotificationService);
            _qqNotificationWindow.Closed += (_, _) => _qqNotificationWindow = null;
            _qqNotificationWindow.Show(MainWindow);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开 QQ 通知设置失败: {ex.Message}");
        }
    }

    public void ShowModelsConfigWindow()
    {
        try
        {
            new ModelsConfigWindow().Show(MainWindow);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开模型配置失败: {ex.Message}");
        }
    }

    public void ShowMcpConfigWindow()
    {
        try
        {
            new McpConfigWindow().Show(MainWindow);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开 MCP 配置失败: {ex.Message}");
        }
    }

    public void ShowKeyMappingWindow()
    {
        if (MainWindow == null) return;
        var vm = new ViewModels.KeyMappingViewModel();
        var keyMappingWindow = new KeyMappingWindow { DataContext = vm };
        keyMappingWindow.ShowDialog(MainWindow);
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
        window.Show(MainWindow);
    }
}