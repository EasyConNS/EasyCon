using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.SDLInput;
using System.Collections.ObjectModel;
using System.IO;
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
        // 仅当 keymapping.json 已存在时才加载（由本工具保存的 SDL 扫描码值）
        // 不存在时使用 SdlKeyMappingDefaults 硬编码默认值，避免 WinForms 的 Keys 枚举值
        var path = Path.Combine(AppPaths.ConfigDir, "keymapping.json");
        if (File.Exists(path))
        {
            try { return ConfigManager.LoadKeyMapping(); }
            catch { /* 文件损坏，回退默认值 */ }
        }
        return SdlKeyMappingDefaults.Create();
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
                item.DisplayKey = ScancodeToDisplayName(item.Scancode);
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
        ConfigManager.SaveKeyMapping(config);
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
            sc = KeyToSdlScancode(key);
            if (sc < 0) return; // 不支持的按键，忽略
        }

        // 冲突检测：检查是否已有其他动作绑定了相同按键
        if (sc != 0)
        {
            var conflict = KeyActions.FirstOrDefault(
                k => k != ListeningItem && k.Scancode == sc);
            if (conflict != null)
            {
                StatusText = $"⚠ 按键冲突：「{ScancodeToDisplayName(sc)}」已绑定到 {conflict.ActionName}，请重新选择";
                return;
            }
        }

        ListeningItem.Scancode = sc;
        ListeningItem.DisplayKey = ScancodeToDisplayName(sc);
        ListeningItem.IsListening = false;
        ListeningItem = null;
        StatusText = "点击手柄图上的按钮设置按键映射";
    }

    // ─── SDL 扫描码 ↔ Avalonia Key 映射 ────────────────────────

    /// <summary>Avalonia Key → SDL_Scancode</summary>
    private static int KeyToSdlScancode(Key key)
    {
        return key switch
        {
            Key.A => 4,
            Key.B => 5,
            Key.C => 6,
            Key.D => 7,
            Key.E => 8,
            Key.F => 9,
            Key.G => 10,
            Key.H => 11,
            Key.I => 12,
            Key.J => 13,
            Key.K => 14,
            Key.L => 15,
            Key.M => 16,
            Key.N => 17,
            Key.O => 18,
            Key.P => 19,
            Key.Q => 20,
            Key.R => 21,
            Key.S => 22,
            Key.T => 23,
            Key.U => 24,
            Key.V => 25,
            Key.W => 26,
            Key.X => 27,
            Key.Y => 28,
            Key.Z => 29,
            Key.D1 => 30,
            Key.D2 => 31,
            Key.D3 => 32,
            Key.D4 => 33,
            Key.D5 => 34,
            Key.D6 => 35,
            Key.D7 => 36,
            Key.D8 => 37,
            Key.D9 => 38,
            Key.D0 => 39,
            Key.Return => 40,
            Key.Escape => 41,
            Key.Back => 42,
            Key.Tab => 43,
            Key.Space => 44,
            Key.OemMinus => 45,
            Key.OemPlus => 46,
            Key.OemOpenBrackets => 47,
            Key.OemCloseBrackets => 48,
            Key.OemPipe => 49,
            Key.OemTilde => 50,
            Key.OemSemicolon => 51,
            Key.OemQuotes => 52,
            Key.OemComma => 54,
            Key.OemPeriod => 55,
            Key.OemQuestion => 56,
            Key.CapsLock => 57,
            Key.F1 => 58,
            Key.F2 => 59,
            Key.F3 => 60,
            Key.F4 => 61,
            Key.F5 => 62,
            Key.F6 => 63,
            Key.F7 => 64,
            Key.F8 => 65,
            Key.F9 => 66,
            Key.F10 => 67,
            Key.F11 => 68,
            Key.F12 => 69,
            Key.PrintScreen => 70,
            Key.Scroll => 71,
            Key.Pause => 72,
            Key.Insert => 73,
            Key.Home => 74,
            Key.PageUp => 75,
            Key.Delete => 76,
            Key.End => 77,
            Key.PageDown => 78,
            Key.Right => 79,
            Key.Left => 80,
            Key.Down => 81,
            Key.Up => 82,
            Key.NumLock => 83,
            Key.Divide => 84,
            Key.Multiply => 85,
            Key.Subtract => 86,
            Key.Add => 87,
            Key.NumPad1 => 89,
            Key.NumPad2 => 90,
            Key.NumPad3 => 91,
            Key.NumPad4 => 92,
            Key.NumPad5 => 93,
            Key.NumPad6 => 94,
            Key.NumPad7 => 95,
            Key.NumPad8 => 96,
            Key.NumPad9 => 97,
            Key.NumPad0 => 98,
            Key.Decimal => 99,
            Key.OemBackslash => 100,
            Key.LeftCtrl => 224,
            Key.LeftShift => 225,
            Key.LeftAlt => 226,
            Key.LWin => 227,
            Key.RightCtrl => 228,
            Key.RightShift => 229,
            Key.RightAlt => 230,
            Key.RWin => 231,
            _ => -1,
        };
    }

    /// <summary>SDL_Scancode → 显示名称</summary>
    private static string ScancodeToDisplayName(int sc)
    {
        if (sc == 0) return "—";
        return sc switch
        {
            4 => "A",
            5 => "B",
            6 => "C",
            7 => "D",
            8 => "E",
            9 => "F",
            10 => "G",
            11 => "H",
            12 => "I",
            13 => "J",
            14 => "K",
            15 => "L",
            16 => "M",
            17 => "N",
            18 => "O",
            19 => "P",
            20 => "Q",
            21 => "R",
            22 => "S",
            23 => "T",
            24 => "U",
            25 => "V",
            26 => "W",
            27 => "X",
            28 => "Y",
            29 => "Z",
            30 => "1",
            31 => "2",
            32 => "3",
            33 => "4",
            34 => "5",
            35 => "6",
            36 => "7",
            37 => "8",
            38 => "9",
            39 => "0",
            40 => "Enter",
            41 => "Esc",
            42 => "Back",
            43 => "Tab",
            44 => "Space",
            45 => "-",
            46 => "=",
            47 => "[",
            48 => "]",
            49 => "\\",
            50 => "`",
            51 => ";",
            52 => "'",
            54 => ",",
            55 => ".",
            56 => "/",
            57 => "Caps",
            58 => "F1",
            59 => "F2",
            60 => "F3",
            61 => "F4",
            62 => "F5",
            63 => "F6",
            64 => "F7",
            65 => "F8",
            66 => "F9",
            67 => "F10",
            68 => "F11",
            69 => "F12",
            70 => "PrtSc",
            71 => "ScrLk",
            72 => "Pause",
            73 => "Ins",
            74 => "Home",
            75 => "PgUp",
            76 => "Del",
            77 => "End",
            78 => "PgDn",
            79 => "→",
            80 => "←",
            81 => "↓",
            82 => "↑",
            83 => "NumLk",
            84 => "N/",
            85 => "N*",
            86 => "N-",
            87 => "N+",
            88 => "NEnter",
            89 => "N1",
            90 => "N2",
            91 => "N3",
            92 => "N4",
            93 => "N5",
            94 => "N6",
            95 => "N7",
            96 => "N8",
            97 => "N9",
            98 => "N0",
            99 => "N.",
            100 => "\\",
            224 => "LCtrl",
            225 => "LShift",
            226 => "LAlt",
            227 => "LWin",
            228 => "RCtrl",
            229 => "RShift",
            230 => "RAlt",
            231 => "RWin",
            _ => $"SC{sc}",
        };
    }
}