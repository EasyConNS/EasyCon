using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using EasyCon.Core.Script;
using EasyCon.Tests.Support;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyCon.Tests.Notifications;

[TestFixture]
public class QQNotificationTests
{
    [Test]
    public void Settings_DefaultToImages_AndNeverSerializePlaintextCredentials()
    {
        QQNotificationSettings settings = new() { secret = "test-secret", verified = true };
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        Assert.Multiple(() =>
        {
            Assert.That(settings.attach_image, Is.True);
            Assert.That(json.RootElement.TryGetProperty("secret", out _), Is.False);
            Assert.That(json.RootElement.TryGetProperty("verified", out _), Is.False);
        });
    }

    [Test]
    public void Targets_RequireEveryEnabledRecipient()
    {
        QQNotificationSettings settings = ReadySettings();
        settings.group_enabled = true;
        Assert.That(settings.IsReady(), Is.False);
        Assert.Throws<InvalidOperationException>(() => settings.Targets());
        settings.group_openid = " group-id ";
        Assert.That(settings.Targets(), Is.EqualTo(new[]
        {
            new QQNotificationTarget(QQNotificationTargetKind.User, "user-id"),
            new QQNotificationTarget(QQNotificationTargetKind.Group, "group-id"),
        }));
    }

    [Test]
    public void SecretProtection_RoundTripsUnicodeWithoutStoringPlaintext()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("DPAPI 仅支持 Windows。");
        const string secret = "QQ-test-密钥";
        string encrypted = QQNotificationSecretProtector.Protect(secret);
        Assert.Multiple(() =>
        {
            Assert.That(encrypted, Does.Not.Contain(secret));
            Assert.That(QQNotificationSecretProtector.Unprotect(encrypted), Is.EqualTo(secret));
        });
    }

    [Test]
    public void Image_UsesLogoWithoutCapture_AndPreservesFrameAspectRatio()
    {
        using Mat logo = Cv2.ImDecode(NotificationImage.FromFrame(null), ImreadModes.Color);
        using Mat frame = new(1800, 3200, MatType.CV_8UC3, Scalar.Red);
        using Mat image = Cv2.ImDecode(NotificationImage.FromFrame(frame), ImreadModes.Color);
        Assert.Multiple(() =>
        {
            Assert.That(logo.Empty(), Is.False);
            Assert.That(image.Width, Is.EqualTo(1600));
            Assert.That(image.Height, Is.EqualTo(900));
            Assert.That(frame.Width, Is.EqualTo(3200), "编码不得修改共享的视频帧。");
        });
    }

    [TestCase(QQNotificationTargetKind.User, "/v2/users/")]
    [TestCase(QQNotificationTargetKind.Group, "/v2/groups/")]
    public async Task Client_UploadsCompletePartsInOrder_WithoutSendingTokenToStorage(
        QQNotificationTargetKind kind, string route)
    {
        using StubServer server = new() { BlockSize = 2 };
        using HttpClient http = new(server);
        using QQNotificationClient client = new(http);
        client.Configure("app-id", "test-secret");
        byte[] image = [1, 2, 3, 4, 5];
        await client.SendAsync([new QQNotificationTarget(kind, "receiver/id")], "完成", image);

        Request[] requests = server.Requests.ToArray();
        Request token = requests.Single(request => request.Uri.Host == "bots.qq.com");
        Assert.Multiple(() =>
        {
            Assert.That(token.Json.GetProperty("appId").GetString(), Is.EqualTo("app-id"));
            Assert.That(token.Json.GetProperty("clientSecret").GetString(), Is.EqualTo("test-secret"));
            Assert.That(token.Authorization, Is.Null);
        });
        Request prepared = requests.Single(request => request.Uri.AbsolutePath.EndsWith("/upload_prepare"));
        Assert.Multiple(() =>
        {
            Assert.That(prepared.Uri.AbsolutePath, Is.EqualTo(route + "receiver%2Fid/upload_prepare"));
            Assert.That(prepared.Json.GetProperty("file_size").GetString(), Is.EqualTo("5"));
            Assert.That(prepared.Json.GetProperty("md5").GetString(),
                Is.EqualTo(Convert.ToHexString(MD5.HashData(image)).ToLowerInvariant()));
        });
        Request[] uploads = requests.Where(request => request.Method == HttpMethod.Put).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(uploads.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[] { "/0", "/1", "/2" }));
            Assert.That(uploads[0].Bytes, Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(uploads[1].Bytes, Is.EqualTo(new byte[] { 3, 4 }));
            Assert.That(uploads[2].Bytes, Is.EqualTo(new byte[] { 5 }));
            Assert.That(uploads.All(request => request.Authorization == null), Is.True);
            Assert.That(requests.Where(request => request.Uri.Host == "api.sgroup.qq.com")
                .All(request => request.Authorization == "QQBot test-token"), Is.True);
        });
        Request[] finished = requests.Where(request => request.Uri.AbsolutePath.EndsWith("/upload_part_finish")).ToArray();
        Assert.That(finished.Select(request => request.Json.GetProperty("part_index").GetInt32()), Is.EqualTo(new[] { 0, 1, 2 }));
        for (int i = 0; i < finished.Length; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(finished[i].Json.GetProperty("block_size").GetString(), Is.EqualTo(uploads[i].Bytes.Length.ToString()));
                Assert.That(finished[i].Json.GetProperty("md5").GetString(),
                    Is.EqualTo(Convert.ToHexString(MD5.HashData(uploads[i].Bytes)).ToLowerInvariant()));
            });
        }
        Request merged = requests.Single(request => request.Uri.AbsolutePath.EndsWith("/files"));
        Assert.Multiple(() =>
        {
            Assert.That(merged.Json.GetProperty("file_name").GetString(), Is.EqualTo(prepared.Json.GetProperty("file_name").GetString()));
            Assert.That(merged.Json.GetProperty("upload_id").GetString(), Is.EqualTo("upload-id"));
        });
        Request[] messages = requests.Where(request => request.Uri.AbsolutePath.EndsWith("/messages")).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(messages[0].Json.GetProperty("msg_type").GetInt32(), Is.Zero);
            Assert.That(messages[0].Json.GetProperty("content").GetString(), Is.EqualTo("完成"));
            Assert.That(messages[1].Json.GetProperty("msg_type").GetInt32(), Is.EqualTo(7));
            Assert.That(messages[1].Json.GetProperty("media").GetProperty("file_info").GetString(), Is.EqualTo("file-info"));
        });
    }

    [Test]
    public async Task Client_RejectsDuplicatePartsBeforeUploading_AndReportsPartialSubmission()
    {
        using StubServer server = new() { BlockSize = 2, DuplicateParts = true };
        using HttpClient http = new(server);
        using QQNotificationClient client = new(http);
        client.Configure("app-id", "test-secret");
        Exception? error = null;
        try
        {
            await client.SendAsync([new QQNotificationTarget(QQNotificationTargetKind.User, "user-id")],
                "完成", [1, 2, 3]);
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Message, Does.Contain("分片列表不完整").And.Contain("文字已提交"));
            Assert.That(server.Requests.Any(request => request.Method == HttpMethod.Put), Is.False);
        });
    }

    [Test]
    public async Task Client_CallerCancellationStopsRemainingRecipients()
    {
        using StubServer server = new();
        TaskCompletionSource<bool> sending = NewSignal();
        server.Intercept = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.StartsWith("/v2/users/") && request.Uri.AbsolutePath.EndsWith("/messages"))
            {
                sending.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }
            return null;
        };
        using HttpClient http = new(server);
        using QQNotificationClient client = new(http);
        using CancellationTokenSource cancellation = new();
        client.Configure("app-id", "test-secret");
        Task send = client.SendAsync([
            new QQNotificationTarget(QQNotificationTargetKind.User, "user-id"),
            new QQNotificationTarget(QQNotificationTargetKind.Group, "group-id"),
        ], "完成", null, cancellation.Token);
        try
        {
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(server.Requests.Any(request => request.Uri.AbsolutePath.StartsWith("/v2/groups/")), Is.False);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    [TestCase("/messages", false)]
    [TestCase("/upload_prepare", true)]
    public async Task Client_TimeoutContinuesOtherRecipientsAndReportsSubmission(string timedOutEndpoint, bool textSubmitted)
    {
        using StubServer server = new();
        server.Intercept = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.StartsWith("/v2/users/") && request.Uri.AbsolutePath.EndsWith(timedOutEndpoint))
                await Task.Delay(Timeout.Infinite, token);
            return null;
        };
        using HttpClient http = new(server) { Timeout = TimeSpan.FromMilliseconds(250) };
        using QQNotificationClient client = new(http);
        using CancellationTokenSource caller = new();
        client.Configure("app-id", "test-secret");
        InvalidOperationException? error = null;
        try
        {
            await client.SendAsync([
                new QQNotificationTarget(QQNotificationTargetKind.User, "user-id"),
                new QQNotificationTarget(QQNotificationTargetKind.Group, "group-id"),
            ], "完成", [1, 2, 3], caller.Token).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }
        Request[] groupMessages = server.Requests.Where(request => request.Uri.AbsolutePath.StartsWith("/v2/groups/")
            && request.Uri.AbsolutePath.EndsWith("/messages")).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(caller.IsCancellationRequested, Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Message, Does.Contain("私聊"));
            Assert.That(error.Message.Contains("文字已提交"), Is.EqualTo(textSubmitted));
            Assert.That(groupMessages.Select(request => request.Json.GetProperty("msg_type").GetInt32()), Is.EqualTo(new[] { 0, 7 }));
        });
    }

    [Test]
    public async Task Client_RedactsCredentialsInErrors_AndReacquiresTokenAfterUnauthorized()
    {
        using StubServer server = new();
        int failures = 0;
        server.Intercept = (request, _) => Task.FromResult<HttpResponseMessage?>(
            request.Uri.AbsolutePath.EndsWith("/messages") && Interlocked.Increment(ref failures) == 1
                ? StubServer.JsonResponse(new { error = "test-secret test-token" }, HttpStatusCode.Unauthorized) : null);
        using HttpClient http = new(server);
        using QQNotificationClient client = new(http);
        client.Configure("app-id", "test-secret");
        try
        {
            await client.SendAsync([new QQNotificationTarget(QQNotificationTargetKind.User, "user-id")], "完成", null);
            Assert.Fail("鉴权失败不应报告发送成功。");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message, Does.Not.Contain("test-secret").And.Not.Contain("test-token"));
        }
        await client.SendAsync([new QQNotificationTarget(QQNotificationTargetKind.User, "user-id")], "完成", null);
        Assert.That(server.Requests.Count(request => request.Uri.Host == "bots.qq.com"), Is.EqualTo(2));
    }

    [Test]
    public async Task Alert_ScriptContinuesWhileNetworkIsBlocked_AndWritesLocalAlert()
    {
        using StubServer server = new();
        TaskCompletionSource<bool> sending = NewSignal();
        TaskCompletionSource<bool> release = NewSignal();
        server.Intercept = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/messages"))
            {
                sending.TrySetResult(true);
                await release.Task.WaitAsync(token);
            }
            return null;
        };
        using HttpClient http = new(server);
        await using QQNotificationService service = new(sender: new QQNotificationClient(http),
            settings: ReadySettings(attachImage: false));
        RecordingIo io = new();
        IScriptSession session = new EasyScriptEngine().FromSource("ALERT \"完成\"\nPRINT \"继续执行\"",
            new ScriptHostOptions { Compile = new EasyCon.Script.CompileOptions { UseDiskCache = false } });
        Task script = Task.Run(() => session.Run(CancellationToken.None,
            new CapabilitySet { Console = new ConsoleIoAdapter(io, service) }));
        try
        {
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await script.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(io.Lines, Does.Contain("继续执行"));
                Assert.That(io.Alerts, Does.Contain("完成"));
                Assert.That(service.FlushAsync().IsCompleted, Is.False);
            });
        }
        finally
        {
            release.TrySetResult(true);
        }
        await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Service_DisablingCancelsActiveSendAndQueuedMessages()
    {
        using StubServer server = new();
        TaskCompletionSource<bool> sending = NewSignal();
        server.Intercept = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/messages"))
            {
                sending.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }
            return null;
        };
        using HttpClient http = new(server);
        await using QQNotificationService service = new(sender: new QQNotificationClient(http),
            settings: ReadySettings(attachImage: false));
        service.Dispatch("第一条");
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispatch("第二条");
        service.Update(settings => settings.enabled = false, save: false);
        await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(server.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("/messages")), Is.EqualTo(1));
    }

    [Test]
    public async Task Service_DefaultNotificationAndTextOnlyConfiguredTestBothIncludeImages()
    {
        using StubServer automatic = new();
        using StubServer setup = new();
        using HttpClient sendHttp = new(automatic);
        using HttpClient setupHttp = new(setup);
        await using QQNotificationService service = new(client: new QQNotificationClient(setupHttp),
            sender: new QQNotificationClient(sendHttp), settings: ReadySettings());
        service.Dispatch("完成");
        await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(automatic.Requests.Any(request => request.Uri.AbsolutePath.EndsWith("/files")), Is.True);
        service.Update(settings => settings.attach_image = false, save: false);
        await service.SendTestAsync();
        Assert.That(setup.Requests.Any(request => request.Uri.AbsolutePath.EndsWith("/files")), Is.True);
    }

    [Test]
    public async Task Service_ChangingAppIdClearsBindingsAndDisablesAutomaticNotifications()
    {
        await using QQNotificationService service = new(settings: ReadySettings());
        service.Update(settings => settings.app_id = "another-app", save: false);
        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.enabled, Is.False);
            Assert.That(service.Settings.verified, Is.False);
            Assert.That(service.Settings.user_openid, Is.Empty);
            Assert.That(service.Settings.group_openid, Is.Empty);
        });
    }

    [Test]
    public async Task Service_BoundsWaitingQueueAndLogsOverflow()
    {
        using StubServer server = new();
        TaskCompletionSource<bool> sending = NewSignal();
        TaskCompletionSource<bool> release = NewSignal();
        server.Intercept = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/messages"))
            {
                sending.TrySetResult(true);
                await release.Task.WaitAsync(token);
            }
            return null;
        };
        using HttpClient http = new(server);
        await using QQNotificationService service = new(sender: new QQNotificationClient(http),
            settings: ReadySettings(attachImage: false));
        service.Dispatch("正在发送");
        try
        {
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 21; i++)
                service.Dispatch("排队通知");
            Assert.That(service.LastError, Does.Contain("待发送通知过多"));
        }
        finally
        {
            release.TrySetResult(true);
        }
        await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(server.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("/messages")), Is.EqualTo(21));
    }

    [Test]
    public async Task Service_QueuesImageSnapshotEvenIfCallerReusesItsBuffer()
    {
        using StubServer server = new();
        TaskCompletionSource<bool> sending = NewSignal();
        TaskCompletionSource<bool> release = NewSignal();
        server.Intercept = async (request, token) =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/messages"))
            {
                sending.TrySetResult(true);
                await release.Task.WaitAsync(token);
            }
            return null;
        };
        using HttpClient http = new(server);
        await using QQNotificationService service = new(sender: new QQNotificationClient(http),
            settings: ReadySettings());
        byte[] image = [1, 2, 3];
        service.Dispatch("完成", image: image);
        image[0] = 9;
        try
        {
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult(true);
        }
        await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(server.Requests.Single(request => request.Method == HttpMethod.Put).Bytes,
            Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Service_ImageFailureIsLoggedAndNeverBecomesTextOnlySuccess()
    {
        using StubServer server = new();
        using HttpClient http = new(server);
        await using QQNotificationService service = new(sender: new QQNotificationClient(http),
            settings: ReadySettings())
        { ImageProvider = () => throw new InvalidOperationException("截图失败") };
        service.Dispatch("完成");
        await service.FlushAsync();
        Assert.Multiple(() =>
        {
            Assert.That(service.LastError, Does.Contain("截图失败"));
            Assert.That(server.Requests, Is.Empty);
        });
    }

    private static QQNotificationSettings ReadySettings(bool attachImage = true) => new()
    {
        app_id = "app-id",
        secret = "test-secret",
        user_openid = "user-id",
        enabled = true,
        verified = true,
        attach_image = attachImage,
    };

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record Request(Uri Uri, HttpMethod Method, string? Authorization, byte[] Bytes)
    {
        public JsonElement Json
        {
            get
            {
                using JsonDocument document = JsonDocument.Parse(Bytes);
                return document.RootElement.Clone();
            }
        }
    }

    private sealed class StubServer : HttpMessageHandler
    {
        public ConcurrentQueue<Request> Requests { get; } = new();
        public int BlockSize { get; init; } = 1024 * 1024;
        public bool DuplicateParts { get; init; }
        public Func<Request, CancellationToken, Task<HttpResponseMessage?>>? Intercept { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request recorded = new(request.RequestUri!, request.Method, request.Headers.Authorization?.ToString(),
                request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken));
            Requests.Enqueue(recorded);
            if (Intercept != null)
            {
                HttpResponseMessage? intercepted = await Intercept(recorded, cancellationToken);
                if (intercepted != null)
                    return intercepted;
            }
            if (recorded.Uri.Host == "bots.qq.com")
                return JsonResponse(new { access_token = "test-token", expires_in = "7200" });
            if (recorded.Uri.AbsolutePath.EndsWith("/upload_prepare"))
            {
                int length = int.Parse(recorded.Json.GetProperty("file_size").GetString()!);
                int count = ((length - 1) / BlockSize) + 1;
                int[] indexes = DuplicateParts ? Enumerable.Repeat(0, count).ToArray()
                    : Enumerable.Range(0, count).Reverse().ToArray();
                return JsonResponse(new
                {
                    upload_id = "upload-id",
                    block_size = BlockSize.ToString(),
                    parts = indexes.Select(index => new { index, presigned_url = "https://storage.example/" + index }),
                });
            }
            if (recorded.Uri.AbsolutePath.EndsWith("/files"))
            {
                if (!recorded.Json.TryGetProperty("file_name", out JsonElement fileName) || fileName.GetString() != "notification.jpg")
                    return JsonResponse(new { error = "missing file_name" }, HttpStatusCode.BadRequest);
                return JsonResponse(new { file_info = "file-info" });
            }
            return JsonResponse(new { });
        }

        public static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }
}