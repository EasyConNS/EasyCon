using EasyCon.Core.Config;
using EasyCon.Core.Logging;
using EasyCon.Core.Services;
using System.Reflection;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Services;

public class ConfigService : IConfigService
{
    private readonly ILogService _logService;
    private ConfigState _config;
    private KeyMappingConfig _keyMapping;
    private AlertDispatcher _alertDispatcher;

    public ConfigState Config => _config;
    public KeyMappingConfig KeyMapping => _keyMapping;

    public ConfigService(ILogService logService)
    {
        _logService = logService;
        _config = LoadOrCreate(() => ConfigManager.LoadConfig());
        _keyMapping = LoadOrCreate(() => ConfigManager.LoadKeyMapping());
        _alertDispatcher = new AlertDispatcher(ConfigManager.LoadAlert());

        // 只订阅一次：此前在每次 DispatchAlert 里重复 += 且从不退订，
        // 导致推送结果按调用次数翻倍打印
        _alertDispatcher.OnResult += (_, result) => _logService.Print(result, true);
    }

    public void Save()
    {
        ConfigManager.SaveConfig(_config);
        ConfigManager.SaveKeyMapping(_keyMapping);
    }

    public void UpdateKeyMapping(KeyMappingConfig keyMapping)
    {
        _keyMapping = keyMapping;
        Save();
    }

    public void UpdateConfig(Action<ConfigState> update)
    {
        update(_config);
        Save();
    }

    public void DispatchAlert(string message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _alertDispatcher.DispatchAsync(message);
            }
            catch (Exception e)
            {
                _logService.Print($"推送失败:{e.Message}", true);
            }
        });
    }

    public async Task<string?> CheckForUpdate()
    {
        try
        {
            var data = await _updateClient.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags");
            var ver = JsonSerializer.Deserialize<VerInfo[]>(data);
            if (ver == null || ver.Length == 0) return null;

            // tag 名不保证是合法版本号（v 前缀/任意命名），逐条 TryParse 过滤，
            // 单条坏 tag 不能让整个更新检查失效；取有效值中最大的
            Version? latest = null;
            foreach (var info in ver)
            {
                if (TryParseTagVersion(info.name) is { } v && (latest == null || v > latest))
                    latest = v;
            }
            if (latest == null) return null;

            var cur = Assembly.GetExecutingAssembly()?.GetName().Version ?? new Version();
            return latest > cur ? $"发现新版本{latest}，快去群文件里看看吧" : "暂时没有发现新版本";
        }
        catch (Exception ex)
        {
            // 网络故障与「无更新」同返回 null：调用方语义上等价（都保持静默），
            // 但故障原因必须可归因——送诊断通道
            CoreLog.Info($"更新检查失败: {ex.Message}");
            return null;
        }
    }

    internal static Version? TryParseTagVersion(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var s = name.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? name[1..] : name;
        return Version.TryParse(s, out var v) ? v : null;
    }

    private static T LoadOrCreate<T>(Func<T> loader) where T : new()
    {
        // 只兜 loader 自身的构造异常；文件损坏路径由 ConfigManager.Load
        // 的「备份 + ConfigErrorReported 上报」链路处理，此处再吞会绕过上报
        try { return loader(); }
        catch (Exception ex)
        {
            CoreLog.Warn($"配置装载异常，改用默认值: {ex.Message}");
            return new T();
        }
    }

    private record VerInfo
    {
        public string name { get; set; } = "";
    }

    /// <summary>共享实例：每次 new HttpClient 会耗尽可用端口（TIME_WAIT 堆积）。</summary>
    private static readonly HttpClient _updateClient = new() { Timeout = TimeSpan.FromSeconds(5) };
}