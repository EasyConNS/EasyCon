namespace EasyCon2.Theme;

public static class ThemeManager
{
    private static bool _isDark;

    public static bool IsDark => _isDark;

    public static event Action<bool>? ThemeChanged;

    public static void Init(bool isDark) => _isDark = isDark;

    public static void Toggle(bool isDark)
    {
        _isDark = isDark;
        ThemeChanged?.Invoke(isDark);
    }

    // Page / Form background
    public static Color PageBackground => _isDark
        ? Color.FromArgb(20, 20, 19)      // #141413 Near Black
        : Color.FromArgb(242, 241, 237);  // #f2f1ed

    // Panels, table layouts, elevated surfaces
    public static Color SurfaceBackground => _isDark
        ? Color.FromArgb(48, 48, 46)      // #30302e Dark Surface
        : Color.FromArgb(230, 229, 224);  // #e6e5e0

    // Button background
    public static Color ButtonBackground => _isDark
        ? Color.FromArgb(48, 48, 46)      // #30302e
        : Color.FromArgb(235, 234, 229);  // #ebeae5

    // Primary text
    public static Color TextPrimary => _isDark
        ? Color.FromArgb(250, 249, 245)   // #faf9f5 Ivory
        : Color.FromArgb(38, 37, 30);     // #26251e

    // Secondary / muted text
    public static Color TextSecondary => _isDark
        ? Color.FromArgb(176, 174, 165)   // #b0aea5 Warm Silver
        : Color.FromArgb(140, 139, 132);  // #8c8b84

    // Brand accent (same in both modes)
    public static Color Accent => Color.FromArgb(201, 100, 66); // #c96442 Terracotta

    // Success green
    public static Color Success => _isDark
        ? Color.FromArgb(49, 181, 122)    // #31b57a (lighter for dark bg)
        : Color.FromArgb(31, 138, 101);   // #1f8a65

    // Error red
    public static Color Error => Color.FromArgb(207, 45, 86); // #cf2d56

    // Always-dark surfaces (log area, timer label)
    public static Color DarkSurfaceBackground => Color.FromArgb(48, 48, 46);  // #30302e
    public static Color DarkSurfaceText => Color.FromArgb(176, 174, 165);     // #b0aea5

    // White substitute for dark backgrounds
    public static Color WhiteOrLight => _isDark
        ? Color.FromArgb(250, 249, 245)   // #faf9f5
        : Color.White;

    // Menu
    public static Color MenuBackground => _isDark
        ? Color.FromArgb(20, 20, 19)      // #141413
        : Color.FromArgb(242, 241, 237);  // #f2f1ed

    public static Color MenuItemSelected => _isDark
        ? Color.FromArgb(48, 48, 46)      // #30302e
        : Color.FromArgb(235, 234, 229);  // #ebeae5

    public static Color MenuItemBorder => _isDark
        ? Color.FromArgb(61, 61, 58)      // #3d3d3a
        : Color.FromArgb(225, 224, 219);

    public static Color MenuItemPressed => _isDark
        ? Color.FromArgb(61, 61, 58)      // #3d3d3a
        : Color.FromArgb(230, 229, 224);  // #e6e5e0

    public static Color MenuSeparatorDark => _isDark
        ? Color.FromArgb(61, 61, 58)      // #3d3d3a
        : Color.FromArgb(210, 209, 204);

    public static Color MenuSeparatorLight => _isDark
        ? Color.FromArgb(48, 48, 46)      // #30302e
        : Color.FromArgb(235, 234, 229);  // #ebeae5

    public static Color StatusStripBackground => _isDark
        ? Color.FromArgb(20, 20, 19)      // #141413
        : Color.FromArgb(230, 229, 224);  // #e6e5e0

    public static Color MenuDropdownBackground => _isDark
        ? Color.FromArgb(20, 20, 19)      // #141413
        : Color.FromArgb(242, 241, 237);  // #f2f1ed

    public static Color MenuImageMargin => _isDark
        ? Color.FromArgb(26, 26, 24)      // #1a1a18
        : Color.FromArgb(237, 236, 232);  // #edece8
}