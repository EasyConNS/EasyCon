using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using EasyCon2.Avalonia.Services;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// IWindowService 实现 —— 按键注册表化的单例窗口管理器：各 feature 窗口的
/// 构造工厂由组合根（App）在启动时注册，本类只负责通用的单例生命周期
/// （已存在 → 激活，关闭 → 移除键），从而保持 Services 层零 feature 反向依赖。
/// </summary>
public class WindowService : IWindowService
{
    /// <summary>
    /// 子窗口的 Owner，由 App 在初始化时注入，确保 Z-order 和模态行为正确。
    /// </summary>
    public Window? Owner { get; set; }

    private readonly ILogService _logService;
    private readonly Dictionary<string, Func<Window>> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Window> _open = new(StringComparer.Ordinal);

    public WindowService(ILogService logService)
    {
        _logService = logService;
    }

    public void Register(string key, Func<Window> factory)
    {
        // 同键不同工厂说明组合根装配冲突，启动期快速失败
        if (_factories.TryGetValue(key, out var existing) && existing.Method != factory.Method)
            throw new InvalidOperationException($"窗口键重复注册: {key}");
        _factories[key] = factory;
    }

    public bool Show(string key)
    {
        if (!_factories.TryGetValue(key, out var create))
        {
            _logService.AddLog($"窗口未注册: {key}");
            return false;
        }

        if (_open.TryGetValue(key, out var existing) && existing.IsVisible)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return false;
        }

        try
        {
            var window = create();
            _open[key] = window;
            window.Closed += (_, _) => _open.Remove(key);
            // Show(Window owner) 不接受 null owner（生产环境由 App 先注入 Owner）
            if (Owner is not null)
                window.Show(Owner);
            else
                window.Show();
            return true;
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开窗口({key})失败: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 构造脚本语法帮助窗口（纯 Avalonia 构造，无 feature 依赖，保留在本类）。
    /// </summary>
    public static Window BuildScriptSyntaxWindow()
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
