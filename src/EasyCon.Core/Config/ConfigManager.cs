using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyCon.Core.Config;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions _jsonReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static ConfigState LoadConfig()
    {
        return Load<ConfigState>(AppPaths.ConfigFile);
    }

    public static void SaveConfig(ConfigState config)
    {
        Save(AppPaths.ConfigFile, config);
    }

    public static KeyMappingConfig LoadKeyMapping()
    {
        return Load<KeyMappingConfig>(AppPaths.KeyMappingConfig);
    }

    public static void SaveKeyMapping(KeyMappingConfig keyMapping)
    {
        Save(AppPaths.KeyMappingConfig, keyMapping);
    }

    public static AlertConfig LoadAlert()
    {
        var path = AppPaths.AlertConfig;
        if (!File.Exists(path))
            GenerateDefaultAlert(path);
        return Load<AlertConfig>(path, _jsonReadOptions);
    }

    public static void SaveAlert(AlertConfig config)
    {
        Save(AppPaths.AlertConfig, config);
    }

    private static T Load<T>(string path, JsonSerializerOptions? options = null) where T : new()
    {
        if (!File.Exists(path))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    private static void Save<T>(string path, T data)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(data, _jsonOptions));
    }

    private static void GenerateDefaultAlert(string path)
    {
        var json = """
{
  "timeout": 10,
  "alerts": [
    {
      "name": "PushPlus",
      "enable": false,
      "url": "https://www.pushplus.plus/send/{{token}}?content={{content}}&title={{title}}",
      "token": ""
    },
    {
      "name": "Bark",
      "enable": false,
      "url": "https://api.day.app/{{token}}/{{title}}/{{content}}?group={{group}}&icon={{icon}}",
      "token": "",
      "variables": {
        "group": "伊机控",
        "icon": "https://avatars.githubusercontent.com/u/107608104?s=48&v=4"
      }
    },
    {
      "name": "自定义Webhook",
      "enable": false,
      "method": "POST",
      "url": "https://example.com/webhook",
      "token": "",
      "headers": {
        "Authorization": "Bearer {{token}}",
        "Content-Type": "application/json"
      },
      "body": "{\"msg\":\"{{content}}\"}",
      "variables": {
        "chat_id": ""
      }
    }
  ]
}
""";
        File.WriteAllText(path, json);
    }
}