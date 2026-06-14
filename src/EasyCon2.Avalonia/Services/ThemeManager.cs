using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyCon2.Avalonia.Services;

public sealed partial class ThemeManager : ObservableObject
{
    public const string whiteGraySchemeName = "白色";
    public const string WarmToneSchemeName = "暖色";
    public const string DarkModeSchemeName = "Dark模式";
    public const string ClassicStyleName = "经典";
    public const string RoundedStyleName = "卡片式";
    public const string GlassStyleName = "磨砂玻璃";
    private const double WorkbenchMenuHeight = 34;
    private const double WorkbenchButtonMinHeight = 32;
    private const double WorkbenchCompactButtonMinHeight = 26;
    private const double WorkbenchInputMinHeight = 34;
    private static readonly FontFamily WorkbenchUiFontFamily = new("Inter, Noto Sans CJK SC, WenQuanYi Micro Hei, Microsoft YaHei UI, Microsoft YaHei");
    private static readonly FontFamily WorkbenchMonoFontFamily = new("Cascadia Code, Consolas, DejaVu Sans Mono, Noto Sans Mono CJK SC");

    private static readonly Lazy<ThemeManager> _instance = new(
        () => new ThemeManager(),
        LazyThreadSafetyMode.PublicationOnly
    );
    public static ThemeManager Instance => _instance.Value;

    [ObservableProperty]
    private string _selectedColorSchemeName = WarmToneSchemeName;

    [ObservableProperty]
    private string _selectedThemeStyleName = RoundedStyleName;

    public string[] ColorSchemeNames { get; } = { whiteGraySchemeName, WarmToneSchemeName, DarkModeSchemeName };
    public string[] ThemeStyleNames { get; } = { ClassicStyleName, RoundedStyleName, GlassStyleName };

    public bool IsDarkMode => SelectedColorSchemeName == DarkModeSchemeName;

    public event Action<bool>? DarkModeChanged;

    private ThemeManager()
    {
        ApplyColorScheme(SelectedColorSchemeName);
        ApplyThemeStyle(SelectedThemeStyleName);
    }

    public void ApplyColorScheme(string colorSchemeName)
    {
        var palette = colorSchemeName switch
        {
            whiteGraySchemeName => WorkbenchColorScheme.whiteGray,
            WarmToneSchemeName => WorkbenchColorScheme.WarmTone,
            DarkModeSchemeName => WorkbenchColorScheme.DarkMode,
            _ => null
        };

        if (palette == null)
            return;

        SelectedColorSchemeName = colorSchemeName;
        var settingsCardColor = GetSettingsCardColor(colorSchemeName, SelectedThemeStyleName, palette.Panel);

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
        SetResource("WorkbenchSettingsCardColor", settingsCardColor);
        SetResource("WorkbenchSettingsCardBorderColor", palette.ControlBorder);
        SetResource("WorkbenchDirectionalShadowColor", palette.DirectionalShadow);

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
        SetResource("WorkbenchSettingsCardBrush", new SolidColorBrush(settingsCardColor));
        SetResource("WorkbenchSettingsCardBorderBrush", new SolidColorBrush(palette.ControlBorder));
        SetDirectionalShadowResources(SelectedThemeStyleName == ClassicStyleName, palette.DirectionalShadow);
        SetComboBoxGlyphResources(colorSchemeName, palette);
        DarkModeChanged?.Invoke(IsDarkMode);
    }

    public void ApplyThemeStyle(string themeStyleName)
    {
        var style = themeStyleName switch
        {
            ClassicStyleName => WorkbenchStyle.Classic,
            RoundedStyleName => WorkbenchStyle.Rounded,
            GlassStyleName => WorkbenchStyle.Glass,
            _ => null
        };

        if (style == null)
            return;

        SelectedThemeStyleName = themeStyleName;

        SetResource("WorkbenchControlRadius", style.ControlRadius);
        SetResource("WorkbenchCardRadius", style.CardRadius);
        SetResource("WorkbenchMenuHeight", style.MenuHeight);
        SetResource("WorkbenchButtonMinHeight", style.ButtonMinHeight);
        SetResource("WorkbenchCompactButtonMinHeight", style.CompactButtonMinHeight);
        SetResource("WorkbenchInputMinHeight", style.InputMinHeight);
        SetResource("WorkbenchUiFontFamily", style.UiFontFamily);
        SetResource("WorkbenchMonoFontFamily", style.MonoFontFamily);
        SetResource("WorkbenchSidebarDividerThickness", style.SidebarDividerThickness);
        SetResource("WorkbenchBottomDividerThickness", style.BottomDividerThickness);
        SetResource("WorkbenchTopDividerThickness", style.TopDividerThickness);
        SetResource("WorkbenchEditorBorderThickness", style.EditorBorderThickness);
        SetResource("WorkbenchSettingsCardBorderThickness", style.SettingsCardBorderThickness);
        SetResource("WorkbenchSettingsCardShadow", style.SettingsCardShadow);

        var palette = GetColorScheme(SelectedColorSchemeName);
        if (palette is not null)
        {
            var settingsCardColor = GetSettingsCardColor(SelectedColorSchemeName, themeStyleName, palette.Panel);
            SetResource("WorkbenchSettingsCardColor", settingsCardColor);
            SetResource("WorkbenchSettingsCardBrush", new SolidColorBrush(settingsCardColor));
        }

        SetDirectionalShadowResources(themeStyleName == ClassicStyleName, palette?.DirectionalShadow ?? Colors.Transparent);
    }

