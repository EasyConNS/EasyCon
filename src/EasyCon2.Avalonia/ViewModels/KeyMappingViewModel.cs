using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.SDLInput;
using EasyCon2.Avalonia.Core.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace EasyCon2.Avalonia.ViewModels;

/// <summary>
/// 按键映射配置 ViewModel —— 26 行（键帽图标 + Switch 图标）+ NS2 手柄图热区 + 按键捕获。
/// </summary>
public partial class KeyMappingViewModel : ViewModelBase
{
    /// <summary>取消时恢复用的原始配置（当前窗口直接关闭，保留引用以便未来回滚）。</summary>
    private readonly KeyMappingConfig _original;

    /// <summary>加载时生效的配置 —— 用于回写 4 个无行的 D-pad 斜向属性。</summary>
    private readonly KeyMappingConfig _loaded;

    /// <summary>26 行映射（左列 13 行 + 右列 13 行，按构造顺序排列）。</summary>
    public ObservableCollection<KeyMappingRow> Rows { get; } = [];

    /// <summary>当前正在监听按键的行。</summary>
    [ObservableProperty]
    private KeyMappingRow? _listeningRow;

    /// <summary>当前高亮的行（监听中或刚完成绑定）。</summary>
    [ObservableProperty]
    private KeyMappingRow? _highlightedRow;

    /// <summary>当前高亮的手柄 SVG 分组 id（空 = 无高亮）；由 code-behind 转成 CSS。</summary>
    [ObservableProperty]
    private string? _highlightedControllerId;

    [ObservableProperty]
    private string _statusText = "点击手柄图上的按钮或列表项设置按键映射（Esc / 退格 清除绑定）";

    public KeyMappingViewModel()
    {
        _original = LoadCurrent();
        _loaded = _original;
        BuildRows();
        LoadFromConfig(_original);
    }

    // ─── 初始化 26 行 ──────────────────────────────────────────

    /// <summary>
    /// 按设计画布固定坐标构建 26 行。
    /// 行容器 x：左列 150（键帽 x=150、Switch x=200），右列 1200（Switch x=1200、键帽 x=1252）。
    /// </summary>
    private void BuildRows()
    {
        // 左列：键帽图标相对偏移 0，Switch 图标相对偏移 50。
        AddRow("L", "L", "ico-btn-l", "l-zl", 150, 118, 0, 50);
        AddRow("ZL", "ZL", "ico-btn-zl", "l-zl", 150, 166, 0, 50);
        AddRow("Minus", "Minus", "ico-btn-minus", "btn-minus", 150, 226, 0, 50);
        AddRow("LSUp", "LS↑", "ico-ls-up", "stick-l", 150, 286, 0, 50);
        AddRow("LSRight", "LS→", "ico-ls-right", "stick-l", 150, 334, 0, 50);
        AddRow("LSDown", "LS↓", "ico-ls-down", "stick-l", 150, 382, 0, 50);
        AddRow("LSLeft", "LS←", "ico-ls-left", "stick-l", 150, 430, 0, 50);
        AddRow("LClick", "LClick", "ico-ls-press", "stick-l", 150, 478, 0, 50);
        AddRow("Up", "↑", "ico-dp-up", "dpad-up", 150, 538, 0, 50);
        AddRow("Right", "→", "ico-dp-right", "dpad-right", 150, 586, 0, 50);
        AddRow("Down", "↓", "ico-dp-down", "dpad-down", 150, 634, 0, 50);
        AddRow("Left", "←", "ico-dp-left", "dpad-left", 150, 682, 0, 50);
        AddRow("Capture", "Capture", "ico-capture", "btn-capture", 150, 742, 0, 50);

        // 右列：Switch 图标相对偏移 0，键帽图标相对偏移 52。
        AddRow("R", "R", "ico-btn-r", "r-zr", 1200, 118, 52, 0);
        AddRow("ZR", "ZR", "ico-btn-zr", "r-zr", 1200, 166, 52, 0);
        AddRow("Plus", "Plus", "ico-btn-plus", "btn-plus", 1200, 226, 52, 0);
        AddRow("X", "X", "ico-face-x", "face-x", 1200, 286, 52, 0);
        AddRow("A", "A", "ico-face-a", "face-a", 1200, 334, 52, 0);
        AddRow("B", "B", "ico-face-b", "face-b", 1200, 382, 52, 0);
        AddRow("Y", "Y", "ico-face-y", "face-y", 1200, 430, 52, 0);
        AddRow("RSUp", "RS↑", "ico-rs-up", "stick-r", 1200, 490, 52, 0);
        AddRow("RSRight", "RS→", "ico-rs-right", "stick-r", 1200, 538, 52, 0);
        AddRow("RSDown", "RS↓", "ico-rs-down", "stick-r", 1200, 586, 52, 0);
        AddRow("RSLeft", "RS←", "ico-rs-left", "stick-r", 1200, 634, 52, 0);
        AddRow("RClick", "RClick", "ico-rs-press", "stick-r", 1200, 682, 52, 0);
        AddRow("Home", "Home", "ico-btn-home", "btn-home", 1200, 742, 52, 0);
    }

