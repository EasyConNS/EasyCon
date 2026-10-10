#nullable enable

using System.Text.Json;

namespace EasyCon.Core.Config;

public static partial class ConfigManager
{
    private static readonly object _alertLock = new();
    private static readonly Dictionary<(string Path, string Id), QQNotificationSettings> _qqSessions = [];

    public static event Action<AlertConfig>? AlertConfigChanged;

    public static AlertConfig CreateDefaultAlert() => new()
    {
        schema_version = 1,
        alerts =
        [
            new AlertItem
            {
                name = "PushPlus",
                url = "https://www.pushplus.plus/send/{{token}}?content={{content}}&title={{title}}",
            },
            new AlertItem
            {
                name = "Bark",
                url = "https://api.day.app/{{token}}/{{title}}/{{content}}?group={{group}}&icon={{icon}}",
                variables = new() { ["group"] = "伊机控", ["icon"] = "https://avatars.githubusercontent.com/u/107608104?s=48&v=4" },
            },
            new AlertItem
            {
                name = "自定义Webhook",
                method = "POST",
                url = "https://example.com/webhook",
                headers = new() { ["Authorization"] = "Bearer {{token}}", ["Content-Type"] = "application/json" },
                body = "{\"msg\":\"{{content}}\"}",
                variables = new() { ["chat_id"] = "" },
            },
            AlertItem.CreateQq(),
        ],
    };

    public static AlertConfig LoadAlert(string? path = null, string? legacyQqPath = null)
    {
        path = Path.GetFullPath(path ?? AppPaths.AlertConfig);
        lock (_alertLock)
        {
            AlertConfig config;
            try
            {
                config = File.Exists(path)
                    ? JsonSerializer.Deserialize<AlertConfig>(File.ReadAllText(path), _jsonReadOptions) ?? new()
                    : CreateDefaultAlert();
            }
            catch
            {
                // 不覆盖无法解析的旧文件，保留用户手动修复的机会。
                return new AlertConfig { schema_version = 1 };
            }
            bool needsSave = !File.Exists(path) || config.schema_version < 1;
            if (config.schema_version < 1 && !config.alerts.Any(item => item.IsQq))
                config.alerts.Add(AlertItem.CreateQq());
            if (needsSave)
            {
                AlertItem? item = config.alerts.FirstOrDefault(item => item.IsQq);
                string legacyPath = legacyQqPath ?? (path == AppPaths.AlertConfig ? AppPaths.QqNotificationConfig : "");
                if (item != null && File.Exists(legacyPath))
                {
                    try
                    {
                        item.qq = Load<QQNotificationSettings>(legacyPath, _jsonReadOptions);
                        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(legacyPath));
                        item.enable = document.RootElement.TryGetProperty("enabled", out JsonElement enabled)
                            && enabled.ValueKind == JsonValueKind.True;
                    }
                    catch
                    {
                        item.qq = new QQNotificationSettings();
                        item.enable = false;
                    }
                }
                SaveAlert(config, path);
                config = Load<AlertConfig>(path, _jsonReadOptions);
            }
            foreach (AlertItem item in config.alerts.Where(item => item.IsQq))
            {
                QQNotificationSettings settings = item.qq ??= new();
                if (_qqSessions.TryGetValue((path, item.id), out QQNotificationSettings? session)
                    && session.secret.Length > 0 && session.app_id == settings.app_id
                    && session.protected_secret == settings.protected_secret)
                {
                    settings.secret = session.secret;
                    settings.verified = session.verified;
                }
                else if (settings.remember_secret && settings.protected_secret.Length > 0)
                {
                    try
                    {
                        settings.secret = QQNotificationSecretProtector.Unprotect(settings.protected_secret);
                    }
                    catch
                    {
                        settings.load_error = "保存的 QQ AppSecret 无法解密，请重新填写。";
                    }
                }
                settings.enabled = item.enable = item.enable && settings.IsReady();
            }
            return config;
        }
    }

    public static void SaveAlert(AlertConfig config, string? path = null)
    {
        path = Path.GetFullPath(path ?? AppPaths.AlertConfig);
        AlertConfig snapshot = new() { schema_version = 1, timeout = config.timeout, alerts = [.. config.alerts.Select(item => item.Clone())] };
        lock (_alertLock)
        {
            HashSet<string> ids = [];
            foreach (AlertItem item in snapshot.alerts)
            {
                if (item.id.Length == 0 || !ids.Add(item.id))
                {
                    item.id = Guid.NewGuid().ToString("N");
                    ids.Add(item.id);
                }
                if (!item.IsQq)
                    continue;
                QQNotificationSettings settings = item.qq ??= new();
                settings.enabled = item.enable;
                if (settings.secret.Length > 0)
                    settings.protected_secret = settings.remember_secret ? QQNotificationSecretProtector.Protect(settings.secret) : "";
                else if (!settings.remember_secret)
                    settings.protected_secret = "";
            }
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            foreach (var key in _qqSessions.Keys.Where(key => key.Path == path && !ids.Contains(key.Id)).ToArray())
                _qqSessions.Remove(key);
            foreach (AlertItem item in snapshot.alerts.Where(item => item.IsQq))
                _qqSessions[(path, item.id)] = item.qq!.Clone();
        }
        AlertConfigChanged?.Invoke(snapshot);
    }

    public static QQNotificationSettings LoadQqNotification() =>
        LoadAlert().alerts.FirstOrDefault(item => item.IsQq)?.qq ?? new QQNotificationSettings();

    public static void SaveQqNotification(QQNotificationSettings settings)
    {
        AlertConfig config = LoadAlert();
        AlertItem? item = config.alerts.FirstOrDefault(item => item.IsQq);
        if (item == null)
        {
            item = AlertItem.CreateQq();
            config.alerts.Add(item);
        }
        item.qq = settings.Clone();
        item.enable = settings.enabled;
        SaveAlert(config);
    }
}