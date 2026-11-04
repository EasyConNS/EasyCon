using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 编排层通用防线的回归测试：工具结果体积预算、并发调度（Exclusive 不重叠）、
/// vision 能力门控、流式 usage 请求参数、客户端工厂凭证隔离。
/// </summary>
[TestFixture]
public class AgentOrchestratorOrchestrationTests
{
    // ── 假工具 ────────────────────────────────────────────

    /// <summary>返回指定长度填充文本的普通工具。</summary>
    private sealed class FakeBulkTool(string name, int length) : IAiTool
    {
        public string Name => name;
        public string Description => "bulk";
        public JsonSchema Parameters => new() { Type = "object" };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok(new string('x', length)));
    }

    /// <summary>并发探针：执行期间计数重叠。同一时刻只允许一个 Exclusive 工具在跑。</summary>
    private sealed class ConcurrencyProbeTool(string name, ToolConcurrency concurrency) : IAiTool
    {
        private static int _inFlight;

        public string Name => name;
        public string Description => "probe";
        public JsonSchema Parameters => new() { Type = "object" };
        public ToolConcurrency Concurrency => concurrency;
        public int Violations;

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _inFlight) > 1)
                Interlocked.Increment(ref Violations);
            await Task.Delay(75, ct);
            Interlocked.Decrement(ref _inFlight);
            return ToolResult.Ok($"{name}-ok");
        }
    }

    /// <summary>按轮次脚本返回 delta 的假客户端。</summary>
    private sealed class ScriptedClient(params StreamDelta[][] calls) : IChatClient
    {
        private readonly Queue<StreamDelta[]> _script = new(calls);

        public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Success = true, Content = "" });

        public async IAsyncEnumerable<StreamDelta> SendStreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
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

    private static async Task<List<ChatMessage>> RunAsync(RecordingTools tools, ScriptedClient client, bool visionSupported = true)
    {
        var orchestrator = new AgentOrchestrator(tools.Registry, null, _ => client);
        var history = new List<ChatMessage>();
        // 视觉能力由编排器从模型条目解析；visionSupported 参数保留用于用例开关
        var provider = new ProviderConfig
        {
            Models = [new ModelInfo { Id = "test-model", Vision = visionSupported }]
        };
        await orchestrator.RunAsync(history, "test-model", provider, _ => { }, CancellationToken.None);
        return history;
    }

    /// <summary>工具注册容器，测试后可查执行计数。</summary>
    private sealed class RecordingTools(params IAiTool[] tools)
    {
        public ToolRegistry Registry { get; } = CreateRegistry(tools);

        private static ToolRegistry CreateRegistry(IAiTool[] tools)
        {
            var registry = new ToolRegistry();
            foreach (var tool in tools)
                registry.Register(tool);
            return registry;
        }
    }

    // ── 用例 ──────────────────────────────────────────────

    [Test]
    public async Task ToolResult_OverBudget_IsTruncatedWithNotice()
    {
        var client = new ScriptedClient(
            ToolCallRound(("c1", "bulk")),
            [StreamDelta.Content("完成")]);

        var history = await RunAsync(new RecordingTools(new FakeBulkTool("bulk", AgentOrchestrator.MaxToolResultChars + 10_000)), client);

        var toolMsg = history.Single(m => m.Role == "tool");
        Assert.Multiple(() =>
        {
            Assert.That(toolMsg.Content?.ToString()?.Length, Is.LessThan(AgentOrchestrator.MaxToolResultChars + 200),
                "超预算结果必须截断后再入历史");
            Assert.That(toolMsg.Content?.ToString(), Does.Contain("已截断"), "截断必须带显式尾注");
        });
    }

    [Test]
    public async Task ToolResult_WithinBudget_PassesThroughIntact()
    {
        var client = new ScriptedClient(
            ToolCallRound(("c1", "bulk")),
            [StreamDelta.Content("完成")]);

        var history = await RunAsync(new RecordingTools(new FakeBulkTool("bulk", 1_000)), client);

        var toolMsg = history.Single(m => m.Role == "tool");
        Assert.That(toolMsg.Content?.ToString(), Has.Length.EqualTo(1_000), "预算内结果不得改动");
    }

    [Test]
    public async Task ExclusiveTools_InSameRound_NeverOverlap()
    {
        var p1 = new ConcurrencyProbeTool("p1", ToolConcurrency.Exclusive);
        var p2 = new ConcurrencyProbeTool("p2", ToolConcurrency.Exclusive);
        var client = new ScriptedClient(
            ToolCallRound(("c1", "p1"), ("c2", "p2")),
            [StreamDelta.Content("完成")]);

        var history = await RunAsync(new RecordingTools(p1, p2), client);

        Assert.Multiple(() =>
        {
            Assert.That(p1.Violations, Is.EqualTo(0), "Exclusive 工具执行期间不得有其它工具并发");
            Assert.That(p2.Violations, Is.EqualTo(0));
            Assert.That(history.Where(m => m.Role == "tool").Select(m => m.Content),
                Is.EquivalentTo(new[] { "p1-ok", "p2-ok" }), "两个工具均须执行成功");
        });
    }

    [Test]
    public async Task ParallelTools_InSameRound_BothExecute()
    {
        var p1 = new ConcurrencyProbeTool("p1", ToolConcurrency.Parallel);
        var p2 = new ConcurrencyProbeTool("p2", ToolConcurrency.Parallel);
        var client = new ScriptedClient(
            ToolCallRound(("c1", "p1"), ("c2", "p2")),
            [StreamDelta.Content("完成")]);

        var history = await RunAsync(new RecordingTools(p1, p2), client);

        Assert.That(history.Where(m => m.Role == "tool").Select(m => m.Content),
            Is.EquivalentTo(new[] { "p1-ok", "p2-ok" }), "只读工具应并行成组且均执行成功");
    }

    [Test]
    public async Task VisionUnsupported_get_frame_RejectedWithoutExecution()
    {
        var frameTool = new AgentOrchestratorFrameTests.FakeFrameTool();
        var client = new ScriptedClient(
            ToolCallRound(("c1", "get_frame")),
            [StreamDelta.Content("完成")]);

        var history = await RunAsync(new RecordingTools(frameTool), client, visionSupported: false);

        var toolMsg = history.Single(m => m.Role == "tool");
        Assert.Multiple(() =>
        {
            Assert.That(frameTool.ExecutedCount, Is.EqualTo(0), "视觉不可用时 get_frame 不得执行");
            Assert.That(toolMsg.Content?.ToString(), Does.Contain("[错误]"));
            Assert.That(toolMsg.Content?.ToString(), Does.Contain("视觉"));
            Assert.That(history.Any(m => m.Content is List<ContentPart>), Is.False, "不得产生图片消息");
        });
    }

    [Test]
    public void ChatRequest_StreamOptions_SerializedForWire()
    {
        var request = new ChatRequest
        {
            Model = "m",
            Stream = true,
            StreamOptions = new StreamOptions { IncludeUsage = true }
        };

        var json = JsonSerializer.Serialize(request);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"stream_options\""));
            Assert.That(json, Does.Contain("\"include_usage\":true"));
        });
    }

    [Test]
    public void ChatClientFactory_SameBaseUrlDifferentKey_DistinctClients()
    {
        try
        {
            var first = ChatClientFactory.Create(new ProviderConfig
            {
                Api = "openai-completions",
                BaseUrl = "https://factory-isolation-test.invalid/v1",
                ApiKey = "key-one"
            });
            var second = ChatClientFactory.Create(new ProviderConfig
            {
                Api = "openai-completions",
                BaseUrl = "https://factory-isolation-test.invalid/v1",
                ApiKey = "key-two"
            });

            Assert.That(ReferenceEquals(first, second), Is.False,
                "同址不同 Key 的供应商不得共享客户端（错误凭证）");
        }
        finally
        {
            ChatClientFactory.ClearCache();
        }
    }
}