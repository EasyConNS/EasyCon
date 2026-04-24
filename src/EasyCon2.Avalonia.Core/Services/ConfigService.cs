using EasyCon.Core.Config;
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
        Task.Run(async () =>
        {
            try
            {
                _alertDispatcher.OnResult += (_, result) => _logService.Print(result, true);
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
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var data = await client.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags");
            var ver = JsonSerializer.Deserialize<VerInfo[]>(data);
            if (ver == null) return null;

            var info = new VersionParser(ver, Assembly.GetExecutingAssembly()?.GetName().Version);
            return info.IsNewVersion ? $"发现新版本{info.NewVer}，快去群文件里看看吧" : "暂时没有发现新版本";
        }
        catch
        {
            return null;
        }
    }

    private static T LoadOrCreate<T>(Func<T> loader) where T : new()
    {
        try { return loader(); }
        catch { return new T(); }
    }

    private record VerInfo
    {
        public string name { get; set; } = "";
        public Version Ver => new(name ?? "");
    }

    private class VersionParser(VerInfo[] version, Version? cur)
    {
        private readonly VerInfo _ver = version.Last();
        private readonly Version _cur = cur ?? new Version();
        public Version NewVer => _ver.Ver;
        public bool IsNewVersion => _ver.Ver > _cur;
    }
}