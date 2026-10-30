using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace EasyDevice.Connection.Pokeldn;

/// <summary>官方 pokeldn.pad.service 的本机 JSON 行协议，由该服务持有 ESP32-S3 的 BLE 连接。</summary>
internal sealed class PokeldnServiceTransport(int port = 47800) : IPokeldnTransport
{
    private readonly TcpClient _client = new() { NoDelay = true };
    private StreamReader? _reader;
    private NetworkStream? _stream;

    public int ConnectTimeoutMilliseconds => 60000;

    public PokeldnStatus Connect(CancellationToken token)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            _client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            throw new IOException("无法连接 pokeldn 本地手柄服务。请先在 pokeldn 的 Control 页面连接 ESP32-S3，或运行 python -m pokeldn.pad.service。", ex);
        }
        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        // 上游扫描、连接最长 40 秒；整个连接仍在后台执行，且可取消。
        using JsonDocument reply = Call("connect", null, token, 45000);
        return GetStatus(token);
    }

    public PokeldnStatus GetStatus(CancellationToken token)
    {
        using JsonDocument reply = Call("status", null, token, 6000);
        JsonElement root = reply.RootElement;
        if (!root.TryGetProperty("connected", out JsonElement connected) || !connected.GetBoolean()
            || !root.TryGetProperty("status", out JsonElement status))
            throw new IOException("pokeldn 服务尚未连接手柄。请给 ESP32-S3 供电并开启电脑蓝牙。");
        return new PokeldnStatus(
            status.GetProperty("mounted").GetBoolean(),
            status.TryGetProperty("playing", out JsonElement playing) && playing.GetBoolean(),
            status.TryGetProperty("version", out JsonElement version) ? version.GetString() ?? "" : "");
    }

    public void SendReport(byte[] report, CancellationToken token)
    {
        using JsonDocument reply = Call("send", Convert.ToHexString(report), token);
    }

    public void Stop(CancellationToken token)
    {
        using JsonDocument reply = Call("stop", null, token);
    }

    public void Disconnect(CancellationToken token)
    {
        using JsonDocument reply = Call("disconnect", null, token);
    }

    private JsonDocument Call(string operation, string? report, CancellationToken token, int timeoutMilliseconds = 5000)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMilliseconds);
        using MemoryStream json = new();
        using (Utf8JsonWriter writer = new(json))
        {
            writer.WriteStartObject();
            writer.WriteString("op", operation);
            if (report != null)
                writer.WriteString("report", report);
            writer.WriteEndObject();
        }
        json.WriteByte((byte)'\n');
        try
        {
            _stream!.WriteAsync(json.ToArray(), timeout.Token).AsTask().GetAwaiter().GetResult();
            string? line = _reader!.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            if (line == null)
                throw new IOException("pokeldn 手柄服务已断开。");
            JsonDocument reply = JsonDocument.Parse(line);
            if (!reply.RootElement.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.True)
            {
                string message = reply.RootElement.TryGetProperty("error", out JsonElement error)
                    ? error.GetString() ?? "未知错误" : "无效响应";
                reply.Dispose();
                throw new IOException($"pokeldn 服务: {message}");
            }
            return reply;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // 超时后的迟到响应不能被下一次请求当作 ACK；工作循环将关闭连接。
            throw new TimeoutException($"pokeldn 服务未及时响应 {operation}。");
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _reader?.Dispose();
    }
}