    private void AddRow(
        string propertyName,
        string actionName,
        string buttonIcon,
        string controllerId,
        double x,
        double y,
        double keyIconLeft,
        double buttonIconLeft)
    {
        Rows.Add(new KeyMappingRow
        {
            PropertyName = propertyName,
            ActionName = actionName,
            ButtonIcon = buttonIcon,
            ControllerId = controllerId,
            X = x,
            Y = y,
            KeyIconLeft = keyIconLeft,
            ButtonIconLeft = buttonIconLeft,
        });
    }

    // ─── 加载 / 保存 ───────────────────────────────────────────

    private static KeyMappingConfig LoadCurrent()
    {
        // 无文件或旧版（VK）格式文件时回退到 SDL 默认值，且不改写用户文件。
        return KeyMappingStore.Instance.Current;
    }

    private void LoadFromConfig(KeyMappingConfig config)
    {
        Type type = typeof(KeyMappingConfig);
        foreach (KeyMappingRow row in Rows)
        {
            PropertyInfo? prop = type.GetProperty(row.PropertyName);
            if (prop != null)
            {
                row.Scancode = (int)(prop.GetValue(config) ?? 0);
                row.DisplayKey = SdlScancodeMap.ToDisplayName(row.Scancode);
            }
        }

        RecomputeConflicts();
    }

    private KeyMappingConfig BuildConfig()
    {
        // 4 个 D-pad 斜向属性（UpRight / DownRight / UpLeft / DownLeft）没有对应行，
        // 必须原样沿用加载时的配置，保证 KeyMappingConfig 的 30 个属性全部往返一致。
        KeyMappingConfig config = new()
        {
            UpRight = _loaded.UpRight,
            DownRight = _loaded.DownRight,
            UpLeft = _loaded.UpLeft,
            DownLeft = _loaded.DownLeft,
        };

        Type type = typeof(KeyMappingConfig);
        foreach (KeyMappingRow row in Rows)
        {
            PropertyInfo? prop = type.GetProperty(row.PropertyName);
            prop?.SetValue(config, row.Scancode);
        }

        config.SchemaVersion = KeyMappingConfig.CurrentSchemaVersion;
        return config;
    }

    // ─── 命令 ──────────────────────────────────────────────────

    [RelayCommand]
    private void StartListening(KeyMappingRow? row)
    {
        if (row == null) return;

        // 取消之前的监听
        if (ListeningRow != null)
            ListeningRow.IsListening = false;

        ListeningRow = row;
        row.IsListening = true;
        SetHighlight(row);
        StatusText = $"🎯 当前监听: {row.ActionName} — 按下键盘按键 (Esc / 退格 清除绑定)";
    }

    [RelayCommand]
    private void Save()
    {
        KeyMappingConfig config = BuildConfig();
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
        // 与绑定完成一致：结束监听并清除高亮，避免停留在高亮色
        ListeningRow = null;
        SetHighlight(null);
        LoadFromConfig(SdlKeyMappingDefaults.Create());
        StatusText = "已恢复默认按键映射";
    }

