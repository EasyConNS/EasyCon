using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyCon2.Avalonia.ViewModels;

public sealed partial class ThemeManager : ViewModelBase
{
    public const string IndustrialGraySchemeName = "工业灰";
    public const string WarmToneSchemeName = "暖色调";
    public const string DarkModeSchemeName = "Dark模式";

    private static readonly Lazy<ThemeManager> _instance = new(
        () => new ThemeManager(),
        LazyThreadSafetyMode.PublicationOnly
    );
    public static ThemeManager Instance => _instance.Value;

    [ObservableProperty]
    private string _selectedColorSchemeName = IndustrialGraySchemeName;

    public string[] ColorSchemeNames { get; } = { IndustrialGraySchemeName, WarmToneSchemeName, DarkModeSchemeName };

    public bool IsDarkMode => SelectedColorSchemeName == DarkModeSchemeName;

    public event Action<bool>? DarkModeChanged;

    private ThemeManager()
    {
        ApplyColorScheme(SelectedColorSchemeName);
    }

    public void ApplyColorScheme(string colorSchemeName)
    {
        var palette = colorSchemeName switch
        {
            IndustrialGraySchemeName => WorkbenchPalette.IndustrialGray,
            WarmToneSchemeName => WorkbenchPalette.WarmTone,
            DarkModeSchemeName => WorkbenchPalette.DarkMode,
            _ => null
        };

        if (palette == null)
            return;

        SelectedColorSchemeName = colorSchemeName;

        SetResource("WorkbenchWindowColor", palette.Window);
        SetResource("WorkbenchTitleColor", palette.Title);
        SetResource("WorkbenchMenuColor", palette.Menu);
        SetResource("WorkbenchPanelColor", palette.Panel);
        SetResource("WorkbenchTreeColor", palette.Tree);
        SetResource("WorkbenchTreeRootColor", palette.TreeRoot);
        SetResource("WorkbenchTreeFolderColor", palette.TreeFolder);
        SetResource("WorkbenchSideColor", palette.Side);
        SetResource("WorkbenchBorderColor", palette.Border);
        SetResource("WorkbenchTextColor", palette.Text);
        SetResource("WorkbenchMutedTextColor", palette.MutedText);
        SetResource("WorkbenchAccentColor", palette.Accent);
        SetResource("WorkbenchRunColor", palette.Run);
        SetResource("WorkbenchDangerColor", palette.Danger);
        SetResource("WorkbenchInputColor", palette.Input);
        SetResource("WorkbenchLogColor", palette.Log);
        SetResource("WorkbenchLogTextColor", palette.LogText);
        SetResource("WorkbenchOnAccentColor", palette.OnAccent);
        SetResource("WorkbenchAmberColor", palette.Amber);
        SetResource("WorkbenchButtonColor", palette.Button);
        SetResource("WorkbenchButtonHoverColor", palette.ButtonHover);
        SetResource("WorkbenchButtonBorderColor", palette.ButtonBorder);
        SetResource("WorkbenchOutlineAccentBackgroundColor", palette.OutlineAccentBackground);
        SetResource("WorkbenchControlBorderColor", palette.ControlBorder);
        SetResource("WorkbenchGroupBorderColor", palette.GroupBorder);

        SetResource("WorkbenchWindowBrush", new SolidColorBrush(palette.Window));
        SetResource("WorkbenchTitleBrush", new SolidColorBrush(palette.Title));
        SetResource("WorkbenchMenuBrush", new SolidColorBrush(palette.Menu));
        SetResource("WorkbenchPanelBrush", new SolidColorBrush(palette.Panel));
        SetResource("WorkbenchTreeBrush", new SolidColorBrush(palette.Tree));
        SetResource("WorkbenchTreeRootBrush", new SolidColorBrush(palette.TreeRoot));
        SetResource("WorkbenchTreeFolderBrush", new SolidColorBrush(palette.TreeFolder));
        SetResource("WorkbenchSideBrush", new SolidColorBrush(palette.Side));
        SetResource("WorkbenchBorderBrush", new SolidColorBrush(palette.Border));
        SetResource("WorkbenchTextBrush", new SolidColorBrush(palette.Text));
        SetResource("WorkbenchMutedTextBrush", new SolidColorBrush(palette.MutedText));
        SetResource("WorkbenchAccentBrush", new SolidColorBrush(palette.Accent));
        SetResource("WorkbenchRunBrush", new SolidColorBrush(palette.Run));
        SetResource("WorkbenchDangerBrush", new SolidColorBrush(palette.Danger));
        SetResource("WorkbenchInputBrush", new SolidColorBrush(palette.Input));
        SetResource("WorkbenchLogBrush", new SolidColorBrush(palette.Log));
        SetResource("WorkbenchLogTextBrush", new SolidColorBrush(palette.LogText));
        SetResource("WorkbenchOnAccentBrush", new SolidColorBrush(palette.OnAccent));
        SetResource("WorkbenchAmberBrush", new SolidColorBrush(palette.Amber));
        SetResource("WorkbenchButtonBrush", new SolidColorBrush(palette.Button));
        SetResource("WorkbenchButtonHoverBrush", new SolidColorBrush(palette.ButtonHover));
        SetResource("WorkbenchButtonBorderBrush", new SolidColorBrush(palette.ButtonBorder));
        SetResource("WorkbenchOutlineAccentBackgroundBrush", new SolidColorBrush(palette.OutlineAccentBackground));
        SetResource("WorkbenchControlBorderBrush", new SolidColorBrush(palette.ControlBorder));
        SetResource("WorkbenchGroupBorderBrush", new SolidColorBrush(palette.GroupBorder));
        SetResource("WorkbenchControlRadius", palette.ControlRadius);
        SetResource("WorkbenchCardRadius", palette.CardRadius);
        SetResource("WorkbenchMenuHeight", palette.MenuHeight);
        SetResource("WorkbenchButtonMinHeight", palette.ButtonMinHeight);
        SetResource("WorkbenchCompactButtonMinHeight", palette.CompactButtonMinHeight);
        SetResource("WorkbenchInputMinHeight", palette.InputMinHeight);
        SetResource("WorkbenchUiFontFamily", palette.UiFontFamily);
        SetResource("WorkbenchMonoFontFamily", palette.MonoFontFamily);
        SetTabShapeResources(colorSchemeName);
        DarkModeChanged?.Invoke(IsDarkMode);
    }

