using EasyCon.Core.Config;
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

    [Test]
    public void LegacyConfiguration_MigratesEncryptedQqIntoExistingListOnce()
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
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AlertConfig { timeout = 30, alerts = [webhook] }));
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
            Assert.That(loaded.alerts.Count, Is.EqualTo(2));
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

    [Test]
    public void DefaultList_ContainsQqAndHonorsItsDeletion()
    {
        AlertConfig loaded = ConfigManager.LoadAlert(ConfigPath);
        Assert.That(loaded.alerts.Select(item => item.name),
            Is.EqualTo(new[] { "PushPlus", "Bark", "自定义Webhook", "QQ 图片通知" }));
        loaded.alerts.RemoveAll(item => item.IsQq);
        ConfigManager.SaveAlert(loaded, ConfigPath);
        Assert.That(ConfigManager.LoadAlert(ConfigPath).alerts.Count, Is.EqualTo(3));
    }

    [Test]
    public void UnreadableConfiguration_IsNotOverwrittenDuringLoad()
    {
        const string broken = "{\"alerts\": [未完成的配置";
        File.WriteAllText(ConfigPath, broken);
        Assert.Multiple(() =>
        {
            Assert.That(ConfigManager.LoadAlert(ConfigPath).alerts, Is.Empty);
            Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(broken));
        });
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
}