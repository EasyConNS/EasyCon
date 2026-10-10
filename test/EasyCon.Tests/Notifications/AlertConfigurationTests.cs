using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace EasyCon.Tests.Notifications;

[TestFixture]
public class AlertConfigurationTests
{
    private string _directory = "";
    private string ConfigPath => Path.Combine(_directory, "alert.json");
    private string LegacyPath => Path.Combine(_directory, "qq-notifications.json");

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("easycon-alert-tests-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(false)]
    [TestCase(true)]
    public void UnifiedConfiguration_RoundTripsWebhookAndQqWithoutPlaintextSecrets(bool rememberSecret)
    {
        if (rememberSecret && !OperatingSystem.IsWindows())
            Assert.Ignore("DPAPI 仅支持 Windows。");
        AlertConfig config = ConfigManager.CreateDefaultAlert();
        config.timeout = 25;
        config.alerts[0].enable = true;
        config.alerts[0].token = "webhook-token";
        AlertItem qq = config.alerts.Single(item => item.IsQq);
        qq.enable = true;
        qq.qq = new QQNotificationSettings
        {
            app_id = "app",
            secret = "QQ-test-密钥",
            remember_secret = rememberSecret,
            user_openid = "user",
            verified = true,
        };

        ConfigManager.SaveAlert(config, ConfigPath);
        AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath);
        AlertItem loadedQq = loaded.alerts.Single(item => item.IsQq);
        using JsonDocument file = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        JsonElement savedQq = file.RootElement.GetProperty("alerts")[3].GetProperty("qq");
        Assert.Multiple(() =>
        {
            Assert.That(loaded.timeout, Is.EqualTo(25));
            Assert.That(loaded.alerts.Select(item => item.name), Is.EqualTo(config.alerts.Select(item => item.name)));
            Assert.That(loaded.alerts[0].token, Is.EqualTo("webhook-token"));
            Assert.That(loaded.alerts[2].headers, Is.EqualTo(config.alerts[2].headers));
            Assert.That(loaded.alerts[2].body, Is.EqualTo(config.alerts[2].body));
            Assert.That(loaded.alerts[2].variables, Is.EqualTo(config.alerts[2].variables));
            Assert.That(loaded.alerts.Select(item => item.id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(loadedQq.enable, Is.True);
            Assert.That(loadedQq.qq!.secret, Is.EqualTo("QQ-test-密钥"));
            Assert.That(loadedQq.qq.verified, Is.True);
            Assert.That(loadedQq.qq.attach_image, Is.True);
            Assert.That(savedQq.TryGetProperty("secret", out _), Is.False);
            Assert.That(savedQq.TryGetProperty("verified", out _), Is.False);
            Assert.That(savedQq.TryGetProperty("enabled", out _), Is.False);
            Assert.That(File.ReadAllText(ConfigPath), Does.Not.Contain("QQ-test-密钥"));
            Assert.That(loadedQq.qq.protected_secret.Length > 0, Is.EqualTo(rememberSecret));
            Assert.That(File.Exists(LegacyPath), Is.False);
        });
    }

    [TestCase(1)]
    [TestCase(5)]
    public void LegacyConfiguration_MigratesEncryptedQqIntoExistingListOnce(int existingCount)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("DPAPI 仅支持 Windows。");
        AlertItem webhook = new()
        {
            name = "已有推送",
            enable = true,
            url = "https://example.com/notify",
            token = "token",
            headers = new() { ["X-Existing"] = "value" },
        };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AlertConfig
        {
            timeout = 30,
            alerts = Enumerable.Range(0, existingCount).Select(_ => webhook.Clone()).ToList(),
        }));
        string encrypted = QQNotificationSecretProtector.Protect("legacy-secret");
        string legacy = JsonSerializer.Serialize(new
        {
            app_id = "legacy-app",
            protected_secret = encrypted,
            remember_secret = true,
            user_openid = "legacy-user",
            user_enabled = true,
            enabled = true,
            attach_image = true,
        });
        File.WriteAllText(LegacyPath, legacy);

        AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
        AlertItem qq = loaded.alerts.Single(item => item.IsQq);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.timeout, Is.EqualTo(30));
            Assert.That(loaded.alerts.Count, Is.EqualTo(existingCount + 1));
            Assert.That(loaded.alerts[0].name, Is.EqualTo(webhook.name));
            Assert.That(loaded.alerts[0].token, Is.EqualTo(webhook.token));
            Assert.That(loaded.alerts[0].headers, Is.EqualTo(webhook.headers));
            Assert.That(qq.enable, Is.True);
            Assert.That(qq.qq!.secret, Is.EqualTo("legacy-secret"));
            Assert.That(qq.qq.user_openid, Is.EqualTo("legacy-user"));
            Assert.That(File.ReadAllText(LegacyPath), Is.EqualTo(legacy));
        });

        loaded.alerts.Remove(qq);
        ConfigManager.SaveAlert(loaded, ConfigPath);
        Assert.That(ConfigManager.LoadAlert(ConfigPath, LegacyPath).alerts.Any(item => item.IsQq), Is.False,
            "用户删除 QQ 项后，不应再次导入旧文件。");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MigrationSaveFailure_PreservesUsableConfigurationAndAllowsRetry(bool lockFile)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("本用例使用 Windows 只读属性或文件锁模拟迁移保存失败。");
        AlertConfig original = new()
        {
            timeout = 30,
            alerts = Enumerable.Range(0, 5).Select(i => new AlertItem
            {
                name = "已有推送-" + i,
                enable = true,
                url = "https://example.com/notify",
                token = "existing-token-" + i,
            }).ToList(),
        };
        string originalJson = JsonSerializer.Serialize(original);
        File.WriteAllText(ConfigPath, originalJson);
        File.WriteAllText(LegacyPath, JsonSerializer.Serialize(new
        {
            app_id = "legacy-app",
            protected_secret = QQNotificationSecretProtector.Protect("legacy-secret"),
            remember_secret = true,
            user_openid = "legacy-user",
            enabled = true,
        }));
        int updates = 0;
        void Changed(AlertConfig _) => updates++;
        ConfigManager.AlertConfigChanged += Changed;
        FileStream? locked = null;
        try
        {
            if (lockFile)
                locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
                File.SetAttributes(ConfigPath, FileAttributes.ReadOnly);
            AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            AlertItem qq = loaded.alerts.Single(item => item.IsQq);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.schema_version, Is.EqualTo(1));
                Assert.That(loaded.timeout, Is.EqualTo(30));
                Assert.That(loaded.alerts.Count, Is.EqualTo(6));
                Assert.That(loaded.alerts.Take(5).Select(item => item.token), Is.EqualTo(original.alerts.Select(item => item.token)));
                Assert.That(loaded.alerts.All(item => item.id.Length > 0), Is.True);
                Assert.That(loaded.alerts.Select(item => item.id).Distinct().Count(), Is.EqualTo(6));
                Assert.That(qq.enable, Is.True);
                Assert.That(qq.qq!.secret, Is.EqualTo("legacy-secret"));
                Assert.That(qq.qq.user_openid, Is.EqualTo("legacy-user"));
                Assert.That(loaded.load_error, Does.Contain("迁移未能保存"));
                Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(originalJson));
                Assert.That(updates, Is.Zero);
                Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            });
            Assert.DoesNotThrow(() =>
            {
                using AlertDispatcher dispatcher = new(loaded);
            }, "迁移保存失败不应阻断通知服务初始化。");
            locked?.Dispose();
            locked = null;
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
            ConfigManager.SaveAlert(loaded, ConfigPath);
            AlertConfig reloaded = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.load_error, Is.Empty);
                Assert.That(reloaded.alerts.Count, Is.EqualTo(6));
                Assert.That(reloaded.alerts.Single(item => item.IsQq).enable, Is.True);
                Assert.That(updates, Is.EqualTo(1));
                Assert.That(File.ReadAllText(ConfigPath), Does.Not.Contain("load_error"));
            });
        }
        finally
        {
            locked?.Dispose();
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
            ConfigManager.AlertConfigChanged -= Changed;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetryingMigrationDuringSettingsLoadKeepsRuntimeNotificationsReady(bool lockFile)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("本用例使用 Windows DPAPI 和只读属性或文件锁模拟迁移重试。");
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AlertConfig()));
        File.WriteAllText(LegacyPath, JsonSerializer.Serialize(new
        {
            app_id = "legacy-app",
            protected_secret = QQNotificationSecretProtector.Protect("legacy-secret"),
            remember_secret = true,
            user_openid = "legacy-user",
            enabled = true,
            attach_image = false,
        }));
        FileStream? locked = null;
        AlertConfig? published = null;
        void Changed(AlertConfig config) => published = config;
        ConfigManager.AlertConfigChanged += Changed;
        try
        {
            if (lockFile)
                locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
                File.SetAttributes(ConfigPath, FileAttributes.ReadOnly);
            AlertConfig initial = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            Assert.Multiple(() =>
            {
                Assert.That(initial.load_error, Does.Contain("迁移未能保存"));
                Assert.That(initial.alerts.Single().qq!.IsReady(), Is.True);
                Assert.That(published, Is.Null);
            });

            using MigrationNotificationHandler handler = new();
            using HttpClient http = new(handler);
            using AlertDispatcher dispatcher = new(initial, http, settings => new QQNotificationService(
                client: new QQNotificationClient(http), sender: new QQNotificationClient(http),
                settings: settings, persistSettings: false));
            ConcurrentQueue<string> results = new();
            using AlertService alerts = new(resultLogger: results.Enqueue, dispatcher: dispatcher);
            alerts.Dispatch("迁移前的提醒");
            await alerts.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(handler.MessageCount, Is.EqualTo(1));

            locked?.Dispose();
            locked = null;
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
            AlertConfig editorConfig = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            // 模拟打开推送设置后取消：不再次保存编辑器返回的配置。
            alerts.Dispatch("迁移重试后的提醒");
            await alerts.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(editorConfig.load_error, Is.Empty);
                Assert.That(editorConfig.alerts.Single().enable, Is.True);
                Assert.That(published!.alerts.Single().qq!.secret, Is.EqualTo("legacy-secret"));
                Assert.That(published.alerts.Single().qq!.IsReady(), Is.True);
                Assert.That(handler.MessageCount, Is.EqualTo(2));
                Assert.That(results.Count(result => result.Contains("QQ 通知发送成功")), Is.EqualTo(2));
                Assert.That(results.Any(result => result.Contains("凭据不完整")), Is.False);
                Assert.That(File.ReadAllText(ConfigPath), Does.Not.Contain("legacy-secret"));
            });
        }
        finally
        {
            locked?.Dispose();
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
            ConfigManager.AlertConfigChanged -= Changed;
        }
    }

    [Test]
    public void DefaultList_ContainsQqAndHonorsItsDeletion()
    {
        AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath);
        Assert.That(loaded.alerts.Select(item => item.name),
            Is.EqualTo(new[] { "PushPlus", "Bark", "自定义Webhook", "qq bot" }));
        loaded.alerts.RemoveAll(item => item.IsQq);
        ConfigManager.SaveAlert(loaded, ConfigPath);
        Assert.That(ConfigManager.LoadAlert(ConfigPath).alerts.Count, Is.EqualTo(3));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task RetryingMigrationPreservesActiveAndQueuedNotifications(bool lockFile, bool existingQq)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("本用例使用 Windows DPAPI 和只读属性或文件锁模拟迁移重试。");
        AlertConfig original = new();
        if (existingQq)
            original.alerts.Add(new AlertItem { provider = AlertItem.QqProvider, name = AlertItem.QqDefaultName });
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(original));
        File.WriteAllText(LegacyPath, JsonSerializer.Serialize(new
        {
            app_id = "legacy-app",
            protected_secret = QQNotificationSecretProtector.Protect("legacy-secret"),
            remember_secret = true,
            user_openid = "legacy-user",
            enabled = true,
            attach_image = false,
        }));
        TaskCompletionSource<bool> active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FileStream? locked = null;
        try
        {
            if (lockFile)
                locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
                File.SetAttributes(ConfigPath, FileAttributes.ReadOnly);
            AlertConfig initial = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            using MigrationNotificationHandler handler = new()
            {
                BeforeMessage = async token =>
                {
                    active.TrySetResult(true);
                    try
                    {
                        await release.Task.WaitAsync(token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        canceled.TrySetResult(true);
                        throw;
                    }
                },
            };
            using HttpClient http = new(handler);
            int channels = 0;
            using AlertDispatcher dispatcher = new(initial, http, settings =>
            {
                channels++;
                return new QQNotificationService(client: new QQNotificationClient(http),
                    sender: new QQNotificationClient(http), settings: settings, persistSettings: false);
            });
            ConcurrentQueue<string> results = new();
            using AlertService alerts = new(resultLogger: results.Enqueue, dispatcher: dispatcher);
            alerts.Dispatch("正在发送的提醒");
            await active.Task.WaitAsync(TimeSpan.FromSeconds(5));
            alerts.Dispatch("队列中的提醒");
            Assert.That(alerts.FlushAsync().IsCompleted, Is.False);
            AlertConfig failedRetry = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            Assert.That(failedRetry.alerts.Single().id, Is.EqualTo(initial.alerts.Single().id));

            locked?.Dispose();
            locked = null;
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
            AlertConfig editorConfig = ConfigManager.LoadAlert(ConfigPath, LegacyPath);
            Assert.Multiple(() =>
            {
                Assert.That(editorConfig.alerts.Single().id, Is.EqualTo(initial.alerts.Single().id));
                Assert.That(channels, Is.EqualTo(1));
                Assert.That(canceled.Task.IsCompleted, Is.False);
                Assert.That(alerts.FlushAsync().IsCompleted, Is.False);
            });
            release.TrySetResult(true);
            await alerts.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(handler.Messages, Is.EqualTo(new[] { "伊机控消息\n正在发送的提醒", "伊机控消息\n队列中的提醒" }));
                Assert.That(results.Count(result => result.Contains("QQ 通知发送成功")), Is.EqualTo(2));
                Assert.That(canceled.Task.IsCompleted, Is.False);
            });
        }
        finally
        {
            release.TrySetResult(true);
            locked?.Dispose();
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
        }
    }

    [Test]
    public void CorruptConfiguration_IsBackedUpAndReportedBeforeLaterSave()
    {
        const string broken = "{\"alerts\": [未完成的配置";
        File.WriteAllText(ConfigPath, broken);
        List<string> errors = [];
        void Report(string path, string message)
        {
            if (path == ConfigPath)
                errors.Add(message);
        }
        ConfigManager.ConfigErrorReported += Report;
        try
        {
            AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath);
            string[] backups = Directory.GetFiles(_directory, "alert.json.corrupt-*");
            Assert.Multiple(() =>
            {
                Assert.That(loaded.alerts, Is.Empty);
                Assert.That(loaded.load_error, Does.Contain("解析失败").And.Contain("已备份"));
                Assert.That(errors, Is.EqualTo(new[] { loaded.load_error }));
                Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(broken));
                Assert.That(backups, Has.Length.EqualTo(1));
            });

            ConfigManager.SaveAlert(loaded, ConfigPath);
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(backups.Single()), Is.EqualTo(broken));
                Assert.That(File.ReadAllText(ConfigPath), Does.Not.Contain("load_error"));
                Assert.That(ConfigManager.LoadAlert(ConfigPath).load_error, Is.Empty);
            });
        }
        finally
        {
            ConfigManager.ConfigErrorReported -= Report;
        }
    }

    [Test]
    public void ReadFailure_BlocksSavingUntilReloadAndPreservesOriginalConfiguration()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("本用例使用 Windows 文件锁模拟读取失败。");
        AlertConfig original = ConfigManager.CreateDefaultAlert();
        original.alerts[0].token = "existing-webhook-token";
        ConfigManager.SaveAlert(original, ConfigPath);
        string originalJson = File.ReadAllText(ConfigPath);
        List<string> errors = [];
        void Report(string path, string message)
        {
            if (path == ConfigPath)
                errors.Add(message);
        }
        ConfigManager.ConfigErrorReported += Report;
        try
        {
            AlertConfig loaded;
            using (FileStream locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                loaded = ConfigManager.LoadAlert(ConfigPath);
                Assert.Multiple(() =>
                {
                    Assert.That(loaded.load_error, Does.Contain("读取失败").And.Not.Contain("已备份"));
                    Assert.That(errors, Is.EqualTo(new[] { loaded.load_error }));
                    Assert.That(Directory.GetFiles(_directory, "alert.json.corrupt-*"), Is.Empty);
                });
            }
            // 编辑器会从草稿重建保存对象，不能仅靠返回对象上的错误标志阻止覆盖。
            Assert.Throws<InvalidOperationException>(() => ConfigManager.SaveAlert(new AlertConfig(), ConfigPath));
            Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(originalJson));
            AlertConfig recovered = ConfigManager.LoadAlert(ConfigPath);
            ConfigManager.SaveAlert(recovered, ConfigPath);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.load_error, Is.Empty);
                Assert.That(recovered.alerts[0].token, Is.EqualTo("existing-webhook-token"));
                Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            });
        }
        finally
        {
            ConfigManager.ConfigErrorReported -= Report;
        }
    }

    [Test]
    public void UnreadableEncryptedSecret_DisablesQqAndReportsReentry()
    {
        AlertItem qq = AlertItem.CreateQq();
        qq.enable = true;
        qq.qq = new QQNotificationSettings
        {
            app_id = "app",
            user_openid = "user",
            remember_secret = true,
            protected_secret = "invalid-ciphertext",
        };
        ConfigManager.SaveAlert(new AlertConfig { alerts = [qq] }, ConfigPath);
        AlertItem loaded = ConfigManager.LoadAlert(ConfigPath).alerts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.enable, Is.False);
            Assert.That(loaded.qq!.secret, Is.Empty);
            Assert.That(loaded.qq.load_error, Does.Contain("无法解密"));
        });
    }

    [Test]
    public void ReenteringImportedSecretOnNonWindowsSavesOnlyForCurrentSession()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("本用例验证非 Windows 平台的会话密钥回退。");
        AlertItem qq = AlertItem.CreateQq();
        qq.qq = new QQNotificationSettings
        {
            app_id = "app",
            user_openid = "user",
            remember_secret = true,
            protected_secret = "imported-windows-ciphertext",
        };
        ConfigManager.SaveAlert(new AlertConfig { schema_version = 1, alerts = [qq] }, ConfigPath);
        AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath);
        AlertItem imported = loaded.alerts.Single();
        Assert.That(imported.qq!.load_error, Is.Not.Empty);
        imported.qq.secret = "new-session-secret";
        imported.enable = true;
        Assert.DoesNotThrow(() => ConfigManager.SaveAlert(loaded, ConfigPath));

        string storedText = File.ReadAllText(ConfigPath);
        using JsonDocument stored = JsonDocument.Parse(storedText);
        JsonElement storedQq = stored.RootElement.GetProperty("alerts")[0].GetProperty("qq");
        AlertItem reloaded = ConfigManager.LoadAlert(ConfigPath).alerts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(storedQq.GetProperty("remember_secret").GetBoolean(), Is.False);
            Assert.That(storedQq.GetProperty("protected_secret").GetString(), Is.Empty);
            Assert.That(storedQq.TryGetProperty("secret", out _), Is.False);
            Assert.That(storedText, Does.Not.Contain("new-session-secret"));
            Assert.That(reloaded.qq!.secret, Is.EqualTo("new-session-secret"));
            Assert.That(reloaded.enable, Is.True);
        });
    }

    [Test]
    public void FailedSave_PreservesPreviousFileAndSessionAndDoesNotPublishChanges()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("本用例使用 Windows 文件锁模拟替换失败。");
        AlertItem qq = AlertItem.CreateQq();
        qq.enable = true;
        qq.qq = new QQNotificationSettings { app_id = "app", user_openid = "user", secret = "original-secret" };
        AlertConfig config = new() { alerts = [qq] };
        ConfigManager.SaveAlert(config, ConfigPath);
        string original = File.ReadAllText(ConfigPath);
        config.alerts[0].qq!.secret = "unsaved-secret";
        int updates = 0;
        void Changed(AlertConfig _) => updates++;
        ConfigManager.AlertConfigChanged += Changed;
        try
        {
            using (FileStream locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Exception? error = Assert.Catch(() => ConfigManager.SaveAlert(config, ConfigPath));
                Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(original));
                Assert.That(ConfigManager.LoadAlert(ConfigPath).alerts.Single().qq!.secret, Is.EqualTo("original-secret"));
                Assert.That(updates, Is.Zero);
                Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            });
        }
        finally
        {
            ConfigManager.AlertConfigChanged -= Changed;
        }
    }
    private sealed class MigrationNotificationHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public int MessageCount => Messages.Count;
        public Func<CancellationToken, Task>? BeforeMessage { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
            {
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Messages.Enqueue(body.RootElement.GetProperty("content").GetString()!);
                if (BeforeMessage != null)
                    await BeforeMessage(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.Host == "bots.qq.com"
                    ? "{\"access_token\":\"test-token\",\"expires_in\":7200}" : "{}"),
            };
        }
    }
}