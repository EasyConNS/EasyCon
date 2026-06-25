using EasyCon.Core.LLM.Messages;
using EasyCon2.Avalonia.Core.AiAgent;
using EasyCon2.Avalonia.Core.AiAgent.Tools;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 验证 AgentOrchestrator.BuildMessages 的核心不变量：
/// messages[0] 为 system 角色的基础角色提示词（干净，不包裹），
/// 后续为 user 角色 + &lt;system-reminder&gt; 包裹的技能/反思/截断/AGENTS.md 上下文，
/// 最后为对话历史。
/// </summary>
[TestFixture]
public class AgentOrchestratorBuildMessagesTests
{
    private const int MaxHistoryMessages = AgentOrchestrator.MaxHistoryMessages; // 40

    private static AgentOrchestrator NewOrchestrator() => new(new ToolRegistry());

    private static int SystemCount(List<ChatMessage> messages) =>
        messages.Count(m => m.Role == "system");

    private static string IdentityText(List<ChatMessage> messages) =>
        messages[0].Content?.ToString() ?? "";

    private static string BaseRoleText(List<ChatMessage> messages) =>
        messages[1].Content?.ToString() ?? "";

    private static List<ChatMessage> BuildUserHistory(int count)
    {
        var list = new List<ChatMessage>(count);
        for (var i = 0; i < count; i++)
            list.Add(ChatMessage.User($"msg-{i}"));
        return list;
    }

    // ── 普通路径 ──────────────────────────────────────────

