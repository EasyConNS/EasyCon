using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.SDLInput;
using EasyCon2.Avalonia.Core.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace EasyCon2.Avalonia.ViewModels;

/// <summary>
/// 按键映射配置 ViewModel —— 可视化手柄热区布局 + 按键捕获。
/// </summary>
public partial class KeyMappingViewModel : ViewModelBase
{
    private readonly KeyMappingConfig _original; // 取消时恢复

    /// <summary>28 个 Switch 动作对应的热区</summary>
    public ObservableCollection<KeyActionItem> KeyActions { get; } = [];

    [ObservableProperty]
    private KeyActionItem? _listeningItem;

    [ObservableProperty]
    private string _statusText = "点击手柄图上的按钮设置按键映射";

    public KeyMappingViewModel()
    {
        _original = LoadCurrent();
        InitKeyActions();
        LoadFromConfig(_original);
    }

    // ─── 初始化热区列表 ────────────────────────────────────────

    private void InitKeyActions()
    {
        // 坐标完全复用 WinForms FormKeyMapping.Designer.cs
        KeyActions.Add(new() { ActionName = "ZL", PropertyName = "ZL", X = 280, Y = 134, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "L", PropertyName = "L", X = 280, Y = 185, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "ZR", PropertyName = "ZR", X = 643, Y = 134, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "R", PropertyName = "R", X = 643, Y = 185, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "LS↑", PropertyName = "LSUp", X = 218, Y = 239, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "LS↓", PropertyName = "LSDown", X = 218, Y = 340, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "LS←", PropertyName = "LSLeft", X = 146, Y = 292, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "LS→", PropertyName = "LSRight", X = 290, Y = 292, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "LClick", PropertyName = "LClick", X = 218, Y = 292, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "RS↑", PropertyName = "RSUp", X = 571, Y = 357, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "RS↓", PropertyName = "RSDown", X = 571, Y = 459, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "RS←", PropertyName = "RSLeft", X = 499, Y = 409, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "RS→", PropertyName = "RSRight", X = 643, Y = 409, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "RClick", PropertyName = "RClick", X = 571, Y = 409, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "↑", PropertyName = "Up", X = 352, Y = 358, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "↓", PropertyName = "Down", X = 352, Y = 439, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "←", PropertyName = "Left", X = 303, Y = 395, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "→", PropertyName = "Right", X = 402, Y = 395, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "↗", PropertyName = "UpRight", X = 402, Y = 358, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "↘", PropertyName = "DownRight", X = 402, Y = 439, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "↖", PropertyName = "UpLeft", X = 303, Y = 358, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "↙", PropertyName = "DownLeft", X = 303, Y = 439, Width = 49, Height = 41 });
        KeyActions.Add(new() { ActionName = "A", PropertyName = "A", X = 758, Y = 292, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "B", PropertyName = "B", X = 691, Y = 340, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "X", PropertyName = "X", X = 691, Y = 250, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "Y", PropertyName = "Y", X = 633, Y = 292, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "Plus", PropertyName = "Plus", X = 571, Y = 239, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "Minus", PropertyName = "Minus", X = 365, Y = 241, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "Capture", PropertyName = "Capture", X = 417, Y = 292, Width = 62, Height = 41 });
        KeyActions.Add(new() { ActionName = "Home", PropertyName = "Home", X = 522, Y = 292, Width = 62, Height = 41 });
    }

    // ─── 加载 / 保存 ───────────────────────────────────────────

    private static KeyMappingConfig LoadCurrent()
    {
        // 无文件或旧版（VK）格式文件时回退到 SDL 默认值，且不改写用户文件。
        return KeyMappingStore.Instance.Current;
    }

    private void LoadFromConfig(KeyMappingConfig config)
    {
        var type = typeof(KeyMappingConfig);
        foreach (var item in KeyActions)
        {
            var prop = type.GetProperty(item.PropertyName);
            if (prop != null)
            {
                item.Scancode = (int)(prop.GetValue(config) ?? 0);
                item.DisplayKey = SdlScancodeMap.ToDisplayName(item.Scancode);
            }
        }
    }

    private KeyMappingConfig BuildConfig()
    {
        var config = new KeyMappingConfig();
        var type = typeof(KeyMappingConfig);
        foreach (var item in KeyActions)
        {
            var prop = type.GetProperty(item.PropertyName);
            prop?.SetValue(config, item.Scancode);
        }
        config.SchemaVersion = KeyMappingConfig.CurrentSchemaVersion;
        return config;
    }

    // ─── 命令 ──────────────────────────────────────────────────

    [RelayCommand]
    private void StartListening(KeyActionItem? item)
    {
        if (item == null) return;

        // 取消之前的监听
        if (ListeningItem != null)
            ListeningItem.IsListening = false;

        ListeningItem = item;
        item.IsListening = true;
        StatusText = $"🎯 当前监听: {item.ActionName} — 按下键盘按键 (ESC 清除绑定)";
    }

    [RelayCommand]
    private void Save()
    {
        var config = BuildConfig();
        KeyMappingStore.Instance.Save(config);
        CloseWindow();
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow();
    }

    [RelayCommand]
    private void ResetDefault()
    {
        LoadFromConfig(SdlKeyMappingDefaults.Create());
        StatusText = "已恢复默认按键映射";
    }

    /// <summary>关闭窗口 —— 由 WindowService 在 ShowDialog 返回后检查</summary>
    public bool WasSaved { get; private set; }

    private void CloseWindow()
    {
        WasSaved = true;
        // 通过设置 ListeningItem 为 null 并触发关闭，实际操作由 code-behind 订阅
        RequestClose?.Invoke();
    }

    public event Action? RequestClose;

    // ─── 按键捕获（由 code-behind 调用）────────────────────────

    /// <summary>
    /// 接收来自 code-behind 的 KeyDown 事件。
    /// </summary>
    public void OnKeyDown(Key key)
    {
        if (ListeningItem == null) return;

        int sc;
        if (key == Key.Escape)
        {
            sc = 0; // 清除绑定
        }
        else
        {
            sc = SdlScancodeMap.FromAvaloniaKey(key);
            if (sc < 0) return; // 不支持的按键，忽略
        }

        // 冲突检测：检查是否已有其他动作绑定了相同按键
        if (sc != 0)
        {
            var conflict = KeyActions.FirstOrDefault(
                k => k != ListeningItem && k.Scancode == sc);
            if (conflict != null)
            {
                StatusText = $"⚠ 按键冲突：「{SdlScancodeMap.ToDisplayName(sc)}」已绑定到 {conflict.ActionName}，请重新选择";
                return;
            }
        }

        ListeningItem.Scancode = sc;
        ListeningItem.DisplayKey = SdlScancodeMap.ToDisplayName(sc);
        ListeningItem.IsListening = false;
        ListeningItem = null;
        StatusText = "点击手柄图上的按钮设置按键映射";
    }
}