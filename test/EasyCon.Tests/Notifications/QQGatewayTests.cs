using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyCon.Tests.Notifications;

[TestFixture]
public class QQGatewayTests
{
    [TestCase(QQNotificationTargetKind.User)]
    [TestCase(QQNotificationTargetKind.Group)]
    public async Task Bind_UsesLatestSequenceAndOnlyAcceptsMatchingRecipientMessage(QQNotificationTargetKind kind)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using Gateway gateway = new();
        using HttpClient http = new(new GatewayHandler(gateway.Url));
        using QQNotificationClient client = new(http);
        client.Configure("app-id", "test-secret");
        TaskCompletionSource<string> bindingCode = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task server = gateway.ServeAsync(async socket =>
        {
            await IdentifyAsync(socket, 100, timeout.Token);
            string code = await bindingCode.Task.WaitAsync(timeout.Token);
            using JsonDocument heartbeat = await ReceiveAsync(socket, timeout.Token);
            Assert.Multiple(() =>
            {
                Assert.That(heartbeat.RootElement.GetProperty("op").GetInt32(), Is.EqualTo(1));
                Assert.That(heartbeat.RootElement.GetProperty("d").GetInt64(), Is.EqualTo(27));
            });
            await SendAsync(socket, new { op = 11 }, timeout.Token);
            await MessageAsync(socket, kind, code, "unrelated-add", timeout.Token, unrelated: true);
            await MessageAsync(socket, kind, "9" + code, "wrong-digits", timeout.Token);
            await MessageAsync(socket, kind, "绑定 " + code, "bound-receiver", timeout.Token);
        }, timeout.Token);
        string openId = await client.BindAsync(kind, code => bindingCode.TrySetResult(code.Code), timeout.Token);
        await server;
        Assert.That(openId, Is.EqualTo("bound-receiver"));
    }

    [Test]
    public async Task Bind_MissingHeartbeatAckFailsPromptly()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using Gateway gateway = new();
        using HttpClient http = new(new GatewayHandler(gateway.Url));
        using QQNotificationClient client = new(http);
        client.Configure("app-id", "test-secret");
        Task server = gateway.ServeAsync(async socket =>
        {
            await IdentifyAsync(socket, 100, timeout.Token);
            using JsonDocument heartbeat = await ReceiveAsync(socket, timeout.Token);
            await Task.Delay(Timeout.Infinite, timeout.Token);
        }, timeout.Token);
        try
        {
            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client.BindAsync(QQNotificationTargetKind.User, cancellationToken: timeout.Token));
            Assert.That(error!.Message, Does.Contain("心跳未响应"));
        }
        finally
        {
            timeout.Cancel();
            await server;
        }
    }

    [Test]
    public async Task Bind_CodeRefreshKeepsSocketAliveAndRejectsExpiredCode()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(75));
        using Gateway gateway = new();
        using HttpClient http = new(new GatewayHandler(gateway.Url));
        using QQNotificationClient client = new(http);
        client.Configure("app-id", "test-secret");
        TaskCompletionSource<string> original = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string> renewed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task server = gateway.ServeAsync(async socket =>
        {
            await IdentifyAsync(socket, 1000, timeout.Token);
            while (true)
            {
                using JsonDocument heartbeat = await ReceiveAsync(socket, timeout.Token);
                if (heartbeat.RootElement.GetProperty("op").GetInt32() == 1)
                    await SendAsync(socket, new { op = 11 }, timeout.Token);
                if (renewed.Task.IsCompleted)
                {
                    await MessageAsync(socket, QQNotificationTargetKind.User, await original.Task, "expired", timeout.Token);
                    await MessageAsync(socket, QQNotificationTargetKind.User, await renewed.Task, "renewed", timeout.Token);
                    return;
                }
            }
        }, timeout.Token);
        string result = await client.BindAsync(QQNotificationTargetKind.User, code =>
        {
            if (code.Generation == 1)
                original.TrySetResult(code.Code);
            if (code.Generation == 2)
                renewed.TrySetResult(code.Code);
        }, timeout.Token);
        await server;
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("renewed"));
            Assert.That(renewed.Task.Result, Is.Not.EqualTo(original.Task.Result));
        });
    }

    private static async Task IdentifyAsync(WebSocket socket, int heartbeatMs, CancellationToken token)
    {
        await SendAsync(socket, new { op = 10, d = new { heartbeat_interval = heartbeatMs } }, token);
        using JsonDocument identify = await ReceiveAsync(socket, token);
        Assert.Multiple(() =>
        {
            Assert.That(identify.RootElement.GetProperty("op").GetInt32(), Is.EqualTo(2));
            Assert.That(identify.RootElement.GetProperty("d").GetProperty("token").GetString(), Is.EqualTo("QQBot test-token"));
            Assert.That(identify.RootElement.GetProperty("d").GetProperty("intents").GetInt32(), Is.EqualTo(1 << 25));
        });
        await SendAsync(socket, new { op = 0, s = 27, t = "READY", d = new { } }, token);
    }

    private static Task MessageAsync(WebSocket socket, QQNotificationTargetKind kind,
        string content, string receiver, CancellationToken token, bool unrelated = false)
    {
        return SendAsync(socket, new
        {
            op = 0,
            s = 28,
            t = unrelated ? "FRIEND_ADD" : kind == QQNotificationTargetKind.User ? "C2C_MESSAGE_CREATE" : "GROUP_AT_MESSAGE_CREATE",
            d = new { content, author = new { user_openid = receiver }, group_openid = receiver },
        }, token);
    }

    private static async Task SendAsync(WebSocket socket, object payload, CancellationToken token)
    {
        await socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(payload)),
            WebSocketMessageType.Text, true, token);
    }

    private static async Task<JsonDocument> ReceiveAsync(WebSocket socket, CancellationToken token)
    {
        using MemoryStream data = new();
        byte[] buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("网关测试连接意外关闭。");
            data.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(data.ToArray());
    }

    private sealed class Gateway : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public Gateway() => _listener.Start();
        public string Url => "ws://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";

        public async Task ServeAsync(Func<WebSocket, Task> flow, CancellationToken token)
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(token);
                using NetworkStream stream = client.GetStream();
                StringBuilder headers = new();
                byte[] one = new byte[1];
                while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (headers.Length > 8192 || await stream.ReadAsync(one, token) == 0)
                        throw new InvalidOperationException("网关测试握手失败。");
                    headers.Append((char)one[0]);
                }
                string key = headers.ToString().Split("\r\n")
                    .Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    .Split(':', 2)[1].Trim();
                string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\n"
                    + "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await stream.WriteAsync(response, token);
                using WebSocket socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
                await flow(socket);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        public void Dispose() => _listener.Stop();
    }

    private sealed class GatewayHandler(string url) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object response = request.RequestUri!.Host == "bots.qq.com"
                ? new { access_token = "test-token", expires_in = 7200 } : new { url };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json"),
            });
        }
    }
}