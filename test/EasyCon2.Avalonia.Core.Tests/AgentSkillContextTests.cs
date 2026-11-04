using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Skills;
using EasyCon.Core.LLM.Tools;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 技能注入上下文的收敛回归测试：
/// 1. 工具触发型技能只随「最近窗口内」的工具使用而激活，停用后降温，不得是只开不关的闩锁；
/// 2. execute_skill 收敛为 fork 技能专用通道，inline 技能指回 read_skill，消灭三份重复来源。
/// </summary>
[TestFixture]
public class AgentSkillContextTests
{
    private const string VisionBodyMarker = "视觉循环规则正文标记";

    private static SkillRegistry CreateVisionSkillRegistry()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill(new SkillManifest
        {
            Name = "vision-analysis",
            Description = "视觉分析",
            TriggerTools = new HashSet<string> { "get_frame" },
            Priority = 70,
        }, VisionBodyMarker));
        return registry;
    }

    private static ChatMessage ToolCallMessage(params string[] toolNames) =>
        ChatMessage.Assistant(toolNames.Select((n, i) => new ToolCall
        {
            Id = $"c{i}",
            Function = new FunctionCall { Name = n, Arguments = "{}" }
        }).ToList());

    [Test]
    public void ActiveToolInWindow_KeepsSkillInjected()
    {
        var assembler = new PromptAssembler(CreateVisionSkillRegistry());
        var history = new List<ChatMessage>
        {
            ChatMessage.User("看一下画面"),
            ToolCallMessage("get_frame"),
            ChatMessage.Tool("c0", "已获取当前画面")
        };

        var prompt = assembler.Build(history);

        Assert.That(prompt, Does.Contain(VisionBodyMarker), "工具在最近窗口内使用时，技能指令应注入");
    }

    [Test]
    public void SkillLatch_CoolsDownAfterToolDisuse()
    {
        var assembler = new PromptAssembler(CreateVisionSkillRegistry());
        var history = new List<ChatMessage> { ChatMessage.User("开始") };

        // 早期用过 get_frame，之后连续 8 轮只使用其它工具
        history.Add(ToolCallMessage("get_frame"));
        history.Add(ChatMessage.Tool("c0", "已获取当前画面"));
        for (var i = 0; i < 8; i++)
        {
            history.Add(ToolCallMessage("run_script"));
            history.Add(ChatMessage.Tool("c1", "已启动"));
        }

        var prompt = assembler.Build(history);

        Assert.That(prompt, Does.Not.Contain(VisionBodyMarker),
            "工具早已停用后技能不得再注入（激活不得是只开不关的闩锁）");
    }

    [Test]
    public async Task ExecuteSkill_InlineMode_RedirectsToReadSkill()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill(new SkillManifest { Name = "demo", Description = "示例" }, "指令正文BODY"));
        var executor = new SkillExecutor(registry, new ToolRegistry(), () => null, () => "m");

        var result = await executor.ExecuteAsync("demo", "处理这个", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Content, Does.Contain("read_skill"), "inline 技能应指回 read_skill，收敛为双通道");
            Assert.That(result.Content, Does.Not.Contain("指令正文BODY"), "inline 通道不得再回填技能正文");
        });
    }
}