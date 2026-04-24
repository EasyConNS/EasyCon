using EasyCon.Core.Config;
using System.IO;

namespace EasyCon2.Services;

public class ConfigService
{
    public ConfigState Config { get; private set; } = new();
    public KeyMappingConfig KeyMapping { get; private set; } = new();

    public void Load()
    {
        try
        {
            Config = ConfigManager.LoadConfig();
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            System.Windows.Forms.MessageBox.Show("读取设置文件失败！");
            Config = new();
        }

        try
        {
            KeyMapping = ConfigManager.LoadKeyMapping();
        }
        catch
        {
            KeyMapping = new();
        }
    }

    public void Save()
    {
        ConfigManager.SaveConfig(Config);
    }

    public void SaveKeyMapping()
    {
        ConfigManager.SaveKeyMapping(KeyMapping);
    }

    public void UpdateKeyMapping(KeyMappingConfig mapping)
    {
        KeyMapping = mapping;
        SaveKeyMapping();
    }
}