using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.AiAgent;
using EasyCon2.Avalonia.AiAgent.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 行为对齐机制的回归测试：失败检测走结构化状态（而非结果文本子串）、
/// 重复调用按「工具名+参数」签名检测（不受结果文本影响）、
/// 终止原因与中间轮助手文本必须写入对话历史、反思提醒贴着失败现场注入并携带现场数据。
/// </summary>
[TestFixture]
public class AgentAlignmentTests
{
    // ── 假工具 ────────────────────────────────────────────

    /// <summary>返回指定状态与文本的工具（Status 是编排器失败检测的唯一依据）。</summary>
    private sealed class FakeStatusTool(string name, ToolResultStatus status, string text) : IAiTool
    {
        public string Name => name;
        public string Description => "status tool";
        public JsonSchema Parameters => new() { Type = "object" };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(status switch
            {
                ToolResultStatus.Error => ToolResult.Error(text),
                ToolResultStatus.Retryable => ToolResult.Retryable(text, "提示"),
                _ => ToolResult.Ok(text)
            });
    }

    /// <summary>每次调用返回唯一文本的工具（用于区分"调用重复"与"结果重复"）。</summary>
    private sealed class FakeCountingTool(string name) : IAiTool
    {
        private int _n;
        public string Name => name;
        public string Description => "counting tool";
        public JsonSchema Parameters => new() { Type = "object" };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok($"result-{Interlocked.Increment(ref _n)}"));
    }

    /// <summary>记录每次请求的假客户端；可注入流式挂起行为。</summary>
    private sealed class RecordingClient(params StreamDelta[][] calls) : IChatClient
    {
        private readonly Queue<StreamDelta[]> _script = new(calls);
        public List<ChatRequest> Requests { get; } = [];
        public bool Hang { get; init; }

        public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Success = true, Content = "" });

        public async IAsyncEnumerable<StreamDelta> SendStreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
                yield break;
            }
            foreach (var delta in _script.Dequeue())
            {
                await Task.Yield();
                yield return delta;
            }
        }

        public void Dispose() { }
    }

    // ── 辅助 ──────────────────────────────────────────────

    private static StreamDelta[] ToolCallRound(params (string Id, string Tool)[] calls) =>
        calls.Select((c, i) => StreamDelta.ToolCall(new ToolCallDelta
        {
            Index = i,
            Id = c.Id,
            Type = "function",
            Function = new FunctionCallDelta { Name = c.Tool, Arguments = "{}" }
        })).ToArray();

    private static bool IsReflectionNotice(ChatMessage m) =>
        m.Role == "user" && m.Content?.ToString()?.Contains("反思") == true;

    private static async Task<(List<ChatMessage> History, RecordingClient Client)> RunAsync(
        RecordingClient client, params IAiTool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
            registry.Register(tool);
        var orchestrator = new AgentOrchestrator(registry, null, _ => client);
        var history = new List<ChatMessage>();
        await orchestrator.RunAsync(history, "test-model", new ProviderConfig(), _ => { }, CancellationToken.None);
        return (history, client);
    }

    // ── 失败检测 ──────────────────────────────────────────

    [Test]
    public async Task RetryableFailures_TriggerReflection()
    {
        // 编译失败走 Retryable，文本不含 [错误] —— 必须照常计入失败并触发反思
        var client = new RecordingClient(
            ToolCallRound(("c1", "compile")),
            ToolCallRound(("c2", "compile")),
            [StreamDelta.Content("完成")]);

        var (_, captured) = await RunAsync(client, new FakeStatusTool("compile", ToolResultStatus.Retryable, "编译失败。\n[提示] 查看错误"));

        Assert.That(captured.Requests[^1].Messages.Any(IsReflectionNotice),
            "连续两次 Retryable 失败必须触发反思提醒");
    }

    [Test]
    public async Task SuccessTextContainingErrorMarker_DoesNotTriggerReflection()
    {
        // 结果文本里出现字面 [错误] 不代表工具失败：检测只认结构化状态
        var client = new RecordingClient(
            ToolCallRound(("c1", "logs")),
            ToolCallRound(("c2", "logs")),
            [StreamDelta.Content("完成")]);

        var (_, captured) = await RunAsync(client, new FakeStatusTool("logs", ToolResultStatus.Success, "运行日志：[错误] 已被上层捕获并恢复"));

        Assert.That(captured.Requests.Any(r => r.Messages.Any(IsReflectionNotice)),
            Is.False, "成功结果不得因文本样式被误判为失败");
    }

    // ── 重复调用检测 ──────────────────────────────────────

    [Test]
    public async Task RepeatedIdenticalCalls_TriggerReflection_EvenWithUniqueResults()
    {
        // 同参调用 ×3：结果文本每次都不同（时间戳类凭据），签名检测必须照常命中
        var client = new RecordingClient(
            ToolCallRound(("c1", "frame")),
            ToolCallRound(("c2", "frame")),
            ToolCallRound(("c3", "frame")),
            [StreamDelta.Content("完成")]);

        var (_, captured) = await RunAsync(client, new FakeCountingTool("frame"));

        Assert.That(captured.Requests[^1].Messages.Any(IsReflectionNotice),
            "连续三次同签名调用必须触发反思提醒（不受结果文本影响）");
    }

    [Test]
    public async Task DifferentCalls_WithIdenticalResults_DoNotTriggerReflection()
    {
        // 不同工具交替调用、结果恰好同文：不是行为循环，不得触发
        var client = new RecordingClient(
            ToolCallRound(("c1", "toolA")),
            ToolCallRound(("c2", "toolB")),
            ToolCallRound(("c3", "toolA")),
            ToolCallRound(("c4", "toolB")),
            [StreamDelta.Content("完成")]);

        var (_, captured) = await RunAsync(client,
            new FakeStatusTool("toolA", ToolResultStatus.Success, "ok"),
            new FakeStatusTool("toolB", ToolResultStatus.Success, "ok"));

        Assert.That(captured.Requests.Any(r => r.Messages.Any(IsReflectionNotice)),
            Is.False, "交替调用不同工具不构成重复行为");
    }

    // ── 历史交代 ──────────────────────────────────────────

    [Test]
    public async Task Stop_CommitsReasonIntoHistory()
    {
        var client = new RecordingClient { Hang = true };
        var orchestrator = new AgentOrchestrator(new ToolRegistry(), null, _ => client);
        var history = new List<ChatMessage>();
        using var cts = new CancellationTokenSource();

        var run = orchestrator.RunAsync(history, "test-model", new ProviderConfig(), _ => { }, cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();

        Assert.Multiple(() =>
        {
            Assert.CatchAsync<OperationCanceledException>(() => run);
            Assert.That(history.Any(m => m.Role == "assistant" && m.Content?.ToString()?.Contains("已停止") == true),
                "用户停止的事实必须写入历史，模型下轮才能知晓被打断");
        });
    }

    [Test]
    public async Task EmptyReply_CommitsMarkerIntoHistory()
    {
        // 一轮空流（无内容、无工具调用）
        var client = new RecordingClient([[]]);

        var (history, _) = await RunAsync(client);

        Assert.That(history.Any(m => m.Role == "assistant" && m.Content?.ToString()?.Contains("空回复") == true),
            "空回复的交代必须进历史，避免下轮模型对上一轮沉默零认知");
    }

    [Test]
    public async Task MidRoundText_CommittedToHistory_WithToolCalls()
    {
        var client = new RecordingClient(
            [
                StreamDelta.Content("先观察当前画面"),
                .. ToolCallRound(("c1", "frame"))
            ],
            [StreamDelta.Content("完成")]);

        var (history, _) = await RunAsync(client, new FakeCountingTool("frame"));

        var callMsg = history.FirstOrDefault(m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 });
        Assert.Multiple(() =>
        {
            Assert.That(callMsg, Is.Not.Null);
            Assert.That(callMsg!.Content?.ToString(), Does.Contain("先观察当前画面"),
                "带工具调用的中间轮助手文本不得被丢弃");
        });
    }

    // ── 反思提醒位置与现场数据 ────────────────────────────

    [Test]
    public async Task ReflectionNotice_AppendedAtEnd_WithTriggerContext()
    {
        var client = new RecordingClient(
            ToolCallRound(("c1", "compile")),
            ToolCallRound(("c2", "compile")),
            [StreamDelta.Content("完成")]);

        var (_, captured) = await RunAsync(client, new FakeStatusTool("compile", ToolResultStatus.Error, "[错误] 设备未连接"));

        var messages = captured.Requests[^1].Messages;
        var last = messages[^1];
        Assert.Multiple(() =>
        {
            Assert.That(IsReflectionNotice(last), "反思提醒应贴着失败现场：位于对话末尾");
            Assert.That(last.Content?.ToString(), Does.Contain("连续"), "提醒应携带现场数据（连续失败次数）");
        });
    }
}
