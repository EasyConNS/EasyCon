using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EasyCon.Tests.Notifications;

[TestFixture]
public class AlertDispatcherTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task UnifiedDispatcher_UsesEachListItemsEnableSwitch(bool webhookEnabled, bool qqEnabled)
    {
        using NotificationHandler handler = new();
        using HttpClient http = new(handler);
        AlertConfig config = CreateConfig(webhookEnabled, qqEnabled);
        using AlertDispatcher dispatcher = CreateDispatcher(config, http);
        ConcurrentQueue<string> results = new();
        dispatcher.OnResult += (_, result) => results.Enqueue(result);

        await dispatcher.DispatchAsync("完成", "任务标题").WaitAsync(TimeSpan.FromSeconds(5));
        RecordedRequest[] requests = handler.Requests.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(requests.Count(request => request.Uri.Host == "webhook.example"), Is.EqualTo(webhookEnabled ? 1 : 0));
            Assert.That(requests.Count(request => request.Uri.AbsolutePath.EndsWith("/messages")), Is.EqualTo(qqEnabled ? 1 : 0));
            Assert.That(results.Count, Is.EqualTo((webhookEnabled ? 1 : 0) + (qqEnabled ? 1 : 0)));
        });
        if (webhookEnabled)
        {
            RecordedRequest webhook = requests.Single(request => request.Uri.Host == "webhook.example");
            Assert.Multiple(() =>
            {
                Assert.That(webhook.Authorization, Is.EqualTo("Bearer webhook-token"));
                Assert.That(webhook.Body, Does.Contain("%e5%ae%8c%e6%88%90"));
                Assert.That(webhook.Uri.Query, Does.Contain("group=existing"));
                Assert.That(results.Any(result => result.Contains("[自定义推送] 推送成功")), Is.True);
            });
        }
        if (qqEnabled)
        {
            RecordedRequest message = requests.Single(request => request.Uri.AbsolutePath.EndsWith("/messages"));
            using JsonDocument payload = JsonDocument.Parse(message.Body);
            Assert.Multiple(() =>
            {
                Assert.That(message.Uri.AbsolutePath, Is.EqualTo("/v2/users/user/messages"));
                Assert.That(payload.RootElement.GetProperty("content").GetString(), Is.EqualTo("任务标题\n完成"));
                Assert.That(results.Any(result => result.Contains("[QQ 图片通知] QQ 通知发送成功")), Is.True);
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SavingDisabledOrDeletedQq_CancelsQueueWithoutBlockingAlert(bool deleteItem)
    {
        using NotificationHandler handler = new();
        using HttpClient http = new(handler);
        TaskCompletionSource<bool> sending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeResponse = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/messages"))
            {
                sending.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }
        };
        AlertConfig config = CreateConfig(false, true);
        using AlertService service = new(dispatcher: CreateDispatcher(config, http));
        string directory = Directory.CreateTempSubdirectory("easycon-alert-dispatch-tests-").FullName;
        try
        {
            Task dispatch = Task.Run(() => service.Dispatch("第一条"));
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
            service.Dispatch("第二条");
            Assert.That(service.FlushAsync().IsCompleted, Is.False);

            if (deleteItem)
                config.alerts.RemoveAll(item => item.IsQq);
            else
                config.alerts.Single(item => item.IsQq).enable = false;
            ConfigManager.SaveAlert(config, Path.Combine(directory, "alert.json"));
            await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            service.Dispatch("已关闭的第三条");
            await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(handler.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("/messages")), Is.EqualTo(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AlertConfig CreateConfig(bool webhookEnabled, bool qqEnabled)
    {
        AlertItem qq = AlertItem.CreateQq();
        qq.enable = qqEnabled;
        qq.qq = new QQNotificationSettings
        {
            app_id = "app",
            secret = "secret",
            user_openid = "user",
            attach_image = false,
            enabled = !qqEnabled,
        };
        return new AlertConfig
        {
            alerts =
            [
                new AlertItem
                {
                    name = "自定义推送", enable = webhookEnabled, method = "POST", token = "webhook-token",
                    url = "https://webhook.example/notify?group={{group}}",
                    headers = new() { ["Authorization"] = "Bearer {{token}}", ["Content-Type"] = "application/json" },
                    variables = new() { ["group"] = "existing" }, body = "{\"msg\":\"{{content}}\"}",
                },
                qq,
            ],
        };
    }

    private static AlertDispatcher CreateDispatcher(AlertConfig config, HttpClient http) => new(config, http,
        settings => new QQNotificationService(sender: new QQNotificationClient(http), settings: settings, persistSettings: false));

    private sealed record RecordedRequest(Uri Uri, string Body, string? Authorization);

    private sealed class NotificationHandler : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();
        public Func<RecordedRequest, CancellationToken, Task>? BeforeResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RecordedRequest recorded = new(request.RequestUri!,
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString());
            Requests.Enqueue(recorded);
            if (BeforeResponse != null)
                await BeforeResponse(recorded, cancellationToken);
            string response = recorded.Uri.Host == "bots.qq.com"
                ? "{\"access_token\":\"test-token\",\"expires_in\":7200}" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}