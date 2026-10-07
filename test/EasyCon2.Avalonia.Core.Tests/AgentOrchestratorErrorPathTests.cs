using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon2.Avalonia.AiAgent;
using EasyCon2.Avalonia.AiAgent.Tools;
using System.Runtime.CompilerServices;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// AgentOrchestrator 错误链路：供应商/模型配置错误时，错误信息必须进入
/// Completed.FinalContent（UI 以它为最终内容真源），不允许静默。
/// 通过构造函数注入 fake client 覆盖，不依赖网络。
/// </summary>
[TestFixture]
public class AgentOrchestratorErrorPathTests
{
    /// <summary>所有调用返回同一组 delta。</summary>
    private sealed class FakeChatClient(params StreamDelta[] deltas) : IChatClient
    {
        public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Success = true, Content = "" });

        public async IAsyncEnumerable<StreamDelta> SendStreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var delta in deltas)
            {
                await Task.Yield();
                yield return delta;
            }
        }

        public void Dispose() { }
    }

    /// <summary>每次 SendStreamAsync 调用按脚本依次返回一组 delta，覆盖断线重连路径。</summary>
    private sealed class ScriptedChatClient(params StreamDelta[][] calls) : IChatClient
    {
        private readonly Queue<StreamDelta[]> _script = new(calls);

        public int CallCount { get; private set; }

        public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Success = true, Content = "" });

        public async IAsyncEnumerable<StreamDelta> SendStreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            foreach (var delta in _script.Dequeue())
            {
                await Task.Yield();
                yield return delta;
            }
        }

        public void Dispose() { }
    }

    private static async Task<(List<AgentEvent> Events, List<ChatMessage> History)> RunAsync(IChatClient client)
    {
        var orchestrator = new AgentOrchestrator(new ToolRegistry(), null, _ => client);
        var events = new List<AgentEvent>();
        var history = new List<ChatMessage>();
        await orchestrator.RunAsync(history, "test-model", new ProviderConfig(), events.Add, CancellationToken.None);
        return (events, history);
    }

    [Test]
    public async Task ErrorDelta_IsPropagatedIntoFinalContent()
    {
        var (events, history) = await RunAsync(
            new FakeChatClient(StreamDelta.Error("HTTP 401: Unauthorized")));

        var completed = events.OfType<AgentEvent.Completed>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(completed.FinalContent, Does.Contain("[错误] HTTP 401: Unauthorized"));
            // 错误交代必须写入历史：模型下轮需要知道自己上轮失败过（对齐契约，2026-09 起）
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("assistant"));
            Assert.That(history[0].Content?.ToString(), Does.Contain("HTTP 401"));
        });
    }

    [Test]
    public async Task ErrorAfterPartialContent_PreservesBothInFinalContent()
    {
        var (events, _) = await RunAsync(
            new FakeChatClient(StreamDelta.Content("部分内容"), StreamDelta.Error("流内报错")));

        var completed = events.OfType<AgentEvent.Completed>().Single();
        Assert.That(completed.FinalContent, Is.EqualTo("部分内容[错误] 流内报错"));
    }

    [Test]
    public async Task EmptyStream_ProducesEmptyFinalContent()
    {
        // 空回复本身由 ViewModel 层兜底为可见的错误提示；
        // 编排器侧契约：FinalContent 如实传递空内容，历史落一条空回复交代供模型知悉
        var (events, history) = await RunAsync(new FakeChatClient());

        var completed = events.OfType<AgentEvent.Completed>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(completed.FinalContent, Is.Empty);
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].Content?.ToString(), Does.Contain("空回复"));
        });
    }

    [Test]
    public async Task NormalContent_IsDeliveredAndRecordedInHistory()
    {
        var (events, history) = await RunAsync(
            new FakeChatClient(StreamDelta.Content("你好"), StreamDelta.Content("！")));

        var completed = events.OfType<AgentEvent.Completed>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(completed.FinalContent, Is.EqualTo("你好！"));
            Assert.That(history, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task RetriableError_DiscardsPartialDataAndRetriesSameRound()
    {
        // 断线前已收到的部分内容必须被丢弃，重发后不得与新一轮内容重复累积
        var client = new ScriptedChatClient(
            [StreamDelta.Content("partial"), StreamDelta.Error("断线", retryable: true)],
            [StreamDelta.Content("ok")]);

        var (events, _) = await RunAsync(client);

        Assert.Multiple(() =>
        {
            Assert.That(client.CallCount, Is.EqualTo(2));
            var retry = events.OfType<AgentEvent.Retrying>().Single();
            Assert.That(retry.Attempt, Is.EqualTo(1));
            Assert.That(retry.MaxAttempts, Is.EqualTo(2));
            var completed = events.OfType<AgentEvent.Completed>().Single();
            Assert.That(completed.FinalContent, Is.EqualTo("ok"));
        });
    }
}