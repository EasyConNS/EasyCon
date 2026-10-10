#nullable enable

using EasyCon.Core.Config;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyCon.Core.Notifications;

/// <summary>QQ 开放平台机器人客户端。每个实例只处理一条顺序网络通路。</summary>
public sealed class QQNotificationClient : IDisposable
{
    private const string ApiBase = "https://api.sgroup.qq.com";
    private const string TokenEndpoint = "https://bots.qq.com/app/getAppAccessToken";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private string _appId = "";
    private string _secret = "";
    private string _token = "";
    private DateTimeOffset _tokenExpires;
    private bool _disposed;

    public QQNotificationClient(HttpClient? http = null)
    {
        _ownsHttp = http == null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public void Configure(string appId, string secret)
    {
        ThrowIfDisposed();
        appId = appId.Trim();
        secret = secret.Trim();
        if (appId.Length == 0 || secret.Length == 0)
            throw new InvalidOperationException("请填写 QQ AppID 和 AppSecret。");

        if (_appId != appId || _secret != secret)
        {
            _token = "";
            _tokenExpires = default;
        }
        _appId = appId;
        _secret = secret;
    }

    public async Task VerifyCredentialsAsync(CancellationToken cancellationToken = default)
    {
        _token = "";
        _tokenExpires = default;
        _ = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> BindAsync(
        QQNotificationTargetKind kind,
        Action<QQBindingCode>? codeChanged = null,
        CancellationToken cancellationToken = default)
    {
        string token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument gateway = await SendJsonAsync(
            HttpMethod.Get, ApiBase + "/gateway", token, null, cancellationToken).ConfigureAwait(false);
        string url = gateway.RootElement.TryGetProperty("url", out JsonElement urlValue)
            ? urlValue.GetString() ?? "" : "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? gatewayUri) ||
            (gatewayUri.Scheme != Uri.UriSchemeWs && gatewayUri.Scheme != Uri.UriSchemeWss))
            throw new InvalidOperationException("QQ 接口未返回有效的 WebSocket 网关地址。");

        using ClientWebSocket socket = new();
        using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshake.CancelAfter(TimeSpan.FromSeconds(20));
        await socket.ConnectAsync(gatewayUri, handshake.Token).ConfigureAwait(false);
        using JsonDocument hello = await ReceiveJsonAsync(socket, handshake.Token).ConfigureAwait(false);
        if (!hello.RootElement.TryGetProperty("op", out JsonElement helloOp) || helloOp.GetInt32() != 10)
            throw new InvalidOperationException("QQ 网关未返回 Hello 事件。");
        int heartbeatMs = hello.RootElement.TryGetProperty("d", out JsonElement helloData)
            && helloData.TryGetProperty("heartbeat_interval", out JsonElement interval)
            ? ReadInteger(interval) : 0;
        if (heartbeatMs <= 0)
            throw new InvalidOperationException("QQ 网关未返回有效的心跳间隔。");
        await SendJsonAsync(socket, new
        {
            op = 2,
            d = new { token = "QQBot " + token, intents = 1 << 25, shard = new[] { 0, 1 } },
        }, handshake.Token).ConfigureAwait(false);
        handshake.CancelAfter(Timeout.InfiniteTimeSpan);

        // 保持唯一的 ReceiveAsync；取消一次接收会中止 ClientWebSocket，不能用它刷新绑定码。
        using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<JsonDocument> receive = ReceiveJsonAsync(socket, session.Token);
        Stopwatch clock = Stopwatch.StartNew();
        TimeSpan nextHeartbeat = TimeSpan.FromMilliseconds(heartbeatMs);
        TimeSpan codeExpires = TimeSpan.Zero;
        bool ready = false;
        bool awaitingAck = false;
        long? sequence = null;
        string code = "";
        int generation = 0;
        int lastSeconds = -1;
        try
        {
            while (true)
            {
                session.Token.ThrowIfCancellationRequested();
                if (!ready && clock.Elapsed > TimeSpan.FromSeconds(20))
                    throw new InvalidOperationException("QQ 网关鉴权超时，请重新绑定。");

                if (ready && clock.Elapsed >= codeExpires)
                {
                    code = NewBindingCode(code);
                    generation++;
                    codeExpires = clock.Elapsed + TimeSpan.FromSeconds(60);
                    lastSeconds = -1;
                }
                if (ready)
                {
                    int seconds = Math.Max(0, (int)Math.Ceiling((codeExpires - clock.Elapsed).TotalSeconds));
                    if (seconds != lastSeconds)
                    {
                        codeChanged?.Invoke(new QQBindingCode(kind, code, seconds, generation));
                        lastSeconds = seconds;
                    }
                }

                using CancellationTokenSource tick = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                Task delay = Task.Delay(200, tick.Token);
                await Task.WhenAny(receive, delay).ConfigureAwait(false);
                if (receive.IsCompleted)
                {
                    tick.Cancel();
                    using JsonDocument payload = await receive.ConfigureAwait(false);
                    receive = ReceiveJsonAsync(socket, session.Token);
                    JsonElement root = payload.RootElement;
                    if (root.TryGetProperty("s", out JsonElement s) &&
                        s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long value))
                        sequence = value;
                    int op = root.TryGetProperty("op", out JsonElement opValue) ? opValue.GetInt32() : -1;
                    if (op == 11)
                        awaitingAck = false;
                    else if (op == 1)
                    {
                        await SendJsonAsync(socket, new { op = 1, d = sequence }, session.Token).ConfigureAwait(false);
                        awaitingAck = true;
                        nextHeartbeat = clock.Elapsed + TimeSpan.FromMilliseconds(heartbeatMs);
                    }
                    else if (op is 7 or 9)
                        throw new InvalidOperationException("QQ 网关要求重连或鉴权失败，请检查机器人权限后重新绑定。");
                    else if (op == 0)
                    {
                        string eventName = root.TryGetProperty("t", out JsonElement eventValue)
                            ? eventValue.GetString() ?? "" : "";
                        if (eventName == "READY")
                            ready = true;
                        else if (ready && IsBindingEvent(kind, eventName) && clock.Elapsed < codeExpires &&
                            root.TryGetProperty("d", out JsonElement data))
                        {
                            string content = data.TryGetProperty("content", out JsonElement contentValue)
                                ? contentValue.GetString() ?? "" : "";
                            if (Regex.IsMatch(content, @"(?<!\d)" + code + @"(?!\d)"))
                            {
                                string openId = kind == QQNotificationTargetKind.User
                                    ? data.TryGetProperty("author", out JsonElement author)
                                        && author.TryGetProperty("user_openid", out JsonElement userOpenId)
                                        ? userOpenId.GetString() ?? "" : ""
                                    : data.TryGetProperty("group_openid", out JsonElement groupOpenId)
                                        ? groupOpenId.GetString() ?? "" : "";
                                if (string.IsNullOrWhiteSpace(openId))
                                    throw new InvalidOperationException("收到验证码，但 QQ 事件中缺少 OpenID。");
                                return openId.Trim();
                            }
                        }
                    }
                }

                if (clock.Elapsed >= nextHeartbeat)
                {
                    if (awaitingAck)
                        throw new InvalidOperationException("QQ 网关心跳未响应，请重新绑定。");
                    await SendJsonAsync(socket, new { op = 1, d = sequence }, session.Token).ConfigureAwait(false);
                    awaitingAck = true;
                    nextHeartbeat = clock.Elapsed + TimeSpan.FromMilliseconds(heartbeatMs);
                }
            }
        }
        finally
        {
            session.Cancel();
            socket.Abort();
            try
            {
                using JsonDocument pending = await receive.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 清理仍在等待的接收任务，保留绑定操作本身的结果。
            }
        }
    }