    /// <summary>关闭窗口 —— 由 code-behind 订阅 RequestClose 执行 Close()。</summary>
    public bool WasSaved { get; private set; }

    private void CloseWindow()
    {
        WasSaved = true;
        RequestClose?.Invoke();
    }

    public event Action? RequestClose;

    // ─── 高亮 ──────────────────────────────────────────────────

    /// <summary>设置唯一高亮行，并同步手柄 SVG 的高亮分组 CSS。</summary>
    private void SetHighlight(KeyMappingRow? row)
    {
        foreach (KeyMappingRow item in Rows)
            item.IsHighlighted = ReferenceEquals(item, row);

        HighlightedRow = row;
        HighlightedControllerId = row?.ControllerId;
    }

    // ─── 手柄图点击（由 code-behind 调用）──────────────────────

    /// <summary>
    /// 命中手柄 SVG 的某个分组 id 后开始监听。
    /// 分组控件（stick-l / stick-r / controller-dpad / controller-xyab）取组内第一行。
    /// </summary>
    public void StartListeningForController(string elementId)
    {
        KeyMappingRow? row = FindRowForControllerId(elementId);
        if (row == null) return;
        StartListening(row);
    }

    private KeyMappingRow? FindRowForControllerId(string elementId)
    {
        KeyMappingRow? exact = Rows.FirstOrDefault(r => r.ControllerId == elementId);
        if (exact != null) return exact;

        if (elementId == "controller-dpad")
            return Rows.FirstOrDefault(r => r.ControllerId.StartsWith("dpad-"));

        if (elementId == "controller-xyab")
            return Rows.FirstOrDefault(r => r.ControllerId.StartsWith("face-"));

        return null;
    }

    // ─── 按键捕获（由 code-behind 调用）────────────────────────

    /// <summary>
    /// 接收来自 code-behind 的 KeyDown 事件。
    /// ESC / 退格 = 清除绑定；系统保留键与无法映射的按键只提示、不写入，并保持监听。
    /// </summary>
    public void OnKeyDown(Key key)
    {
        if (ListeningRow == null) return;

        if (SdlScancodeMap.IsClearKey(key))
        {
            ApplyBinding(0);
            return;
        }

        if (SdlScancodeMap.IsReservedKey(key))
        {
            StatusText = $"⚠ 「{key}」是系统保留键，不能用于按键映射，请换一个键";
            return;
        }

        int sc = SdlScancodeMap.FromAvaloniaKey(key);
        if (sc < 0)
        {
            StatusText = $"⚠ 「{key}」不支持映射，请换一个键";
            return;
        }

        ApplyBinding(sc);
    }

    /// <summary>写入绑定并结束监听。冲突只标记、不阻止（保证编辑效率）。</summary>
    private void ApplyBinding(int sc)
    {
        KeyMappingRow row = ListeningRow!;
        row.Scancode = sc;
        row.DisplayKey = SdlScancodeMap.ToDisplayName(sc);
        row.IsListening = false;
        ListeningRow = null;

        // 结束监听后必须清除高亮，否则行/手柄分组会停留在高亮色
        SetHighlight(null);

        RecomputeConflicts();

        if (sc == 0)
        {
            StatusText = $"已清除 {row.ActionName} 的绑定";
            return;
        }

        int bound = Rows.Count(r => r.Scancode == sc);
        StatusText = bound > 1
            ? $"⚠ 「{SdlScancodeMap.ToDisplayName(sc)}」已被 {bound} 个动作绑定（红色标记），可继续编辑"
            : $"已将 {row.ActionName} 绑定到「{SdlScancodeMap.ToDisplayName(sc)}」";
    }

    /// <summary>重算冲突：同一扫描码被 2 行以上占用时，相关行全部标记为冲突。</summary>
    private void RecomputeConflicts()
    {
        HashSet<KeyMappingRow> conflicted =
        [
            .. Rows.Where(r => r.Scancode != 0)
                   .GroupBy(r => r.Scancode)
                   .Where(g => g.Count() > 1)
                   .SelectMany(g => g),
        ];

        foreach (KeyMappingRow row in Rows)
            row.IsConflicted = conflicted.Contains(row);
    }
}