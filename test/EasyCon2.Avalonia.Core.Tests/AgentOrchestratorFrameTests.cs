using EasyCon.Core.Capabilities;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.AiAgent;
using EasyCon2.Avalonia.AiAgent.Tools;
using EasyCon2.Avalonia.Services;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// get_frame 图片注入编排的回归测试。
/// 根因背景：旧实现把新帧图片写回首次 get_frame 的历史槽位（原地替换），
/// 导致图片与本次 tool 结果位置分离（模型按对话时间轴把它解读为"行动前画面"），
/// 且固定槽位会被滑窗静默裁掉。修复后契约：
/// 1. 新帧图永远紧跟本轮 tool 文本结果追加；
/// 2. 旧帧图降级为纯文本占位（保留位置，不再携带像素）；
/// 3. 滑窗截断保护最新帧三联 [assistant(tool_calls), tool, user(image)]；
/// 4. get_frame 与其它工具同轮时被拒绝（fail-closed，防并行竞态取到动作前旧帧）。
/// </summary>
[TestFixture]
public class AgentOrchestratorFrameTests
{
    // ── 假工具 ────────────────────────────────────────────

    /// <summary>模拟 get_frame：返回带 AttachedMessage 图片的成功结果，可计数执行次数。</summary>
    internal sealed class FakeFrameTool : IAiTool
    {
        public string Name => "get_frame";
        public string Description => "fake frame tool";
        public JsonSchema Parameters => new() { Type = "object" };
        public bool RequiresVision => true;
        public bool ReturnsImage => true;
        public int ExecutedCount;

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExecutedCount);
            var img = ChatMessage.User(
            [
                ContentPart.FromText("当前视频画面："),
                ContentPart.FromImageBase64("image/png", Convert.ToBase64String(Guid.NewGuid().ToByteArray()))
            ]);
            return Task.FromResult(ToolResult.Ok("已获取当前画面，正在分析...", img));
        }
    }

    /// <summary>模拟普通工具：返回固定文本结果。</summary>
    private sealed class FakeEchoTool(string name) : IAiTool
    {
        public string Name => name;
        public string Description => "fake echo tool";
        public JsonSchema Parameters => new() { Type = "object" };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok($"{name}-ok"));
    }

    /// <summary>按轮次脚本返回 delta，并记录每次请求供滑窗断言。</summary>
    private sealed class RecordingChatClient(params StreamDelta[][] calls) : IChatClient
    {
        private readonly Queue<StreamDelta[]> _script = new(calls);

        public List<ChatRequest> Requests { get; } = [];

        public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Success = true, Content = "" });

        public async IAsyncEnumerable<StreamDelta> SendStreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(request);
            foreach (var delta in _script.Dequeue())
            {
                await Task.Yield();
                yield return delta;
            }
        }

        public void Dispose() { }
    }

    /// <summary>1x1 PNG（合法最小图像，供帧编码器解码）。</summary>
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private sealed class FakeCaptureSource : ICaptureSource
    {
        public string? CaptureFrame(int x, int y, int width, int height) => TinyPngBase64;
    }

    private sealed class FakeToolCallService : IToolCallService
    {
        public string GetScriptContent() => "";
        public void WriteScriptContent(string content, bool append) { }
        public int EditScriptContent(string oldString, string newString, int count) => 0;
        public string? GetScriptPath() => null;
        public Task<bool> CompileScriptAsync() => Task.FromResult(true);
        public Task<string> FormatScriptAsync() => Task.FromResult("");
        public DeviceStatusInfo GetDeviceStatus() => new(true, true, true, false);
        public string? GetProjectTree() => null;
        public string? GetProjectDirectory() => null;
        public Task<bool> RunScriptAsync() => Task.FromResult(true);
        public void StopScript() { }
        public bool IsScriptRunning => false;

        public IPadInput? GetPadInput() => null;
        public ICaptureSource? GetCaptureSource() => new FakeCaptureSource();
        public IOcrService? GetOcrService() => null;
        public string GetRecentLogs(int maxLines) => "";
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

    private static bool HasImagePart(ChatMessage m) =>
        m.Content is List<ContentPart> parts && parts.Any(p => p.Type == "image_url");

    private static async Task<(List<ChatMessage> History, RecordingChatClient Client)> RunAsync(
        RecordingChatClient client,
        params IAiTool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
            registry.Register(tool);

        var orchestrator = new AgentOrchestrator(registry, null, _ => client);
        var history = new List<ChatMessage>();
        // 模型条目声明视觉能力（编排器从 provider+modelId 解析，不再接受宿主布尔）
        var provider = new ProviderConfig
        {
            Models = [new ModelInfo { Id = "test-model", Vision = true }]
        };
        await orchestrator.RunAsync(history, "test-model", provider, _ => { }, CancellationToken.None);
        return (history, client);
    }

    // ── 用例 ──────────────────────────────────────────────

    [Test]
    public async Task SecondGetFrame_AppendsImageAfterToolResult_AndDowngradesOldToPlaceholder()
    {
        var client = new RecordingChatClient(
            ToolCallRound(("c1", "get_frame")),
            ToolCallRound(("c2", "get_frame")),
            [StreamDelta.Content("完成")]);
        var frameTool = new FakeFrameTool();

        var (history, _) = await RunAsync(client, frameTool);

        // 期望布局（从 0）：
        // [0] assistant(tc1) [1] tool(帧1文本) [2] 占位文本（原帧1图）
        // [3] assistant(tc2) [4] tool(帧2文本) [5] user(帧2图，多模态) [6] assistant("完成")
        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.EqualTo(7), "两次 get_frame 应产生 7 条历史消息");

            // 旧帧图槽位降级为纯文本占位
            Assert.That(history[2].Role, Is.EqualTo("user"), "旧帧槽位应保留 user 角色");
            Assert.That(history[2].Content, Is.TypeOf<string>(), "旧帧图应降级为纯文本占位");
            Assert.That(history[2].Content?.ToString(), Does.Contain("旧画面已省略"));

            // 新帧图紧跟本轮 tool 文本结果，位于历史尾部
            Assert.That(history[5].Role, Is.EqualTo("user"), "新帧图应为 user 消息");
            Assert.That(history[5].Content, Is.TypeOf<List<ContentPart>>(), "新帧图应携带多模态内容");
            Assert.That(HasImagePart(history[5]), Is.True, "新帧消息应包含图片 part");
            Assert.That(history[4].Role, Is.EqualTo("tool"), "新帧图前一条应是本轮 tool 文本结果");
            Assert.That(history[3].ToolCalls, Is.Not.Null, "tool 结果前应是 assistant 工具调用消息");

            // 历史中至多一张真图
            Assert.That(history.Count(HasImagePart), Is.EqualTo(1), "历史中应至多保留一张真图");
        });
    }

    [Test]
    public async Task OldImagePlaceholder_ContainsNoImageParts()
    {
        var client = new RecordingChatClient(
            ToolCallRound(("c1", "get_frame")),
            ToolCallRound(("c2", "get_frame")),
            [StreamDelta.Content("完成")]);

        var (history, _) = await RunAsync(client, new FakeFrameTool());

        Assert.Multiple(() =>
        {
            Assert.That(history[2].Content?.ToString(), Does.Contain("旧画面已省略"), "旧帧槽位应为占位文本");
            Assert.That(history[2].Content?.ToString(), Does.Not.Contain("data:image"), "占位文本不得残留图片数据");
            Assert.That(HasImagePart(history[2]), Is.False, "占位消息不得携带图片 part");
        });
    }

    [Test]
    public async Task WindowTruncation_KeepsLatestFrameTriplet()
    {
        // 帧 1 轮 + 20 轮 echo（每轮 2 条）+ 收尾轮 → 历史 44 条，滑窗必然裁掉早期帧图
        var calls = new List<StreamDelta[]> { ToolCallRound(("c0", "get_frame")) };
        for (var i = 0; i < 20; i++)
            calls.Add(ToolCallRound(($"e{i}", "echo")));
        calls.Add([StreamDelta.Content("完成")]);

        var client = new RecordingChatClient([.. calls]);
        var frameTool = new FakeFrameTool();

        var (history, _) = await RunAsync(client, frameTool, new FakeEchoTool("echo"));

        Assert.Multiple(() =>
        {
            Assert.That(history.Count, Is.GreaterThan(AgentOrchestrator.MaxHistoryMessages),
                "前置条件：历史必须超过滑窗上限");

            // 最后一次请求必须仍包含最新帧图，且三联顺序完整
            var lastRequest = client.Requests[^1];
            var imageIndex = lastRequest.Messages.FindIndex(HasImagePart);
            Assert.That(imageIndex, Is.GreaterThanOrEqualTo(0),
                "滑窗截断不得静默丢弃最新帧图（tool 文本声称已获取画面，图必须随行）");
            Assert.That(lastRequest.Messages[imageIndex - 1].Role, Is.EqualTo("tool"),
                "帧图前应是 tool 文本消息");
            Assert.That(lastRequest.Messages[imageIndex - 2].ToolCalls, Is.Not.Null,
                "tool 文本前应是 assistant 工具调用消息（三联完整）");
        });
    }

    [Test]
    public async Task MixedRound_get_frame_Rejected_OthersExecuted()
    {
        var client = new RecordingChatClient(
            ToolCallRound(("cf", "get_frame"), ("ce", "echo")),
            [StreamDelta.Content("完成")]);
        var frameTool = new FakeFrameTool();

        var (history, _) = await RunAsync(client, frameTool, new FakeEchoTool("echo"));

        var frameResult = history.Single(m => m.Role == "tool" && m.ToolCallId == "cf");
        var echoResult = history.Single(m => m.Role == "tool" && m.ToolCallId == "ce");
        Assert.Multiple(() =>
        {
            Assert.That(frameTool.ExecutedCount, Is.EqualTo(0), "混调时 get_frame 不得执行（防并行竞态）");
            Assert.That(frameResult.Content, Does.Contain("[错误]"), "get_frame 应收到明确错误");
            Assert.That(frameResult.Content, Does.Contain("单独"), "错误信息应指导模型单独调用");
            Assert.That(echoResult.Content, Is.EqualTo("echo-ok"), "同轮其它工具应正常执行");
            Assert.That(history.Any(HasImagePart), Is.False, "被拒绝的 get_frame 不得产生图片消息");
        });
    }

    [Test]
    public async Task GetFrameResult_ContainsCaptureTimestamp()
    {
        var tool = new GetFrameTool(() => new FakeToolCallService().GetCaptureSource());

        var result = await tool.ExecuteAsync([]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Content, Does.Contain("已获取当前画面"));
            Assert.That(result.Content, Does.Contain("捕获于"), "结果应携带捕获时刻供模型判断帧新旧");
            Assert.That(result.AttachedMessage, Is.Not.Null);
        });
    }
}