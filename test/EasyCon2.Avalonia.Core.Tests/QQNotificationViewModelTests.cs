using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.Core.QQ;
using System.Net;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class QQNotificationViewModelTests
{
    [Test]
    public async Task CancelingReverificationSynchronizesStatusAndDisablesBindingCommands()
    {
        using ReverificationHandler handler = new();
        using HttpClient http = new(handler);
        using QQNotificationClient client = new(http);
        using QQNotificationService service = new(client: client, settings: new QQNotificationSettings
        {
            app_id = "app",
            secret = "secret",
            user_openid = "user",
        }, persistSettings: false);
        using QQNotificationViewModel model = new(service);
        await model.VerifyCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(model.Verified && service.Settings.verified, Is.True);

        Task verification = model.VerifyCommand.ExecuteAsync(null);
        await handler.SecondRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(model.IsBusy, Is.True);
        model.CancelCommand.Execute(null);
        await verification.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(model.Verified, Is.EqualTo(service.Settings.verified).And.False);
            Assert.That(model.VerificationDisplay, Is.EqualTo("尚未验证凭据"));
            Assert.That(model.IsBusy, Is.False);
            Assert.That(model.BindUserCommand.CanExecute(null), Is.False);
            Assert.That(model.BindGroupCommand.CanExecute(null), Is.False);
            Assert.That(model.SendTestCommand.CanExecute(null), Is.False);
            Assert.That(model.VerifyCommand.CanExecute(null), Is.True);
            Assert.That(model.AppId, Is.EqualTo("app"));
            Assert.That(model.Secret, Is.EqualTo("secret"));
        });
    }

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

    private sealed class ReverificationHandler : HttpMessageHandler
    {
        private int _requests;
        public TaskCompletionSource<bool> SecondRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) > 1)
            {
                SecondRequest.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":7200}"),
            };
        }
    }
}