namespace EasyCon.Core.LLM;

public interface IChatClient : IDisposable
{
    /// <summary>
    /// 发送聊天请求，返回完整响应。
    /// </summary>
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// 发送聊天请求并以流式方式返回文本片段。
    /// 每个 yield 的字符串是一个增量 token，拼接后得到完整回复。
    /// </summary>
    IAsyncEnumerable<string> SendStreamAsync(ChatRequest request, CancellationToken ct = default);
}
