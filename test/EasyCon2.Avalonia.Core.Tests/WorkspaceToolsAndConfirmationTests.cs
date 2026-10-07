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
/// 工作区文件工具：路径安全、快照、精确替换语义。
/// </summary>
[TestFixture]
public class WorkspaceFileToolsTests
{
    private string _root = "";
    private ToolRegistry _registry = new();

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "easycon-wstest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _registry = new ToolRegistry();
        WorkspaceFileTools.RegisterAll(_registry, () => _root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Dictionary<string, JsonElement> Args(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in pairs)
        {
            var json = JsonSerializer.Serialize(value);
            dict[key] = JsonDocument.Parse(json).RootElement.Clone();
        }
        return dict;
    }

    [Test]
    public async Task WriteThenRead_RoundTrips()
    {
        var write = await _registry.Get("write_file")!.ExecuteAsync(Args(("path", "a/b.ecs"), ("content", "KEY A")));
        Assert.That(write.Status, Is.EqualTo(ToolResultStatus.Success));
        Assert.That(File.ReadAllText(Path.Combine(_root, "a", "b.ecs")), Is.EqualTo("KEY A"));

        var read = await _registry.Get("read_file")!.ExecuteAsync(Args(("path", "a/b.ecs")));
        Assert.That(read.Content, Is.EqualTo("KEY A"));
    }

    [Test]
    public async Task Write_RejectsPathEscape()
    {
        var result = await _registry.Get("write_file")!
            .ExecuteAsync(Args(("path", "../escape.txt"), ("content", "x")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
        Assert.That(File.Exists(Path.Combine(_root, "..", "escape.txt")), Is.False);
    }

    [Test]
    public async Task WriteOverExisting_CreatesSnapshot()
    {
        await _registry.Get("write_file")!.ExecuteAsync(Args(("path", "s.txt"), ("content", "v1")));
        await _registry.Get("write_file")!.ExecuteAsync(Args(("path", "s.txt"), ("content", "v2")));

        var historyDir = Path.Combine(_root, ".easycon", "history");
        var snapshots = Directory.GetFiles(historyDir);
        Assert.That(snapshots.Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(snapshots[0]), Is.EqualTo("v1"));
    }

    [Test]
    public async Task Edit_NonUniqueOldString_FailsWithoutReplaceAll()
    {
        await _registry.Get("write_file")!.ExecuteAsync(Args(("path", "e.txt"), ("content", "x\nx")));
        var edit = await _registry.Get("edit_file")!
            .ExecuteAsync(Args(("path", "e.txt"), ("old_string", "x"), ("new_string", "y")));
        Assert.That(edit.Status, Is.EqualTo(ToolResultStatus.Error));
        Assert.That(File.ReadAllText(Path.Combine(_root, "e.txt")), Is.EqualTo("x\nx"));

        var editAll = await _registry.Get("edit_file")!
            .ExecuteAsync(Args(("path", "e.txt"), ("old_string", "x"), ("new_string", "y"), ("replace_all", true)));
        Assert.That(editAll.Status, Is.EqualTo(ToolResultStatus.Success));
        Assert.That(File.ReadAllText(Path.Combine(_root, "e.txt")), Is.EqualTo("y\ny"));
    }

    [Test]
    public async Task Glob_ListsFiles_ExcludesHistory()
    {
        await _registry.Get("write_file")!.ExecuteAsync(Args(("path", "sub/1.ecs"), ("content", "a")));
        await _registry.Get("write_file")!.ExecuteAsync(Args(("path", "sub/2.txt"), ("content", "b")));

        var result = await _registry.Get("glob_files")!.ExecuteAsync(Args(("pattern", "**/*.ecs")));
        Assert.That(result.Content, Does.Contain("sub/1.ecs"));
        Assert.That(result.Content, Does.Not.Contain("2.txt"));
        Assert.That(result.Content, Does.Not.Contain(".easycon"));
    }

    [Test]
    public async Task AllTools_NoWorkspace_ReturnError()
    {
        foreach (var name in new[] { "glob_files", "read_file", "write_file", "edit_file" })
        {
            var registry = new ToolRegistry();
            WorkspaceFileTools.RegisterAll(registry, () => null);
            var result = await registry.Get(name)!.ExecuteAsync(Args(("path", "x"), ("content", "y"), ("pattern", "*")));
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error), name);
        }
    }
}

/// <summary>
/// 危险工具确认门：无回调 fail-closed；拒绝时工具不执行。
/// </summary>
[TestFixture]
public class ConfirmationGateTests
{
    private class DangerousTool : IAiTool
    {
        public int ExecuteCount;
        public string Name => "danger";
        public string Description => "危险工具（测试）";
        public JsonSchema Parameters => new() { Type = "object", Properties = new() };
        public bool RequiresConfirmation => true;

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExecuteCount);
            return Task.FromResult(ToolResult.Ok("executed"));
        }
    }

    private static List<ChatMessage> RunOrchestrator(AgentOrchestrator orchestrator, ScriptedChatClient client)
    {
        var history = new List<ChatMessage> { ChatMessage.User("做点危险的事") };
        orchestrator.RunAsync(history, "m", new ProviderConfig(), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        return history;
    }

    /// <summary>第一轮下发一次 danger 工具调用，第二轮给最终回复。</summary>
    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Queue<StreamDelta[]> _script = new(
        [
            new[]
            {
                StreamDelta.ToolCall(new ToolCallDelta
                {
                    Index = 0,
                    Id = "call-1",
                    Function = new FunctionCallDelta { Name = "danger", Arguments = "{}" }
                })
            },
            new[] { StreamDelta.Content("done") }
        ]);

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

    [Test]
    public void NoHandler_FailClosed_ToolNotExecuted()
    {
        var tool = new DangerousTool();
        var client = new ScriptedChatClient();
        var registry = new ToolRegistry().Register(tool);
        var orchestrator = new AgentOrchestrator(registry, null, clientFactory: _ => client);

        var history = RunOrchestrator(orchestrator, client);

        Assert.That(tool.ExecuteCount, Is.EqualTo(0));
        Assert.That(history.OfType<ChatMessage>().Any(m =>
            m.Role == "tool" && m.Content?.ToString()?.Contains("拒绝") == true),
            Is.True, "拒绝信息必须回传给模型");
    }

    [Test]
    public void RejectedByUser_ToolNotExecuted()
    {
        var tool = new DangerousTool();
        var client = new ScriptedChatClient();
        var registry = new ToolRegistry().Register(tool);
        var orchestrator = new AgentOrchestrator(registry, null,
            clientFactory: _ => client,
            projectDirectoryProvider: null,
            confirmationHandler: _ => Task.FromResult(false));

        RunOrchestrator(orchestrator, client);

        Assert.That(tool.ExecuteCount, Is.EqualTo(0));
    }

    [Test]
    public void ApprovedByUser_ToolExecutes()
    {
        var tool = new DangerousTool();
        var client = new ScriptedChatClient();
        var registry = new ToolRegistry().Register(tool);
        var orchestrator = new AgentOrchestrator(registry, null,
            clientFactory: _ => client,
            projectDirectoryProvider: null,
            confirmationHandler: _ => Task.FromResult(true));

        RunOrchestrator(orchestrator, client);

        Assert.That(tool.ExecuteCount, Is.EqualTo(1));
    }
}