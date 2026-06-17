namespace EasyCon.Core.Config;

public static class AppPaths
{
    private const string _appName = "easycon";

    public static string ConfigDir { get; } = InitDir(Environment.SpecialFolder.ApplicationData);
    public static string DataDir { get; } = InitDir(Environment.SpecialFolder.LocalApplicationData);

    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
    public static string AlertConfig => Path.Combine(ConfigDir, "alert.json");
    public static string KeyMappingConfig => Path.Combine(ConfigDir, "keymapping.json");
    public static string ModelsConfig => Path.Combine(ConfigDir, "models.json");

    public static string CaptureCacheDir { get; } = InitSubDir(DataDir, "cache");

    /// <summary>
    /// 用户级技能目录（热插拔，用户可在不重新编译的前提下扩展 AI 能力）。
    /// </summary>
    public static string UserSkillsDir { get; } = InitSubDir(ConfigDir, "skills");

    private static string InitSubDir(string parent, string name)
    {
        var dir = Path.Combine(parent, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string InitDir(Environment.SpecialFolder folder)
    {
        var dir = Path.Combine(Environment.GetFolderPath(folder), _appName);
        Directory.CreateDirectory(dir);
        return dir;
    }
}