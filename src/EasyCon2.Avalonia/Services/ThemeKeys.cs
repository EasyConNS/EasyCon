namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 主题持久化键。config.json 存稳定 key（不再把中文显示名当协议写入配置），
/// 读取时把 key 与历史中文名都映射回当前显示名。
/// </summary>
public static class ThemeKeys
{
    // 配色方案
    public const string SchemeLight = "light";
    public const string SchemeWarm = "warm";
    public const string SchemeDark = "dark";

    // 外观样式
    public const string StyleClassic = "classic";
    public const string StyleCard = "card";

    /// <summary>显示名 → 稳定 key（保存时用）。</summary>
    public static string Persist(string displayName) => displayName switch
    {
        ThemeManager.whiteGraySchemeName => SchemeLight,
        ThemeManager.WarmToneSchemeName => SchemeWarm,
        ThemeManager.DarkModeSchemeName => SchemeDark,
        ThemeManager.ClassicStyleName => StyleClassic,
        ThemeManager.RoundedStyleName => StyleCard,
        _ => displayName
    };

    /// <summary>稳定 key / 旧中文名 → 当前显示名（加载时用）。</summary>
    public static string Restore(string stored) => stored switch
    {
        SchemeLight => ThemeManager.whiteGraySchemeName,
        SchemeWarm => ThemeManager.WarmToneSchemeName,
        SchemeDark => ThemeManager.DarkModeSchemeName,
        StyleClassic => ThemeManager.ClassicStyleName,
        StyleCard => ThemeManager.RoundedStyleName,
        _ => stored
    };
}