using EasyCon.Core.LLM.Mcp;
using EasyCon.Core.LLM.Models;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyCon.Core.Config;

public static class ConfigManager
{
    /// <summary>
    /// models.json 配置保存后触发，用于订阅方（如 AI Agent）刷新内存中的模型列表。
    /// </summary>
    public static event Action? ModelsConfigChanged;

    /// <summary>
    /// mcp.json 配置保存后触发，用于订阅方（如 McpManager）差量重连 MCP 服务器。
    /// </summary>
    public static event Action? McpConfigChanged;

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

    public static ModelsConfig LoadModelsConfig()
    {
        var path = AppPaths.ModelsConfig;
        if (!File.Exists(path))
            GenerateDefaultModels(path);
        return Load<ModelsConfig>(AppPaths.ModelsConfig, _jsonReadOptions);
    }

    public static void SaveModelsConfig(ModelsConfig config)
    {
        Save(AppPaths.ModelsConfig, config);
        ModelsConfigChanged?.Invoke();
    }

    public static McpConfig LoadMcpConfig()
    {
        var path = AppPaths.McpConfig;
        if (!File.Exists(path))
            GenerateDefaultMcp(path);
        return Load<McpConfig>(path, _jsonReadOptions);
    }

    public static void SaveMcpConfig(McpConfig config)
    {
        Save(AppPaths.McpConfig, config);
        McpConfigChanged?.Invoke();
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

    private static void GenerateDefaultModels(string path)
    {
        var json = """
{
  "models": {
    "providers": {
      "minicpm": {
        "name": "面壁智能",
        "homePage": "https://modelbest.cn",
        "baseUrl": "https://api.modelbest.cn/v1",
        "apiKey": "sk-pQ8L2zF3XmR5kY9wV4jB7hN1tC6vM0xG3aD5sH2bJ9lK4cZ8",
        "api": "openai-completions",
        "models": [
          {
            "id": "MiniCPM-V-4.6-Instruct",
            "name": "MiniCPM-V 4.6",
            "vision": true
          },
          {
            "id": "MiniCPM-V-4.6-Thinking",
            "name": "MiniCPM-V 4.6 Thinking",
            "vision": true
          }
        ]
      }
    }
  }
}
""";
        File.WriteAllText(path, json);
    }

    private static void GenerateDefaultMcp(string path)
    {
        var json = """
{
  "mcp": {
    "servers": {
      "filesystem": {
        "name": "文件系统（示例）",
        "transport": "stdio",
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
        "env": {},
        "enabled": false
      }
    }
  }
}
""";
        File.WriteAllText(path, json);
    }
}