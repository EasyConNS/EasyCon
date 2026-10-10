using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.AlertConfig;
using EasyCon2.Avalonia.Core.QQ;
using System.Text.Json;
using AlertConfigType = EasyCon.Core.Config.AlertConfig;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class AlertConfigViewModelTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SavingWebhookChangePreservesUnreadableCiphertextUnlessExplicitlyForgotten(bool forgetSecret)
    {
        string directory = Directory.CreateTempSubdirectory("easycon-qq-editor-secret-tests-").FullName;
        string path = Path.Combine(directory, "alert.json");
        try
        {
            const string ciphertext = "synthetic-unreadable-ciphertext";
            AlertConfigType original = ConfigManager.CreateDefaultAlert();
            AlertItem qq = original.alerts.Single(item => item.IsQq);
            qq.qq = new QQNotificationSettings
            {
                app_id = "app",
                user_openid = "user",
                remember_secret = true,
                protected_secret = ciphertext,
            };
            ConfigManager.SaveAlert(original, path);
            AlertConfigType loaded = ConfigManager.LoadAlert(path);
            Assert.That(loaded.alerts.Single(item => item.IsQq).qq!.load_error, Is.Not.Empty);
            using AlertConfigViewModel model = new(config => ConfigManager.SaveAlert(config, path));
            model.Load(loaded);
            QQNotificationViewModel qqModel = model.VisibleItems.Single(item => item.IsQq).Qq!;
            Assert.That(qqModel.CanRememberSecret, Is.True, "允许取消导入配置的记住密钥状态。");
            if (forgetSecret)
                qqModel.RememberSecret = false;
            model.VisibleItems[0].Token = "updated-webhook-token";
            Assert.That(model.Save(), Is.True);

            using JsonDocument stored = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement storedQq = stored.RootElement.GetProperty("alerts")[3].GetProperty("qq");
            Assert.Multiple(() =>
            {
                Assert.That(storedQq.GetProperty("protected_secret").GetString(), Is.EqualTo(forgetSecret ? "" : ciphertext));
                Assert.That(storedQq.GetProperty("remember_secret").GetBoolean(), Is.EqualTo(!forgetSecret));
                Assert.That(stored.RootElement.GetProperty("alerts")[0].GetProperty("token").GetString(), Is.EqualTo("updated-webhook-token"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EditingCredentialsClearsPreviousCiphertext(bool changeAppId)
    {
        AlertItem original = AlertItem.CreateQq();
        original.qq = new QQNotificationSettings
        {
            app_id = "app",
            secret = changeAppId ? "" : "previous-secret",
            protected_secret = "previous-ciphertext",
            remember_secret = true,
        };
        AlertConfigType? saved = null;
        using AlertConfigViewModel model = new(config => saved = config);
        model.Load(new AlertConfigType { alerts = [original] });
        QQNotificationViewModel qq = model.VisibleItems.Single().Qq!;
        if (changeAppId)
            qq.AppId = "other-app";
        else
            qq.Secret = "";
        Assert.That(model.Save(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.alerts.Single().qq!.protected_secret, Is.Empty);
            Assert.That(original.qq.protected_secret, Is.EqualTo("previous-ciphertext"));
        });
    }

    [Test]
    public void QqEditsAndDeletion_AreAppliedOnlyByGlobalSave()
    {
        AlertConfigType original = ConfigManager.CreateDefaultAlert();
        AlertItem originalQq = original.alerts.Single(item => item.IsQq);
        originalQq.qq = new QQNotificationSettings { app_id = "app", secret = "secret", user_openid = "user" };
        AlertConfigType? saved = null;
        int saveCount = 0;
        using AlertConfigViewModel model = new(config => { saved = config; saveCount++; });
        model.Load(original);
        AlertItemViewModel qq = model.VisibleItems.Single(item => item.IsQq);
        qq.Enable = true;
        qq.Qq!.AttachImage = false;
        qq.Name = "我的 QQ";
        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.Null);
            Assert.That(originalQq.enable, Is.False);
            Assert.That(originalQq.name, Is.EqualTo("qq bot"));
            Assert.That(originalQq.qq.attach_image, Is.True);
        });
        Assert.That(model.Save(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(saveCount, Is.EqualTo(1));
            Assert.That(saved!.alerts.Count, Is.EqualTo(4));
            Assert.That(saved.alerts.Single(item => item.IsQq).enable, Is.True);
            Assert.That(saved.alerts.Single(item => item.IsQq).qq!.attach_image, Is.False);
            Assert.That(saved.timeout, Is.EqualTo(original.timeout));
        });

        qq.DeleteCommand.Execute(null);
        Assert.That(saved!.alerts.Any(item => item.IsQq), Is.True);
        Assert.That(model.Save(), Is.True);
        Assert.That(saved.alerts.Any(item => item.IsQq), Is.False);
    }

    [Test]
    public void DiscardingEditor_DoesNotApplyEditsOrDeleteOriginalItems()
    {
        AlertConfigType original = ConfigManager.CreateDefaultAlert();
        int saves = 0;
        using (AlertConfigViewModel model = new(_ => saves++))
        {
            model.Load(original);
            AlertItemViewModel qq = model.VisibleItems.Single(item => item.IsQq);
            qq.Qq!.AppId = "unsaved-app";
            qq.Qq.Secret = "unsaved-secret";
            model.VisibleItems[0].Token = "unsaved-token";
            qq.DeleteCommand.Execute(null);
        }
        Assert.Multiple(() =>
        {
            Assert.That(saves, Is.Zero);
            Assert.That(original.alerts.Count, Is.EqualTo(4));
            Assert.That(original.alerts[0].token, Is.Empty);
            Assert.That(original.alerts.Single(item => item.IsQq).qq!.app_id, Is.Empty);
        });
    }

    [Test]
    public void AddingQq_UsesSameListAndRequiresAllSelectedRecipientsBeforeEnabling()
    {
        AlertConfigType? saved = null;
        using AlertConfigViewModel model = new(config => saved = config);
        model.Load(new AlertConfigType { timeout = 12 });
        model.AddItemCommand.Execute("qq");
        AlertItemViewModel qq = model.VisibleItems.Single();
        Assert.That(qq.IsExpanded && qq.IsQq && qq.Qq!.AttachImage, Is.True);
        Assert.That(model.Save(), Is.True, "禁用的 QQ 配置不要求 URL、凭据或绑定。");
        qq.Qq!.AppId = "app";
        qq.Qq.Secret = "secret";
        qq.Qq.UserOpenId = "user";
        qq.Qq.GroupEnabled = true;
        qq.Enable = true;
        Assert.That(model.Save(), Is.False);
        Assert.That(model.ErrorMessage, Does.Contain("所有勾选的接收方"));
        qq.Qq.GroupOpenId = "group";
        Assert.That(model.Save(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.alerts.Single().qq!.Targets().Count, Is.EqualTo(2));
            Assert.That(saved.alerts.Single().id, Is.Not.Empty);
            Assert.That(saved.timeout, Is.EqualTo(12));
        });
    }

    [Test]
    public void BusyQq_DisablesGlobalSaveAndCredentialChangesClearTheSharedSwitch()
    {
        AlertItem qqItem = AlertItem.CreateQq();
        qqItem.enable = true;
        qqItem.qq = new QQNotificationSettings { app_id = "app", secret = "secret", user_openid = "user" };
        int saves = 0;
        using AlertConfigViewModel model = new(_ => saves++);
        model.Load(new AlertConfigType { alerts = [qqItem] });
        AlertItemViewModel qq = model.VisibleItems.Single();
        List<string?> changed = [];
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        qq.Qq!.IsBusy = true;
        Assert.Multiple(() =>
        {
            Assert.That(model.CanSave, Is.False);
            Assert.That(qq.CanEdit, Is.False);
            Assert.That(model.Save(), Is.False);
            Assert.That(saves, Is.Zero);
            Assert.That(changed, Does.Contain(nameof(model.CanSave)));
        });
        qq.Qq.IsBusy = false;
        qq.Qq.Secret = "other-secret";
        Assert.Multiple(() =>
        {
            Assert.That(model.CanSave, Is.True);
            Assert.That(qq.Enable, Is.False);
            Assert.That(qq.Qq.Enabled, Is.False);
        });
    }

    [Test]
    public void MigratedSixthQq_CanBeShownEditedDisabledAndDeletedWithoutLosingExistingItems()
    {
        AlertItem qqItem = AlertItem.CreateQq();
        qqItem.enable = true;
        qqItem.qq = new QQNotificationSettings { app_id = "app", secret = "secret", user_openid = "user" };
        AlertConfigType original = new()
        {
            schema_version = 1,
            alerts = Enumerable.Range(0, 5).Select(i => new AlertItem
            {
                name = "已有推送-" + i,
                url = "https://example.com/notify",
                token = "token-" + i,
            }).Append(qqItem).ToList(),
        };
        AlertConfigType? saved = null;
        using AlertConfigViewModel model = new(config => saved = config);
        model.Load(original);
        Assert.Multiple(() =>
        {
            Assert.That(model.HiddenCount, Is.EqualTo(1));
            Assert.That(model.ShowAllItemsCommand.CanExecute(null), Is.True);
        });
        model.ShowAllItemsCommand.Execute(null);
        AlertItemViewModel qq = model.VisibleItems.Single(item => item.IsQq);
        Assert.Multiple(() =>
        {
            Assert.That(model.VisibleItems.Count, Is.EqualTo(6));
            Assert.That(model.HasHiddenItems, Is.False);
            Assert.That(model.ShowAllItemsCommand.CanExecute(null), Is.False);
            Assert.That(model.CanAdd, Is.False);
            Assert.That(qq.Enable, Is.True);
        });
        qq.Enable = false;
        qq.Name = "已迁入的 QQ";
        Assert.That(model.Save(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.alerts.Count, Is.EqualTo(6));
            Assert.That(saved.alerts.Last().enable, Is.False);
            Assert.That(saved.alerts.Last().name, Is.EqualTo("已迁入的 QQ"));
            Assert.That(qqItem.enable, Is.True);
        });
        qq.DeleteCommand.Execute(null);
        Assert.That(model.Save(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.alerts.Any(item => item.IsQq), Is.False);
            Assert.That(saved.alerts.Select(item => item.token), Is.EqualTo(original.alerts.Take(5).Select(item => item.token)));
            Assert.That(saved.alerts.Select(item => item.name), Is.EqualTo(original.alerts.Take(5).Select(item => item.name)));
        });
    }

    [Test]
    public void MigrationWarning_IsDisplayedAndClearedAfterSuccessfulSave()
    {
        AlertConfigType config = ConfigManager.CreateDefaultAlert();
        config.load_error = "推送配置迁移未能保存，请稍后重新保存。";
        AlertConfigType? saved = null;
        using AlertConfigViewModel model = new(next => saved = next);
        model.Load(config);
        Assert.Multiple(() =>
        {
            Assert.That(model.HasError, Is.True);
            Assert.That(model.ErrorMessage, Is.EqualTo(config.load_error));
        });
        Assert.That(model.Save(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(model.HasError, Is.False);
            Assert.That(model.ErrorMessage, Is.Empty);
            Assert.That(saved!.load_error, Is.Empty);
        });
    }

    [Test]
    public void SaveFailure_KeepsTheEditorOpenAndDisplaysError()
    {
        bool closed = false;
        using AlertConfigViewModel model = new(_ => throw new IOException("磁盘不可写"))
        {
            OnSaveCallback = () => closed = true,
        };
        model.Load(ConfigManager.CreateDefaultAlert());
        Assert.Multiple(() =>
        {
            Assert.That(model.Save(), Is.False);
            Assert.That(closed, Is.False);
            Assert.That(model.ErrorMessage, Does.Contain("磁盘不可写"));
        });
    }
}