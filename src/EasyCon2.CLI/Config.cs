using EasyCon.Core;
using System.Text.Json;

static class ConfigMgr
{
    public static ConfigState Load(string cfgfile = @"config.json")
    {
        if (!File.Exists(cfgfile))
        {
            Console.WriteLine($"配置文件不存在，使用默认配置: {cfgfile}");
            return new ConfigState();
        }
        ConfigState _config = new();
        try
        {
            _config = JsonSerializer.Deserialize<ConfigState>(File.ReadAllText(cfgfile)) ?? new ConfigState();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"配置文件加载失败： {ex.Message}");
        }
        return _config;
    }
}