    public async Task SendAsync(
        IReadOnlyList<QQNotificationTarget> targets,
        string text,
        byte[]? image,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0 || targets.Any(target => string.IsNullOrWhiteSpace(target.OpenId)))
            throw new InvalidOperationException("请先绑定勾选的 QQ 接收方。");
        if (string.IsNullOrWhiteSpace(text) && image is not { Length: > 0 })
            throw new InvalidOperationException("通知文字和图片不能同时为空。");

        List<string> failures = [];
        foreach (QQNotificationTarget target in targets)
        {
            bool textSubmitted = false;
            try
            {
                string token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
                string route = target.Kind == QQNotificationTargetKind.User ? "/v2/users/" : "/v2/groups/";
                string baseUrl = ApiBase + route + Uri.EscapeDataString(target.OpenId.Trim());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    using JsonDocument response = await SendJsonAsync(HttpMethod.Post, baseUrl + "/messages", token,
                        new { msg_type = 0, content = text }, cancellationToken).ConfigureAwait(false);
                    textSubmitted = true;
                }
                if (image is { Length: > 0 })
                {
                    string fileInfo = await UploadImageAsync(baseUrl, token, image, cancellationToken).ConfigureAwait(false);
                    using JsonDocument response = await SendJsonAsync(HttpMethod.Post, baseUrl + "/messages", token,
                        new { msg_type = 7, media = new { file_info = fileInfo } }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string label = target.Kind == QQNotificationTargetKind.User ? "私聊" : "群聊";
                failures.Add(label + "：" + (textSubmitted ? "文字已提交；" : "") + RedactError(ex.Message));
            }
        }
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join("；", failures) + "。已提交的消息不会撤回，请在 QQ 中核对后再决定是否重发。");
    }

    private async Task<string> UploadImageAsync(string baseUrl, string token, byte[] image, CancellationToken cancellationToken)
    {
        using JsonDocument prepared = await SendJsonAsync(HttpMethod.Post, baseUrl + "/upload_prepare", token, new
        {
            file_type = 1,
            file_size = image.Length.ToString(CultureInfo.InvariantCulture),
            file_name = "notification.jpg",
            md5 = HashMd5(image),
            sha1 = Convert.ToHexString(SHA1.HashData(image)).ToLowerInvariant(),
            md5_10m = HashMd5(image.AsSpan(0, Math.Min(image.Length, 10_002_432))),
        }, cancellationToken).ConfigureAwait(false);
        JsonElement root = prepared.RootElement;
        string uploadId = root.GetProperty("upload_id").GetString() ?? "";
        int blockSize = ReadInteger(root.GetProperty("block_size"));
        JsonElement parts = root.GetProperty("parts");
        if (string.IsNullOrWhiteSpace(uploadId) || blockSize <= 0 || parts.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("图片上传接口返回了无效的分片信息。");

        int expected = ((image.Length - 1) / blockSize) + 1;
        List<(int Index, Uri Url)> uploadParts = [];
        foreach (JsonElement part in parts.EnumerateArray())
        {
            int index = ReadInteger(part.GetProperty("index"));
            string url = part.GetProperty("presigned_url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uploadUri) ||
                (uploadUri.Scheme != Uri.UriSchemeHttp && uploadUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("图片上传接口返回了无效的分片地址。");
            uploadParts.Add((index, uploadUri));
        }
        if (uploadParts.Count != expected || !uploadParts.Select(part => part.Index)
            .OrderBy(index => index).SequenceEqual(Enumerable.Range(1, expected)))
            throw new InvalidOperationException("图片上传接口返回的分片列表不完整。");

        foreach ((int index, Uri url) in uploadParts.OrderBy(part => part.Index))
        {
            int offset = (index - 1) * blockSize;
            int length = Math.Min(blockSize, image.Length - offset);
            using HttpRequestMessage upload = new(HttpMethod.Put, url)
            {
                Content = new ByteArrayContent(image, offset, length),
            };
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using HttpResponseMessage uploadResponse = await _http.SendAsync(upload, cancellationToken).ConfigureAwait(false);
            if (!uploadResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"图片分片上传失败：HTTP {(int)uploadResponse.StatusCode}");
            using JsonDocument finished = await SendJsonAsync(HttpMethod.Post, baseUrl + "/upload_part_finish", token, new
            {
                upload_id = uploadId,
                part_index = index,
                block_size = length.ToString(CultureInfo.InvariantCulture),
                md5 = HashMd5(image.AsSpan(offset, length)),
            }, cancellationToken).ConfigureAwait(false);
        }
        using JsonDocument result = await SendJsonAsync(HttpMethod.Post, baseUrl + "/files", token,
            new { file_type = 1, upload_id = uploadId }, cancellationToken).ConfigureAwait(false);
        string fileInfo = result.RootElement.TryGetProperty("file_info", out JsonElement fileValue)
            ? fileValue.GetString() ?? "" : "";
        return string.IsNullOrWhiteSpace(fileInfo)
            ? throw new InvalidOperationException("图片合并接口未返回 file_info。") : fileInfo;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!string.IsNullOrWhiteSpace(_token) && DateTimeOffset.UtcNow < _tokenExpires)
            return _token;
        if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_secret))
            throw new InvalidOperationException("请填写 QQ AppID 和 AppSecret。");
        using JsonDocument result = await SendJsonAsync(HttpMethod.Post, TokenEndpoint, "",
            new { appId = _appId, clientSecret = _secret }, cancellationToken).ConfigureAwait(false);
        _token = result.RootElement.TryGetProperty("access_token", out JsonElement tokenValue)
            ? tokenValue.GetString() ?? "" : "";
        int lifetime = result.RootElement.TryGetProperty("expires_in", out JsonElement expiresValue)
            ? ReadInteger(expiresValue) : 0;
        if (string.IsNullOrWhiteSpace(_token))
            throw new InvalidOperationException("QQ 接口未返回 access_token。");
        _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, lifetime - Math.Min(60, lifetime / 2)));
        return _token;
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string url, string token, object? body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, url);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("QQBot", token);
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string error = RedactError(responseText);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _token = "";
                _tokenExpires = default;
            }
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}：{error}");
        }
        JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            (document.RootElement.TryGetProperty("code", out JsonElement code) && code.ToString() is not ("0" or "")))
        {
            document.Dispose();
            throw new InvalidOperationException("QQ API 错误：" + RedactError(responseText));
        }
        return document;
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream stream = new();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("QQ 网关连接已关闭或返回了无效的消息格式。");
            if (stream.Length + result.Count > 1024 * 1024)
                throw new InvalidOperationException("QQ 网关消息超过允许的大小。");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(stream.ToArray());
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object body, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsBindingEvent(QQNotificationTargetKind kind, string eventName)
    {
        return kind == QQNotificationTargetKind.User
            ? eventName == "C2C_MESSAGE_CREATE" : eventName is "GROUP_AT_MESSAGE_CREATE" or "GROUP_MESSAGE_CREATE";
    }

    private static string NewBindingCode(string previous)
    {
        string code;
        do
        {
            code = RandomNumberGenerator.GetInt32(100000, 1_000_000).ToString(CultureInfo.InvariantCulture);
        } while (code == previous);
        return code;
    }

    private static int ReadInteger(JsonElement value) => int.Parse(value.ToString(), CultureInfo.InvariantCulture);

    private static string HashMd5(ReadOnlySpan<byte> data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    private string RedactError(string detail)
    {
        foreach (string value in new[] { _secret, _token })
        {
            if (value.Length > 0)
                detail = detail.Replace(value, "[已隐藏]", StringComparison.Ordinal);
        }
        detail = detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return detail.Length > 500 ? detail[..500] : detail;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsHttp)
            _http.Dispose();
    }
}

public sealed record QQBindingCode(QQNotificationTargetKind Kind, string Code, int Seconds, int Generation);