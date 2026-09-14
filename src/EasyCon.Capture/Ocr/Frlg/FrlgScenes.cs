using EzCv;

namespace EasyCon.Capture.Ocr.Frlg;

public sealed record FrlgSceneDefinition(string Key, string Label, string Kind,
    double X, double Y, double Width, double Height, int Minimum = 1, int Maximum = 614)
{
    public bool IsText => Kind is "name" or "nature";
    public bool UsesTextBackend => IsText || Kind == "wild-level";
}

public static class FrlgScenes
{
    public static IReadOnlyList<FrlgSceneDefinition> All { get; } = Array.AsReadOnly(new FrlgSceneDefinition[]
    {
        new("FRLG_JPN_TID", "日版 · 训练家卡 TID", "tid", .712981, .118836, .207212, .077373, 0, 65535),
        new("FRLG_EN_TID", "英文 · 训练家卡 TID", "tid", .742683, .117314, .129734, .076006, 0, 65535),
        // Keep the complete kana row clear of the panel, the ball below, and the following "Lv" glyph.
        new("FRLG_JPN_NAME", "日版 · 野生名称", "name", .0769231, .1125, .224359, .0692308),
        new("FRLG_JPN_SUMMARY_NAME", "日版 · 摘要种族名称", "name", .688942, .210577, .261538, .069231),
        // Include raised dakuten (e.g. さみしがり) without entering the memo heading above.
        new("FRLG_JPN_NATURE", "日版 · 性格", "nature", .048718, .747116, .458205, .070192),
        new("FRLG_JPN_LEVEL", "日版 · 摘要等级", "level", .060256, .116346, .124359, .075962, 2, 100),
        // Include both right-aligned digits with blur margins while excluding the preceding "Lv".
        new("FRLG_JPN_WILD_LEVEL", "日版 · 野生等级（只框数字）", "wild-level", .346154, .120193, .0737179, .0653846, 2, 100),
        new("FRLG_JPN_HP", "日版 · 最大 HP（支持当前/最大）", "hp", .717165, .131662, .269632, .066876, 1, 714),
        new("FRLG_JPN_ATTACK", "日版 · 攻击", "stat", .859615, .243269, .121795, .068269),
        new("FRLG_JPN_DEFENSE", "日版 · 防御", "stat", .859615, .323792, .121795, .068269),
        new("FRLG_JPN_SP_ATTACK", "日版 · 特攻", "stat", .859615, .404315, .121795, .068269),
        new("FRLG_JPN_SP_DEFENSE", "日版 · 特防", "stat", .859615, .484838, .121795, .068269),
        new("FRLG_JPN_SPEED", "日版 · 速度", "stat", .859615, .565361, .121795, .068269),
    });

    public static string BaseKey(string scene) => scene.Split(':', 2)[0];
    public static FrlgSceneDefinition? Find(string scene) => All.FirstOrDefault(s => s.Key == BaseKey(scene));
    public static string[] Targets(string scene) => scene.Contains(':')
        ? scene.Split(':', 2)[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    public static Rect DefaultRegion(string scene, int width, int height)
    {
        FrlgSceneDefinition definition = Find(scene) ?? throw new ArgumentException("Unsupported FRLG scene.", nameof(scene));
        int padding = definition.IsText || definition.Kind == "wild-level" ? 0 : Math.Max(1, (int)Math.Ceiling(4.0 * height / 1080));
        return new Rect((int)((.09375 + definition.X * .8125) * width) - padding,
            (int)((.00462963 + definition.Y * .962963) * height) - padding,
            (int)Math.Round(definition.Width * .8125 * width) + 2 * padding,
            (int)Math.Round(definition.Height * .962963 * height) + 2 * padding);
    }
}