    [Test]
    public void FreshConversation_HasSystemBaseRole_AndAgentsMdContext()
    {
        var orchestrator = NewOrchestrator();
        var history = new List<ChatMessage> { ChatMessage.User("你好") };

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            // [0] system: 身份标识（仅一句）
            Assert.That(messages[0].Role, Is.EqualTo("system"), "身份标识应为 system 角色");
            Assert.That(IdentityText(messages), Is.EqualTo("你是 EasyCon（伊机控）的 AI 助手。"), "身份标识应仅为第一句");
            Assert.That(IdentityText(messages), Does.Not.Contain("<system-reminder>"), "身份标识不应包裹");

            // [1] system: 基础角色提示词
            Assert.That(messages[1].Role, Is.EqualTo("system"), "基础角色应为 system 角色");
            Assert.That(BaseRoleText(messages), Does.Contain("EasyCon"), "基础角色应包含 EasyCon");

            // 两条 system 消息
            Assert.That(SystemCount(messages), Is.EqualTo(2), "应有两条 system 消息（身份 + 基础角色）");

            // AGENTS.md 上下文
            var agentsMsg = messages[^2];
            Assert.That(agentsMsg.Role, Is.EqualTo("user"), "AGENTS.md 上下文应为 user 角色");
            Assert.That(agentsMsg.Content?.ToString(), Does.Contain("<system-reminder>"));
            Assert.That(agentsMsg.Content?.ToString(), Does.Contain("As you answer the user's questions"));

            // 历史消息在最后
            Assert.That(messages[^1].Role, Is.EqualTo("user"));
            Assert.That(messages[^1].Content?.ToString(), Is.EqualTo("你好"));
        });
    }

    // ── 反思路径 ──────────────────────────────────────────

    [Test]
    public void ReflectionPath_AddsReflectionAsSeparateUserMessage()
    {
        var orchestrator = NewOrchestrator();
        orchestrator.TestHook_SimulateReflectionTrigger(); // 连续两次失败 → 触发反思
        var history = new List<ChatMessage> { ChatMessage.User("重试一下") };

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(SystemCount(messages), Is.EqualTo(2), "反思不应新增 system 消息");
            // 反思提示应作为独立的 user 消息存在，包裹在 system-reminder 中
            var reflectionMsg = messages.FirstOrDefault(m =>
                m.Role == "user" && m.Content?.ToString()?.Contains("反思") == true);
            Assert.That(reflectionMsg, Is.Not.Null, "反思提示应作为独立 user 消息出现");
            Assert.That(reflectionMsg!.Content?.ToString(), Does.Contain("<system-reminder>"), "反思提示应包裹 system-reminder");
        });
    }

    // ── 截断路径 ──────────────────────────────────────────

    [Test]
    public void AtThreshold_NoTruncation_HasSystemAndAgents()
    {
        var orchestrator = NewOrchestrator();
        var history = BuildUserHistory(MaxHistoryMessages); // 恰好 40，不截断

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(SystemCount(messages), Is.EqualTo(2));
            // 不应有截断说明
            var allText = string.Join("\n", messages.Select(m => m.Content?.ToString()));
            Assert.That(allText, Does.Not.Contain("已被省略"));
            // identity + base role + AGENTS.md + 40 history
            Assert.That(messages.Count, Is.EqualTo(MaxHistoryMessages + 3));
        });
    }

    [Test]
    public void OverThreshold_TruncatesWithNoticeAsSeparateUserMessage()
    {
        var orchestrator = NewOrchestrator();
        var history = BuildUserHistory(MaxHistoryMessages + 10); // 50 条

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            Assert.That(SystemCount(messages), Is.EqualTo(2));
            // 截断说明应作为独立 user 消息
            var truncationMsg = messages.FirstOrDefault(m =>
                m.Role == "user" && m.Content?.ToString()?.Contains("已被省略") == true);
            Assert.That(truncationMsg, Is.Not.Null, "截断说明应作为独立 user 消息出现");
            Assert.That(truncationMsg!.Content?.ToString(), Does.Contain("<system-reminder>"), "截断说明应包裹 system-reminder");
            // 截断后正文不超过窗口上限（identity + base role + truncation + AGENTS.md = 4 条前导）
            Assert.That(messages.Count - 4, Is.AtMost(MaxHistoryMessages));
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
            Assert.That(SystemCount(messages), Is.EqualTo(2));
            // 正文第一条（跳过 system identity、system base role、truncation、AGENTS.md）不得为 tool
            var bodyStart = messages.SkipWhile(m =>
                m.Role == "system" || (m.Content?.ToString()?.Contains("<system-reminder>") == true)).First();
            Assert.That(bodyStart.Role, Is.Not.EqualTo("tool"), "正文不得以孤儿 tool 开头");
        });
    }

    [Test]
    public void Truncation_KeepsConversationTail()
    {
        var orchestrator = NewOrchestrator();
        var history = BuildUserHistory(MaxHistoryMessages + 2); // 42 条

        var messages = orchestrator.BuildMessages(history);

        // 正文应保留最后 40 条（msg-2 .. msg-41），跳过 system + truncation + AGENTS.md
        var body = messages.Where(m =>
            m.Role != "system" && !(m.Content?.ToString()?.Contains("<system-reminder>") ?? false)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(body[0].Content?.ToString(), Is.EqualTo("msg-2"));
            Assert.That(body[^1].Content?.ToString(), Is.EqualTo("msg-41"));
            Assert.That(body, Has.Count.EqualTo(MaxHistoryMessages));
        });
    }

    // ── 轮次计数 ──────────────────────────────────────────

    [Test]
    public void RoundCount_AppearsInSkillContentUserMessage()
    {
        var orchestrator = NewOrchestrator();
        var history = new List<ChatMessage> { ChatMessage.User("继续") };

        var messages = orchestrator.BuildMessages(history, currentRound: 5);

        Assert.Multiple(() =>
        {
            Assert.That(SystemCount(messages), Is.EqualTo(2));
            // 轮次信息应在技能内容的 user 消息中
            var skillMsg = messages.FirstOrDefault(m =>
                m.Role == "user" && m.Content?.ToString()?.Contains("当前已使用 5 轮") == true);
            Assert.That(skillMsg, Is.Not.Null, "轮次计数应在技能内容 user 消息中");
            Assert.That(skillMsg!.Content?.ToString(), Does.Contain("<system-reminder>"), "技能内容应包裹 system-reminder");
        });
    }
}