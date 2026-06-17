using EasyCon.Core.LLM.Messages;
using EasyCon2.Avalonia.Core.AiAgent;
using EasyCon2.Avalonia.Core.AiAgent.Tools;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 验证 AgentOrchestrator.BuildMessages 的核心不变量：
/// 无论走哪条分支（普通 / 反思 / 截断），输出都只含一条 system 消息且位于 messages[0]。
/// 这直接防止 Qwen/DashScope 抛出 "System message must be at the beginning."。
/// </summary>
[TestFixture]
public class AgentOrchestratorBuildMessagesTests
{
    private const int MaxHistoryMessages = AgentOrchestrator.MaxHistoryMessages; // 40

    private static AgentOrchestrator NewOrchestrator() => new(new ToolRegistry());

    private static int SystemCount(List<ChatMessage> messages) =>
        messages.Count(m => m.Role == "system");

    private static string SystemText(List<ChatMessage> messages) =>
        messages[0].Content?.ToString() ?? "";

    private static List<ChatMessage> BuildUserHistory(int count)
    {
        var list = new List<ChatMessage>(count);
        for (var i = 0; i < count; i++)
            list.Add(ChatMessage.User($"msg-{i}"));
        return list;
    }

    // ── 普通路径 ──────────────────────────────────────────

    [Test]
    public void FreshConversation_HasSingleSystemAtZero()
    {
        var orchestrator = NewOrchestrator();
        var history = new List<ChatMessage> { ChatMessage.User("你好") };

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(SystemCount(messages), Is.EqualTo(1), "不应有多余 system 消息");
            Assert.That(messages[1].Role, Is.EqualTo("user"));
            Assert.That(messages, Has.Count.EqualTo(2));
        });
    }

    // ── 反思路径 ──────────────────────────────────────────

    [Test]
    public void ReflectionPath_HasSingleSystemAtZero_WithFoldedReflection()
    {
        var orchestrator = NewOrchestrator();
        orchestrator.TestHook_SimulateReflectionTrigger(); // 连续两次失败 → 触发反思
        var history = new List<ChatMessage> { ChatMessage.User("重试一下") };

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(SystemCount(messages), Is.EqualTo(1), "反思提示必须合并进同一条 system，不得新增");
            Assert.That(SystemText(messages), Does.Contain("反思"), "反思提示应并入 system 文本");
        });
    }

    // ── 截断路径 ──────────────────────────────────────────

    [Test]
    public void AtThreshold_NoTruncation()
    {
        var orchestrator = NewOrchestrator();
        var history = BuildUserHistory(MaxHistoryMessages); // 恰好 40，不截断

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(SystemCount(messages), Is.EqualTo(1));
            Assert.That(SystemText(messages), Does.Not.Contain("已被省略"));
            Assert.That(messages, Has.Count.EqualTo(MaxHistoryMessages + 1)); // system + 40
        });
    }

    [Test]
    public void OverThreshold_TruncatesWithSingleSystemAndNotice()
    {
        var orchestrator = NewOrchestrator();
        var history = BuildUserHistory(MaxHistoryMessages + 10); // 50 条

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(SystemCount(messages), Is.EqualTo(1), "截断说明必须合并进 system，不得新增 system 消息");
            Assert.That(SystemText(messages), Does.Contain("已被省略"));
            // 截断后正文不超过窗口上限
            Assert.That(messages.Count - 1, Is.AtMost(MaxHistoryMessages));
        });
    }

    [Test]
    public void Truncation_NeverStartsOnOrphanedToolMessage()
    {
        // 构造 42 条历史，使截断点（skip = 42-40 = 2）恰好落在 tool 消息上。
        var history = new List<ChatMessage>
        {
            ChatMessage.User("u0"),
            ChatMessage.Assistant("a1"),
            ChatMessage.Tool("call-1", "result", "fn"), // index 2 — 孤儿 tool 目标
            ChatMessage.User("u3")
        };
        while (history.Count < 42)
            history.Add(ChatMessage.User($"fill-{history.Count}"));

        var orchestrator = NewOrchestrator();
        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[1].Role, Is.Not.EqualTo("tool"), "正文不得以孤儿 tool 开头");
            Assert.That(SystemCount(messages), Is.EqualTo(1));
        });
    }

    [Test]
    public void Truncation_KeepsConversationTail()
    {
        var orchestrator = NewOrchestrator();
        var history = BuildUserHistory(MaxHistoryMessages + 2); // 42 条

        var messages = orchestrator.BuildMessages(history);

        // 正文应保留最后 40 条（msg-2 .. msg-41）
        var body = messages.Skip(1).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(body[0].Content?.ToString(), Is.EqualTo("msg-2"));
            Assert.That(body[^1].Content?.ToString(), Is.EqualTo("msg-41"));
            Assert.That(body, Has.Count.EqualTo(MaxHistoryMessages));
        });
    }

    // ── 轮次计数 ──────────────────────────────────────────

    [Test]
    public void RoundCount_IsAppendedToSystemMessage()
    {
        var orchestrator = NewOrchestrator();
        var history = new List<ChatMessage> { ChatMessage.User("继续") };

        var messages = orchestrator.BuildMessages(history, currentRound: 5);

        Assert.Multiple(() =>
        {
            Assert.That(SystemCount(messages), Is.EqualTo(1));
            Assert.That(SystemText(messages), Does.Contain("当前已使用 5 轮"));
        });
    }
}
