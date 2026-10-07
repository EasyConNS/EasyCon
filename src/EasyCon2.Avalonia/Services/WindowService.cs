using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Shell;
using EasyCon2.Avalonia.Monitoring;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.KeyMapping;
using EasyCon2.Avalonia.Scripting;
using EasyCon2.Avalonia.AlertConfig;
using EasyCon2.Avalonia.Mcp;
using EasyCon2.Avalonia.ModelsConfig;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// IWindowService 实现 —— 负责所有子窗口的创建与生命周期管理。
/// 全部子窗口单实例：重复打开只激活已有窗口，避免多份实例各自持有一份
/// ViewModel 造成保存互相覆盖。
/// </summary>
public class WindowService : IWindowService
{
    /// <summary>
    /// 子窗口的 Owner，由 App 在初始化时注入，确保 Z-order 和模态行为正确。
    /// </summary>
    public Window? Owner { get; set; }

    private readonly IDeviceService _deviceService;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;
    private readonly Dictionary<Type, Window> _openWindows = [];

    public WindowService(IDeviceService deviceService, ILogService logService, IDialogService dialogService)
    {
        _deviceService = deviceService;
        _logService = logService;
        _dialogService = dialogService;
    }

    public void ShowESPConfigWindow()
        => ShowSingleton(() => new ESPConfigWindow
        {
            DataContext = new ESPConfigViewModel(_deviceService, _logService, _dialogService)
        });

    public void ShowAlertConfigWindow()
        => ShowSingleton(() => new AlertConfigWindow());

    public void ShowModelsConfigWindow()
        => ShowSingleton(() => new ModelsConfigWindow());

    public void ShowMcpConfigWindow()
        => ShowSingleton(() => new McpConfigWindow());

    public void ShowKeyMappingWindow()
        => ShowSingleton(() => new KeyMappingWindow { DataContext = new KeyMappingViewModel() });

    public void ShowScriptSyntaxWindow()
        => ShowSingleton(BuildScriptSyntaxWindow);

    private void ShowSingleton<TWindow>(Func<TWindow> create) where TWindow : Window
    {
        var type = typeof(TWindow);
        if (_openWindows.TryGetValue(type, out var existing) && existing.IsVisible)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        try
        {
            var window = create();
            _openWindows[type] = window;
            window.Closed += (_, _) => _openWindows.Remove(type);
            window.Show(Owner);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开 {type.Name} 失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static Window BuildScriptSyntaxWindow()
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

        return new Window
        {
            Title = "脚本语法",
            Width = 820,
            Height = 640,
            MinWidth = 520,
            MinHeight = 360,
            Content = textBox
        };
    }
}