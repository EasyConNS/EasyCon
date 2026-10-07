using System.Net;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.Flow;

/// <summary>
/// Flow 服务 HTTP 宿主（loopback HttpListener）：节点目录、设备管理、编排图运行。
/// 前端（Python 画布）与外部 agent 同源访问；无鉴权（仅绑定 127.0.0.1）。
/// </summary>
public sealed class FlowServiceHttp : IAsyncDisposable
{
    private readonly FlowServiceState _state;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <param name="port">监听端口（0 = 失败静默由调用方处理；建议 19391）。</param>
    public FlowServiceHttp(FlowServiceState state, int port)
    {
        _state = state;
        _port = port;
    }

    public string Url => $"http://127.0.0.1:{_port}";

    /// <summary>启动监听循环（后台）。</summary>
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                continue;
            }
            _ = HandleSafeAsync(ctx, ct);
        }
    }

    private async Task HandleSafeAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            await RouteAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 500, new { error = ex.Message });
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private async Task RouteAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;

        switch (method, path)
        {
            case ("GET", "/api/health"):
                await WriteJsonAsync(ctx, 200, new { ok = true, service = "easycon-flow" });
                return;

            case ("GET", "/api/nodes"):
                await WriteRawAsync(ctx, 200, FlowServiceState.NodeCatalogJson());
                return;

            case ("GET", "/api/device/video"):
                await WriteJsonAsync(ctx, 200, _state.VideoInfo());
                return;

            case ("POST", "/api/device/video/connect"):
                {
                    using var bodyDoc = await ReadJsonAsync(ctx);
                    var body = bodyDoc.RootElement;
                    var index = GetInt(body, "index", -1);
                    var api = GetInt(body, "api", 0);
                    var error = _state.ConnectVideo(index, api);
                    await WriteJsonAsync(ctx, error == null ? 200 : 400, error == null ? new { ok = true } : new { ok = false, error });
                    return;
                }

            case ("POST", "/api/device/video/disconnect"):
                _state.DisconnectVideo();
                await WriteJsonAsync(ctx, 200, new { ok = true });
                return;

            case ("GET", "/api/device/mcu"):
                await WriteJsonAsync(ctx, 200, _state.McuInfo());
                return;

            case ("POST", "/api/device/mcu/connect"):
                {
                    using var bodyDoc = await ReadJsonAsync(ctx);
                    var body = bodyDoc.RootElement;
                    var port = GetString(body, "port") ?? "mock";
                    var error = _state.ConnectMcu(port);
                    await WriteJsonAsync(ctx, error == null ? 200 : 400, error == null ? new { ok = true } : new { ok = false, error });
                    return;
                }

            case ("POST", "/api/device/mcu/disconnect"):
                _state.DisconnectMcu();
                await WriteJsonAsync(ctx, 200, new { ok = true });
                return;

            case ("POST", "/api/flow/run"):
                {
                    using var bodyDoc = await ReadJsonAsync(ctx);
                    var body = bodyDoc.RootElement;
                    string json;
                    if (body.TryGetProperty("path", out var pathEl))
                    {
                        var filePath = pathEl.GetString() ?? "";
                        var full = Path.IsPathRooted(filePath) ? filePath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
                        if (!File.Exists(full))
                        {
                            await WriteJsonAsync(ctx, 400, new { error = $"图文件不存在: {full}" });
                            return;
                        }
                        json = File.ReadAllText(full);
                    }
                    else if (body.TryGetProperty("json", out var jsonEl))
                    {
                        json = jsonEl.GetString() ?? "";
                    }
                    else
                    {
                        await WriteJsonAsync(ctx, 400, new { error = "需要 path 或 json 字段" });
                        return;
                    }

                    string runId;
                    try
                    {
                        runId = _state.StartFlow(json);
                    }
                    catch (FlowParseException ex)
                    {
                        await WriteJsonAsync(ctx, 400, new { error = ex.Message });
                        return;
                    }
                    await WriteJsonAsync(ctx, 200, new { runId });
                    return;
                }

            case ("POST", "/api/flow/stop"):
                {
                    using var bodyDoc = await ReadJsonAsync(ctx);
                    _state.StopFlow(GetString(bodyDoc.RootElement, "runId") ?? "");
                    await WriteJsonAsync(ctx, 200, new { ok = true });
                    return;
                }

            case ("GET", "/api/flow/status"):
                {
                    var query = ctx.Request.QueryString["runId"];
                    await WriteJsonAsync(ctx, 200, _state.FlowStatus(string.IsNullOrEmpty(query) ? null : query));
                    return;
                }

            case ("POST", "/api/node/run"):
                {
                    FlowServiceState.FlowNodeRunRequest? request;
                    try
                    {
                        using var bodyDoc = await ReadJsonAsync(ctx);
                        request = bodyDoc.RootElement.Deserialize<FlowServiceState.FlowNodeRunRequest>(FlowGraph.JsonOpts);
                    }
                    catch (JsonException ex)
                    {
                        await WriteJsonAsync(ctx, 400, new { error = $"请求体解析失败: {ex.Message}" });
                        return;
                    }
                    if (request is null)
                    {
                        await WriteJsonAsync(ctx, 400, new { error = "请求体为空" });
                        return;
                    }

                    var result = _state.RunNode(request);
                    await WriteJsonAsync(ctx, result.Ok ? 200 : 400, result);
                    return;
                }

            case ("GET", "/api/options"):
                await WriteJsonAsync(ctx, 200, new
                {
                    appDir = AppDomain.CurrentDomain.BaseDirectory,
                    ocr = _state.OcrInfo(),
                });
                return;

            case ("POST", "/api/options"):
                {
                    using var bodyDoc = await ReadJsonAsync(ctx);
                    var body = bodyDoc.RootElement;
                    var backend = GetString(body, "ocrBackend");
                    if (backend is null)
                    {
                        await WriteJsonAsync(ctx, 400, new { error = "需要 ocrBackend 字段" });
                        return;
                    }
                    var error = _state.SetOcrBackend(backend, GetString(body, "ocrModelDir"));
                    await WriteJsonAsync(ctx, error == null ? 200 : 400,
                        error == null ? new { ok = true, ocr = _state.OcrInfo() } : new { ok = false, error });
                    return;
                }

            default:
                ctx.Response.StatusCode = 404;
                return;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body))
            return JsonDocument.Parse("{}");
        return JsonDocument.Parse(body);
    }

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var el)
        && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : fallback;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() : null;

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    private static async Task WriteRawAsync(HttpListenerContext ctx, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_listener?.IsListening == true)
            _listener.Stop();
        _listener?.Close();
        await Task.CompletedTask;
    }
}