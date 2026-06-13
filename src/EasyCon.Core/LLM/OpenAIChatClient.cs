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

    public async IAsyncEnumerable<string> SendStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        request.Stream = true;

        // 发起请求，将异常信息提取到局部变量，避免在 catch 块中 yield
        HttpResponseMessage? resp = null;
        string? errorMsg = null;

        try
        {
            using var msg = CreateRequestMessage(request);
            resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            yield break;
        }
        catch (HttpRequestException ex)
        {
            errorMsg = $"\n[错误] {ex.Message}";
        }

        if (errorMsg is not null)
        {
            yield return errorMsg;
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

                var delta = ExtractDelta(data);
                if (delta is not null)
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

            var content = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            var usage = root.TryGetProperty("usage", out var u) ? u : default;

            return new ChatResponse
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                Content = content,
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

    private static string? ExtractDelta(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;

            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content))
                return content.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static ChatResponse ErrorFrom(string body, System.Net.HttpStatusCode status)
    {
        string? msg = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            msg = doc.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();
        }
        catch { /* ignore */ }

        return new ChatResponse
        {
            Success = false,
            ErrorMessage = $"HTTP {(int)status}: {msg ?? body}"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
