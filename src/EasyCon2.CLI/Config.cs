using EasyCon.Core;
using System.Text.Json;

static class ConfigMgr
{
    public static ConfigState Load(string cfgfile = @"config.json")
    {
        ConfigState _config = new();
        try
        {
            _config = JsonSerializer.Deserialize<ConfigState>(File.ReadAllText(cfgfile));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"配置文件加载失败： {ex.Message}");
        }
        Console.WriteLine(_config.EnableAlertToken);
        return _config;
    }
}