    private static WorkbenchColorScheme? GetColorScheme(string colorSchemeName)
    {
        return colorSchemeName switch
        {
            whiteGraySchemeName => WorkbenchColorScheme.whiteGray,
            WarmToneSchemeName => WorkbenchColorScheme.WarmTone,
            DarkModeSchemeName => WorkbenchColorScheme.DarkMode,
            _ => null
        };
    }

    private static Color GetSettingsCardColor(string colorSchemeName, string themeStyleName, Color fallbackColor)
    {
        if (themeStyleName != ClassicStyleName)
            return fallbackColor;

        return colorSchemeName switch
        {
            WarmToneSchemeName => Color.FromRgb(0xFA, 0xF9, 0xF5),
            DarkModeSchemeName => Color.FromRgb(0x17, 0x17, 0x17),
            _ => Color.FromRgb(0xF9, 0xFA, 0xFB)
        };
    }

    private static void SetDirectionalShadowResources(bool isClassicStyle, Color shadowColor)
    {
        var activeShadow = isClassicStyle ? shadowColor : Colors.Transparent;

        SetResource("WorkbenchClassicShadowVisible", isClassicStyle);
        SetResource("WorkbenchShadowToLeftBrush", CreateDirectionalShadowBrush(activeShadow, true));
        SetResource("WorkbenchShadowToRightBrush", CreateDirectionalShadowBrush(activeShadow, true, true));
        SetResource("WorkbenchShadowToTopBrush", CreateDirectionalShadowBrush(activeShadow, false));
    }

