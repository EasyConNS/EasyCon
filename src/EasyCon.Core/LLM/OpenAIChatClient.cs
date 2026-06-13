using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EasyCon.Core.LLM.Tools;

namespace EasyCon.Core.LLM;

public sealed class OpenAIChatClient : IChatClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    };

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
        using var msg = CreateRequestMessage(request);

        try
        {
            using var resp = await _http.SendAsync(msg, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return ErrorFrom(body, resp.StatusCode);

            return ParseFullResponse(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new ChatResponse { Success = false, ErrorMessage = "请求已取消" };
        }
        catch (Exception ex)
        {
            return new ChatResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async IAsyncEnumerable<StreamDelta> SendStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        request.Stream = true;

        // 发起请求，将异常信息提取到局部变量，避免在 catch 块中 yield
        HttpResponseMessage? resp = null;
        string? errorMsg = null;

        try
        {
            using var msg = CreateRequestMessage(request);
            resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                errorMsg = ExtractErrorMessage(errBody, resp.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            yield break;
        }
        catch (HttpRequestException ex)
        {
            errorMsg = ex.Message;
        }

        if (errorMsg is not null)
        {
            yield return StreamDelta.Error(errorMsg);
            yield break;
        }

        using (resp!)
        await using (var stream = await resp!.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                foreach (var delta in ExtractDeltas(data))
                    yield return delta;
            }
        }
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
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return deltas;

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
