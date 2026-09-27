using EasyCon.Core.LLM.Mcp;
using EasyCon.Core.LLM.Models;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyCon.Core.Config;

public static class ConfigManager
{
    /// <summary>
    /// models.json 配置保存后触发，用于订阅方（如 AI Agent）刷新内存中的模型列表。
    /// 在保存调用的线程上同步触发；涉及 UI 的订阅方必须自行封送。
    /// </summary>
    public static event Action? ModelsConfigChanged;

    /// <summary>
    /// mcp.json 配置保存后触发，用于订阅方（如 McpManager）差量重连 MCP 服务器。
    /// 在保存调用的线程上同步触发；涉及 UI 的订阅方必须自行封送。
    /// </summary>
    public static event Action? McpConfigChanged;

    /// <summary>
    /// keymapping.json 配置保存后触发，用于订阅方（如 ControllerService）热应用新的按键映射。
    /// 在保存调用的线程上同步触发；订阅方操作非线程安全对象（如 SDL binder）时须自行封送。
    /// </summary>
    public static event Action? KeyMappingChanged;

    /// <summary>
    /// 配置文件损坏或默认配置写入失败时触发，参数为 (文件路径, 描述)。
    /// GUI 启动时应订阅并把消息写入日志，向用户暴露"配置已被重置"的事实。
    /// </summary>
    public static event Action<string, string>? ConfigErrorReported;

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
        KeyMappingChanged?.Invoke();
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

    // ── 测试入口：绕过 AppPaths 静态路径，注入临时目录 ──
    internal static T LoadFrom<T>(string path, JsonSerializerOptions? options = null) where T : new() => Load<T>(path, options);
    internal static void SaveTo<T>(string path, T data) => Save(path, data);
    internal static void WriteDefaultTo(string path, string json) => WriteDefault(path, json);

    private static T Load<T>(string path, JsonSerializerOptions? options = null) where T : new()
    {
        if (!File.Exists(path))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) ?? new T();
        }
        catch (Exception ex)
        {
            // 损坏文件先备份再返回默认值：否则用户打开配置窗口点保存，
            // 会用空白默认值静默覆盖掉原有数据（API Key、供应商等）。
            var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(path, backup, overwrite: true);
            }
            catch
            {
                // 备份失败不阻断加载，消息里如实说明
                backup = path;
            }
            ConfigErrorReported?.Invoke(path, $"配置解析失败，已备份到 {backup} 并改用默认配置: {ex.Message}");
            return new T();
        }
    }

    /// <summary>按路径加锁：并发保存同一配置时串行化共享的 .tmp 临时文件与 Move 替换。</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> SaveLocks = new();

    private static void Save<T>(string path, T data)
    {
        // 原子写：先写临时文件再替换，进程崩溃/断电不会留下截断的配置。
        // 后台线程（如 ControllerService）与 UI 线程可能并发保存同一配置，
        // 无锁时会在同一个 .tmp 上交错，后完成的 Move 抛 FileNotFoundException。
        lock (SaveLocks.GetOrAdd(path, _ => new object()))
        {
            var tmp = $"{path}.tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }

    /// <summary>原子写入默认配置；失败经 <see cref="ConfigErrorReported"/> 上报而非抛出。</summary>
    private static void WriteDefault(string path, string json)
    {
        try
        {
            var tmp = $"{path}.tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            ConfigErrorReported?.Invoke(path, $"默认配置写入失败: {ex.Message}");
        }
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
        WriteDefault(path, json);
    }

    internal static string DefaultModelsJson { get; } = """
{
  "models": {
    "providers": {
      "minicpm": {
        "name": "面壁智能",
        "homePage": "https://modelbest.cn",
        "baseUrl": "https://api.modelbest.cn/v1",
        "apiKey": "sk-REPLACE_WITH_YOUR_KEY",
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

    private static void GenerateDefaultModels(string path)
    {
        WriteDefault(path, DefaultModelsJson);
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
        WriteDefault(path, json);
    }
}