    private static LinearGradientBrush CreateDirectionalShadowBrush(Color shadowColor, bool horizontal, bool reverse = false)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = horizontal
                ? new RelativePoint(1, 0, RelativeUnit.Relative)
                : new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(reverse ? shadowColor : Colors.Transparent, 0),
                new GradientStop(reverse ? Colors.Transparent : shadowColor, 1)
            }
        };
    }

    private static void SetComboBoxGlyphResources(string colorSchemeName, WorkbenchColorScheme palette)
    {
        var glyphColor = colorSchemeName == DarkModeSchemeName
            ? palette.ControlBorder
            : palette.Text;
        var glyphBrush = new SolidColorBrush(glyphColor);

        SetResource("ComboBoxDropDownGlyphForeground", glyphBrush);
        SetResource("ComboBoxDropDownGlyphForegroundPointerOver", glyphBrush);
        SetResource("ComboBoxDropDownGlyphForegroundPressed", glyphBrush);
        SetResource("ComboBoxDropDownGlyphForegroundFocused", glyphBrush);
        SetResource("ComboBoxDropDownGlyphForegroundDisabled", glyphBrush);
    }

    private static void SetResource(string key, object value)
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return;

        resources[key] = value;
    }

    private sealed record WorkbenchColorScheme(
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
        Color DirectionalShadow)
    {
        public static WorkbenchColorScheme whiteGray { get; } = new(
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xD6, 0xDA, 0xE0),
            Color.FromRgb(0x2F, 0x33, 0x3A),
            Color.FromRgb(0x6D, 0x73, 0x7C),
            Color.FromRgb(0x35, 0xAE, 0xE2),
            Color.FromRgb(0x35, 0xAE, 0xE2),
            Color.FromRgb(0xE3, 0x6A, 0x54),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF6, 0xF8, 0xFB),
            Color.FromRgb(0x2F, 0x33, 0x3A),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0x8B, 0x00),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF3, 0xF6, 0xFA),
            Color.FromRgb(0xD6, 0xDA, 0xE0),
            Color.FromRgb(0xF5, 0xFB, 0xFF),
            Color.FromRgb(0xC8, 0xCE, 0xD6),
            Color.FromRgb(0xD6, 0xDA, 0xE0),
            Color.FromArgb(0x1A, 0x3E, 0x48, 0x56));

        public static WorkbenchColorScheme WarmTone { get; } = new(
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
            Color.FromRgb(0xCC, 0x78, 0x5C),
            Color.FromRgb(0xC6, 0x45, 0x45),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xEA, 0xE5, 0xDC),
            Color.FromRgb(0x3D, 0x3A, 0x36),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xE8, 0xA5, 0x5A),
            Color.FromRgb(0xFA, 0xF9, 0xF5),
            Color.FromRgb(0xED, 0xE8, 0xE0),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            Color.FromRgb(0xF5, 0xF0, 0xE8),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            Color.FromRgb(0xE6, 0xDF, 0xD8),
            Color.FromArgb(0x16, 0x54, 0x45, 0x36));

        public static WorkbenchColorScheme DarkMode { get; } = new(
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
            Color.FromRgb(0x5B, 0x9B, 0xD7),
            Color.FromRgb(0xEB, 0x8E, 0x90),
            Color.FromRgb(0x20, 0x21, 0x23),
            Color.FromRgb(0x1A, 0x1B, 0x1E),
            Color.FromRgb(0xF0, 0xF0, 0xF3),
            Color.FromRgb(0xF0, 0xF0, 0xF3),
            Color.FromRgb(0xAB, 0x64, 0x00),
            Color.FromRgb(0x20, 0x21, 0x23),
            Color.FromRgb(0x3A, 0x3D, 0x44),
            Color.FromRgb(0x30, 0x34, 0x3A),
            Color.FromRgb(0x17, 0x17, 0x17),
            Color.FromRgb(0x30, 0x34, 0x3A),
            Color.FromRgb(0x24, 0x27, 0x2B),
            Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
    }

    private sealed record WorkbenchStyle(
        CornerRadius ControlRadius,
        CornerRadius CardRadius,
        double MenuHeight,
        double ButtonMinHeight,
        double CompactButtonMinHeight,
        double InputMinHeight,
        FontFamily UiFontFamily,
        FontFamily MonoFontFamily,
        Thickness SidebarDividerThickness,
        Thickness BottomDividerThickness,
        Thickness TopDividerThickness,
        Thickness EditorBorderThickness,
        Thickness SettingsCardBorderThickness,
        BoxShadows SettingsCardShadow)
    {
        public static WorkbenchStyle Classic { get; } = new(
            new CornerRadius(0),
            new CornerRadius(0),
            WorkbenchMenuHeight,
            WorkbenchButtonMinHeight,
            WorkbenchCompactButtonMinHeight,
            WorkbenchInputMinHeight,
            WorkbenchUiFontFamily,
            WorkbenchMonoFontFamily,
            new Thickness(0),
            new Thickness(0),
            new Thickness(0),
            new Thickness(0),
            new Thickness(1),
            BoxShadows.Parse("0 -3 8 -6 #263E4856"));

        public static WorkbenchStyle Rounded { get; } = new(
            new CornerRadius(8),
            new CornerRadius(12),
            WorkbenchMenuHeight,
            WorkbenchButtonMinHeight,
            WorkbenchCompactButtonMinHeight,
            WorkbenchInputMinHeight,
            WorkbenchUiFontFamily,
            WorkbenchMonoFontFamily,
            new Thickness(0, 0, 1, 0),
            new Thickness(0, 0, 0, 1),
            new Thickness(0, 1, 0, 0),
            new Thickness(1),
            new Thickness(1),
            BoxShadows.Parse("0 0 0 0 #00000000"));

        public static WorkbenchStyle Glass { get; } = new(
            new CornerRadius(12),
            new CornerRadius(16),
            WorkbenchMenuHeight,
            WorkbenchButtonMinHeight,
            WorkbenchCompactButtonMinHeight,
            WorkbenchInputMinHeight,
            WorkbenchUiFontFamily,
            WorkbenchMonoFontFamily,
            new Thickness(0, 0, 1, 0),
            new Thickness(0, 0, 0, 1),
            new Thickness(0, 1, 0, 0),
            new Thickness(1),
            new Thickness(1),
            BoxShadows.Parse("0 0 0 0 #00000000"));
    }
}