    private static void SetTabShapeResources(string colorSchemeName)
    {
        var useStraightTab = colorSchemeName == IndustrialGraySchemeName;
        var fillGeometry = useStraightTab
            ? "M 0,0 L 88,0 L 100,32 L 0,32 Z"
            : "M 0,0 L 82,0 C 91,0 95,7 97,15 L 100,32 L 0,32 Z";
        var borderGeometry = useStraightTab
            ? "M 0,0 L 88,0 L 100,32"
            : "M 0,0 L 82,0 C 91,0 95,7 97,15 L 100,32";

        SetResource("WorkbenchTabFillGeometry", StreamGeometry.Parse(fillGeometry));
        SetResource("WorkbenchTabBorderGeometry", StreamGeometry.Parse(borderGeometry));
    }

    private static void SetResource(string key, object value)
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return;

        resources[key] = value;
    }

    private sealed record WorkbenchPalette(
        Color Window,
        Color Title,
        Color Menu,
        Color Panel,
        Color Tree,
        Color TreeRoot,
        Color TreeFolder,
        Color Side,
        Color Border,
        Color Text,
        Color MutedText,
        Color Accent,
        Color Run,
        Color Danger,
        Color Input,
        Color Log,
        Color LogText,
        Color OnAccent,
        Color Amber,
        Color Button,
        Color ButtonHover,
        Color ButtonBorder,
        Color OutlineAccentBackground,
        Color ControlBorder,
        Color GroupBorder,
        CornerRadius ControlRadius,
        CornerRadius CardRadius,
        double MenuHeight,
        double ButtonMinHeight,
        double CompactButtonMinHeight,
        double InputMinHeight,
        FontFamily UiFontFamily,
        FontFamily MonoFontFamily)
    {
        public static WorkbenchPalette IndustrialGray { get; } = new(
            Color.FromRgb(0xD0, 0xD1, 0xD6),
            Color.FromRgb(0xF8, 0xF8, 0xFA),
            Color.FromRgb(0xE5, 0xE6, 0xEB),
            Color.FromRgb(0xF0, 0xF1, 0xF5),
            Color.FromRgb(0xF3, 0xF4, 0xF8),
            Color.FromRgb(0xCF, 0xCF, 0xD1),
            Color.FromRgb(0xE2, 0xE2, 0xE4),
            Color.FromRgb(0xC7, 0xC8, 0xCD),
            Color.FromRgb(0x9E, 0xA1, 0xA8),
            Color.FromRgb(0x66, 0x67, 0x6B),
            Color.FromRgb(0x7B, 0x7C, 0x81),
            Color.FromRgb(0x35, 0xAE, 0xE2),
            Color.FromRgb(0x34, 0xC9, 0x5B),
            Color.FromRgb(0xE3, 0x6A, 0x54),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xE0, 0xE1, 0xE6),
            Color.FromRgb(0x33, 0x34, 0x38),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0x8B, 0x00),
            Color.FromRgb(0xF4, 0xF5, 0xF8),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xC1, 0xC3, 0xC9),
            Color.FromRgb(0xF7, 0xFB, 0xFF),
            Color.FromRgb(0xB8, 0xBB, 0xC1),
            Color.FromRgb(0xA7, 0xAA, 0xB1),
            new CornerRadius(0),
            new CornerRadius(0),
            34,
            32,
            26,
            34,
            new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Inter"),
            new FontFamily("Consolas, Microsoft YaHei"));

        public static WorkbenchPalette WarmTone { get; } = new(
            Color.FromRgb(0xFA, 0xF9, 0xF5),
            Color.FromRgb(0xFA, 0xF9, 0xF5),
            Color.FromRgb(0xFA, 0xF9, 0xF5),
            Color.FromRgb(0xF5, 0xF0, 0xE8),
            Color.FromRgb(0xFA, 0xF9, 0xF5),
            Color.FromRgb(0xEF, 0xE9, 0xDE),
            Color.FromRgb(0xF5, 0xF0, 0xE8),
            Color.FromRgb(0xEF, 0xE9, 0xDE),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            Color.FromRgb(0x14, 0x14, 0x13),
            Color.FromRgb(0x6C, 0x6A, 0x64),
            Color.FromRgb(0xCC, 0x78, 0x5C),
            Color.FromRgb(0x5D, 0xB8, 0x72),
            Color.FromRgb(0xC6, 0x45, 0x45),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xEA, 0xE5, 0xDC),
            Color.FromRgb(0x3D, 0x3A, 0x36),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xE8, 0xA5, 0x5A),
            Color.FromRgb(0xFA, 0xF9, 0xF5),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            Color.FromRgb(0xF5, 0xF0, 0xE8),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            new CornerRadius(8),
            new CornerRadius(12),
            40,
            40,
            32,
            40,
            new FontFamily("Inter, Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
            new FontFamily("JetBrains Mono, Consolas, Microsoft YaHei"));

        public static WorkbenchPalette DarkMode { get; } = new(
            Color.FromRgb(0x10, 0x10, 0x11),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x20, 0x21, 0x23),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x20, 0x21, 0x23),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x30, 0x34, 0x3A),
            Color.FromRgb(0xF0, 0xF0, 0xF3),
            Color.FromRgb(0xCC, 0xCC, 0xCC),
            Color.FromRgb(0x5B, 0x9B, 0xD7),
            Color.FromRgb(0x16, 0xA3, 0x4A),
            Color.FromRgb(0xEB, 0x8E, 0x90),
            Color.FromRgb(0x20, 0x21, 0x23),
            Color.FromRgb(0x1A, 0x1B, 0x1E),
            Color.FromRgb(0xF0, 0xF0, 0xF3),
            Color.FromRgb(0xF0, 0xF0, 0xF3),
            Color.FromRgb(0xAB, 0x64, 0x00),
            Color.FromRgb(0x20, 0x21, 0x23),
            Color.FromRgb(0x28, 0x2A, 0x2E),
            Color.FromRgb(0x30, 0x34, 0x3A),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x30, 0x34, 0x3A),
            Color.FromRgb(0x24, 0x27, 0x2B),
            new CornerRadius(8),
            new CornerRadius(8),
            40,
            40,
            32,
            40,
            new FontFamily("Inter, Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
            new FontFamily("JetBrains Mono, Consolas, Microsoft YaHei"));
    }
}