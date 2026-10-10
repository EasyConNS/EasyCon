using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.AlertConfig;
using AlertConfigType = EasyCon.Core.Config.AlertConfig;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class AlertConfigViewModelTests
{
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
            Assert.That(originalQq.name, Is.EqualTo("QQ 图片通知"));
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