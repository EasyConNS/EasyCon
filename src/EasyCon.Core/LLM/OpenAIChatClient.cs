using EasyCon.Core.LLM.Tools;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.LLM;

public sealed class OpenAIChatClient : IChatClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    };

    private const int MaxRetries = 3;
    private readonly HttpClient _http;
    private bool _disposed;

    public OpenAIChatClient(string baseUrl, string apiKey)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5)
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
    {
        request.Stream = false;

        // 请求阶段：带重试的指数退避
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            HttpResponseMessage? resp = null;
            try
            {
                using var msg = CreateRequestMessage(request);
                resp = await _http.SendAsync(msg, ct);

                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    return ParseFullResponse(body);
                }

                // 可重试状态码 + 还有剩余次数
                if (IsTransientStatus(resp.StatusCode) && attempt < MaxRetries)
                {
                    var retryAfter = ParseRetryAfter(resp);
                    resp.Dispose();
                    await Task.Delay(ComputeBackoff(attempt, retryAfter), ct);
                    continue;
                }

                // 不可重试 或 最后一次尝试 → 返回错误
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                return ErrorFrom(errBody, resp.StatusCode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new ChatResponse { Success = false, ErrorMessage = "请求已取消" };
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                await Task.Delay(ComputeBackoff(attempt, null), ct);
            }
            catch (Exception ex)
            {
                return new ChatResponse { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                resp?.Dispose();
            }
        }

        return new ChatResponse { Success = false, ErrorMessage = "重试次数已耗尽" };
    }

    /// <summary>
    /// 获取供应商可用模型列表（OpenAI 兼容的 GET /models 端点）。
    /// 返回模型 ID 列表（按字母序排序）；失败时抛出含中文信息的异常。
    /// </summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("models", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"获取模型列表失败：HTTP {(int)resp.StatusCode} {resp.StatusCode}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("获取模型列表失败：响应缺少 data 数组");

        var ids = new List<string>(dataEl.GetArrayLength());
        foreach (var item in dataEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var idEl)
                && idEl.ValueKind == JsonValueKind.String)
            {
                var id = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
        }

        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    public async IAsyncEnumerable<StreamDelta> SendStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        request.Stream = true;

        // ── 请求阶段：带重试的指数退避（透明重试，未 yield 任何 delta）──
        // C# 不允许在 catch 块中 yield，因此用局部变量捕获状态，在 try/catch 外部处理。
        var (resp, errorMsg) = await SendWithRetryAsync(request, ct);

        if (errorMsg is not null)
        {
            yield return StreamDelta.Error(errorMsg);
            yield break;
        }

        if (resp is null)
        {
            yield break;  // 用户取消
        }

        // ── 流读取阶段：异常标记为 Retryable（已 yield 部分数据，不可透明重试）──
        using (resp)
        await using (var stream = await resp.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            // 同样用局部变量捕获异常信息，避免在 catch 中 yield
            string? streamError = null;
            var isRetryable = false;

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    // 网络中断、IO 异常等 → 标记为可重试
                    streamError = $"流式传输中断: {ex.Message}";
                    isRetryable = true;
                    break;
                }

                if (line is null) break;

                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                foreach (var delta in ExtractDeltas(data))
                    yield return delta;
            }

            if (streamError is not null)
            {
                yield return StreamDelta.Error(streamError, retryable: isRetryable);
                yield break;
            }
        }
    }

    /// <summary>
    /// 请求阶段的重试循环，返回成功的响应或错误信息。
    /// 与 yield 分离以避免 C# 的 catch 块中不能 yield 的限制。
    /// </summary>
    /// <returns>
    /// (HttpResponseMessage?, string?) — 成功时返回 (resp, null)，
    /// 失败时返回 (null, errorMessage)，用户取消时返回 (null, null)。
    /// </returns>
    private async Task<(HttpResponseMessage?, string?)> SendWithRetryAsync(
        ChatRequest request, CancellationToken ct)
    {
        HttpResponseMessage? resp = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var msg = CreateRequestMessage(request);
                resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);

                if (resp.IsSuccessStatusCode)
                    return (resp, null);

                // 可重试状态码 + 还有剩余次数
                if (IsTransientStatus(resp.StatusCode) && attempt < MaxRetries)
                {
                    var retryAfter = ParseRetryAfter(resp);
                    resp.Dispose();
                    resp = null;
                    await Task.Delay(ComputeBackoff(attempt, retryAfter), ct);
                    continue;
                }

                // 不可重试 或 最后一次尝试
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                var error = ExtractErrorMessage(errBody, resp.StatusCode);
                resp.Dispose();
                return (null, error);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (null, null);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                await Task.Delay(ComputeBackoff(attempt, null), ct);
            }
            catch (HttpRequestException ex)
            {
                return (null, ex.Message);
            }
        }

        return (null, "重试次数已耗尽，无法建立连接");
    }

    // ── 重试辅助方法 ──────────────────────────────

    /// <summary>
    /// 计算退避时间：指数退避 (2s, 4s, 8s) + 20% 随机抖动。
    /// 如果提供了 Retry-After 秒数，优先使用。
    /// </summary>
    private static TimeSpan ComputeBackoff(int attempt, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is { } seconds and > 0)
            return TimeSpan.FromSeconds(seconds);

        var backoffMs = 2000 * (1 << (attempt - 1));   // 2000, 4000, 8000
        var jitterMs = (int)(backoffMs * 0.2 * Random.Shared.NextDouble());
        return TimeSpan.FromMilliseconds(backoffMs + jitterMs);
    }

    /// <summary>
    /// 判断 HTTP 状态码是否为可重试的瞬态错误。
    /// </summary>
    private static bool IsTransientStatus(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests        // 429
               or HttpStatusCode.InternalServerError       // 500
               or HttpStatusCode.BadGateway                // 502
               or HttpStatusCode.ServiceUnavailable;       // 503

    /// <summary>
    /// 从响应头解析 Retry-After（秒数或 HTTP 日期），解析失败返回 null。
    /// </summary>
    private static int? ParseRetryAfter(HttpResponseMessage resp)
    {
        var header = resp.Headers.RetryAfter;
        if (header is null) return null;

        // Retry-After 可能是秒数
        if (header.Delta is { } delta)
            return (int)delta.TotalSeconds;

        // 或 HTTP 日期（计算距今的秒数）
        if (header.Date is { } date)
        {
            var seconds = (int)(date - DateTimeOffset.UtcNow).TotalSeconds;
            return seconds > 0 ? seconds : null;
        }

        return null;
    }

    // ── 内部方法 ──────────────────────────────────

    private HttpRequestMessage CreateRequestMessage(ChatRequest request)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        return new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static ChatResponse ParseFullResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var message = root
                .GetProperty("choices")[0]
                .GetProperty("message");

            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";

            // 兼容不同供应商的思考字段命名：reasoning_content、reasoning
            var thinkingContent = GetThinkingContent(message);

            // 解析工具调用
            var toolCalls = ParseToolCalls(message);

            var usage = root.TryGetProperty("usage", out var u) ? u : default;

            return new ChatResponse
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                Content = content,
                ThinkingContent = thinkingContent,
                ToolCalls = toolCalls,
                PromptTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0,
                TotalTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0,
                Success = true
            };
        }
        catch (Exception ex)
        {
            return new ChatResponse { Success = false, ErrorMessage = $"响应解析失败: {ex.Message}" };
        }
    }

    /// <summary>
    /// 从 message 节点提取思考/推理内容，兼容不同供应商字段命名。
    /// 优先级：reasoning_content → reasoning
    /// </summary>
    private static string? GetThinkingContent(JsonElement message)
    {
        if (message.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            return rc.GetString();

        if (message.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String)
            return r.GetString();

        return null;
    }

    /// <summary>
    /// 从 message 节点解析 tool_calls 数组（非流式完整结果）。
    /// </summary>
    private static List<ToolCall>? ParseToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCallsEl) || toolCallsEl.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<ToolCall>(toolCallsEl.GetArrayLength());
        foreach (var tc in toolCallsEl.EnumerateArray())
        {
            var id = tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : "";
            var type = tc.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String ? typeEl.GetString() ?? "function" : "function";

            string name = "";
            string arguments = "";
            if (tc.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.Object)
            {
                name = fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() ?? "" : "";
                // arguments 可能缺失或为非字符串，统一取原始 JSON 兼容
                arguments = fnEl.TryGetProperty("arguments", out var argsEl)
                    ? argsEl.ValueKind == JsonValueKind.String
                        ? argsEl.GetString() ?? ""
                        : argsEl.GetRawText()
                    : "";
            }

            result.Add(new ToolCall
            {
                Id = id,
                Type = type,
                Function = new FunctionCall { Name = name, Arguments = arguments }
            });
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 从流式 chunk 提取增量片段列表，解析 content、reasoning_content 和 tool_calls。
    /// 单个 chunk 可能同时包含多种 delta。
    /// </summary>
    private static List<StreamDelta> ExtractDeltas(string data)
    {
        var deltas = new List<StreamDelta>();

        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // Token 用量（流式最后一个 chunk 可能携带 usage 字段）
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                var pt = usage.TryGetProperty("prompt_tokens", out var ptEl) ? ptEl.GetInt32() : 0;
                var ct = usage.TryGetProperty("completion_tokens", out var ctEl) ? ctEl.GetInt32() : 0;
                var tt = usage.TryGetProperty("total_tokens", out var ttEl) ? ttEl.GetInt32() : 0;
                if (tt > 0)
                    deltas.Add(StreamDelta.Usage(pt, ct, tt));
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return deltas;

            var delta = choices[0].GetProperty("delta");

            // 思考字段：reasoning_content → reasoning
            if (TryGetThinking(delta, out var thinking))
                deltas.Add(StreamDelta.Thinking(thinking));

            // 工具调用增量
            if (delta.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCallsEl.EnumerateArray())
                {
                    var toolDelta = ParseToolCallDelta(tc);
                    if (toolDelta is not null)
                        deltas.Add(StreamDelta.ToolCall(toolDelta));
                }
            }

            // 正文内容
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (text is not null)
                    deltas.Add(StreamDelta.Content(text));
            }
        }
        catch
        {
            // 解析失败静默跳过单个 chunk
        }

        return deltas;
    }

    /// <summary>
    /// 解析流式 delta 中的单个 tool_call 增量。
    /// </summary>
    private static ToolCallDelta? ParseToolCallDelta(JsonElement tc)
    {
        var index = tc.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
            ? idxEl.GetInt32()
            : 0;

        var id = tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
        var type = tc.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String ? typeEl.GetString() : null;

        string? name = null;
        string? arguments = null;
        if (tc.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.Object)
        {
            name = fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() : null;
            // arguments 可能为非字符串（部分模型不规范），统一取原始文本
            if (fnEl.TryGetProperty("arguments", out var argsEl))
            {
                arguments = argsEl.ValueKind == JsonValueKind.String
                    ? argsEl.GetString()
                    : argsEl.ValueKind == JsonValueKind.Null ? null : argsEl.GetRawText();
            }
        }

        return new ToolCallDelta
        {
            Index = index,
            Id = id,
            Type = type,
            Function = new FunctionCallDelta { Name = name, Arguments = arguments }
        };
    }

    /// <summary>
    /// 从 delta 节点提取思考片段，兼容 reasoning_content 和 reasoning 命名。
    /// </summary>
    private static bool TryGetThinking(JsonElement delta, out string? thinking)
    {
        thinking = null;

        if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
        {
            thinking = rc.GetString();
            return thinking is not null;
        }

        if (delta.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String)
        {
            thinking = r.GetString();
            return thinking is not null;
        }

        return false;
    }

    private static ChatResponse ErrorFrom(string body, System.Net.HttpStatusCode status)
    {
        return new ChatResponse
        {
            Success = false,
            ErrorMessage = $"HTTP {(int)status}: {ExtractErrorMessage(body, status)}"
        };
    }

    /// <summary>
    /// 从错误响应体中提取 message 字段，失败则返回原始 body（截断）。
    /// </summary>
    private static string ExtractErrorMessage(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
                return err.GetRawText();
            }
        }
        catch { /* ignore */ }

        return body.Length > 500 ? body[..500] + "..." : body;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
