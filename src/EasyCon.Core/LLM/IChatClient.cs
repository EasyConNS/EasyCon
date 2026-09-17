namespace EasyCon.Core.LLM;

public interface IChatClient : IDisposable
{
    /// <summary>
    /// 发送聊天请求，返回完整响应。
    /// </summary>
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// 发送聊天请求并以流式方式返回增量片段。
    /// 每个 yield 的 StreamDelta 标记了类型（正文/思考/错误），拼接后得到完整回复。
    /// </summary>
    IAsyncEnumerable<StreamDelta> SendStreamAsync(ChatRequest request, CancellationToken ct = default);
}