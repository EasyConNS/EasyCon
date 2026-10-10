using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.Core.QQ;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class QQNotificationViewModelTests
{
    [Test]
    public void ChangingCredentialsInvalidatesVerificationAndUpdatesBindingCommands()
    {
        using QQNotificationService service = new(settings: new QQNotificationSettings
        {
            app_id = "app",
            secret = "secret",
            verified = true,
            user_openid = "user",
            enabled = true,
        });
        using QQNotificationViewModel model = new(service);
        List<string?> changed = [];
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        Assert.That(model.BindUserCommand.CanExecute(null), Is.True);
        model.AppId = "other-app";
        Assert.Multiple(() =>
        {
            Assert.That(model.Verified, Is.False);
            Assert.That(model.Enabled, Is.False);
            Assert.That(model.UserOpenId, Is.Empty);
            Assert.That(model.BindUserCommand.CanExecute(null), Is.False);
            Assert.That(changed, Does.Contain(nameof(model.CanBind)));
        });
    }

    [Test]
    public void BusyStateDisablesSetupCommandsAndRefreshesCountdown()
    {
        using QQNotificationService service = new(settings: new QQNotificationSettings
        {
            app_id = "app",
            secret = "secret",
            verified = true,
        });
        using QQNotificationViewModel model = new(service);
        List<string?> changed = [];
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        model.IsBusy = true;
        model.BindingCode = "123456";
        model.BindingSeconds = 59;
        Assert.Multiple(() =>
        {
            Assert.That(model.VerifyCommand.CanExecute(null), Is.False);
            Assert.That(model.SendTestCommand.CanExecute(null), Is.False);
            Assert.That(model.SaveCommand.CanExecute(null), Is.False);
            Assert.That(model.CancelCommand.CanExecute(null), Is.True);
            Assert.That(model.HasBindingCode, Is.True);
            Assert.That(model.BindingCountdownDisplay, Does.Contain("59"));
            Assert.That(changed, Does.Contain(nameof(model.CanEdit)));
            Assert.That(changed, Does.Contain(nameof(model.HasBindingCode)));
            Assert.That(changed, Does.Contain(nameof(model.BindingCountdownDisplay)));
        });
    }
}