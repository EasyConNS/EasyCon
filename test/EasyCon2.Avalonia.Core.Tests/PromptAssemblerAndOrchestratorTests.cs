using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Skills;
using EasyCon2.Avalonia.Core.AiAgent;
using EasyCon2.Avalonia.Core.AiAgent.Tools;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// PromptAssembler 与 AgentOrchestrator 集成测试：
/// 验证技能注入路径下系统提示词的正确组装，以及与旧路径的向后兼容。
/// </summary>
[TestFixture]
public class PromptAssemblerAndOrchestratorTests
{
    // ── PromptAssembler ────────────────────────────────────

    [Test]
    public void Assembler_WithNoSkills_EmitsOnlyRolePrompt()
    {
        var assembler = new PromptAssembler(new SkillRegistry());
        var history = new List<ChatMessage> { ChatMessage.User("你好") };

        var baseRole = assembler.GetBaseRolePrompt();
        var skillContent = assembler.Build(history);

        Assert.Multiple(() =>
        {
            Assert.That(baseRole, Does.Contain("EasyCon"), "基础角色应包含 EasyCon 身份");
            Assert.That(skillContent, Is.Empty, "无技能时技能内容应为空");
        });
    }

    [Test]
    public void Assembler_EmitsSkillIndex_WhenSkillsRegistered()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill
        {
            Manifest = new SkillManifest { Name = "demo-skill", Description = "示例技能描述" }
        });
        var assembler = new PromptAssembler(registry);
        var history = new List<ChatMessage> { ChatMessage.User("你好") };

        var prompt = assembler.Build(history);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("可用技能"), "应输出技能索引");
            Assert.That(prompt, Does.Contain("demo-skill"), "索引应含技能名");
            Assert.That(prompt, Does.Contain("list_skills"), "应提示模型用元工具查询详情");
        });
    }

    [Test]
    public void Assembler_InjectsBody_WhenAlwaysActive()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill
        {
            Manifest = new SkillManifest { Name = "core", AlwaysActive = true, Description = "core" },
            DirectoryPath = "/nonexistent" // body 会读到空
        });
        // 用真实 body：重新构造一个能读到 body 的 Skill
        registry.Clear();
        var tempDir = CreateTempSkill("core", "name: core\ndescription: c\nalways_active: true\n---\n常驻指令内容");
        registry.Register(SkillLoader.TryParse(tempDir)!);

        var assembler = new PromptAssembler(registry);
        var history = new List<ChatMessage> { ChatMessage.User("任意") };

        var prompt = assembler.Build(history);

        Assert.That(prompt, Does.Contain("常驻指令内容"), "常驻技能 body 应被注入");
    }

    [Test]
    public void Assembler_AppendsRoundBudget()
    {
        var assembler = new PromptAssembler(new SkillRegistry());
        var history = new List<ChatMessage> { ChatMessage.User("继续") };

        var prompt = assembler.Build(history, round: 5);

        Assert.That(prompt, Does.Contain("当前已使用 5 轮"));
    }

    // ── Orchestrator 集成：技能路径 vs 旧路径 ─────────────

    [Test]
    public void Orchestrator_WithSkills_UsesAssemblerPath()
    {
        var registry = new SkillRegistry();
        var tempDir = CreateTempSkill("demo", "name: demo\ndescription: 示例\n---\n技能正文 ABC");
        registry.Register(SkillLoader.TryParse(tempDir)!);

        var orchestrator = new AgentOrchestrator(new ToolRegistry(), registry);
        var history = new List<ChatMessage> { ChatMessage.User("你好") };

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            // [0] system: 身份标识, [1] system: 基础角色
            Assert.That(messages[0].Role, Is.EqualTo("system"), "身份标识应为 system");
            Assert.That(messages[1].Role, Is.EqualTo("system"), "基础角色应为 system");
            Assert.That(messages.Count(m => m.Role == "system"), Is.EqualTo(2), "两条 system 消息");

            // 技能内容在后续 user 消息中
            var skillMsg = messages.FirstOrDefault(m =>
                m.Role == "user" && m.Content?.ToString()?.Contains("可用技能") == true);
            Assert.That(skillMsg, Is.Not.Null, "技能路径应输出索引");
            Assert.That(skillMsg!.Content?.ToString(), Does.Contain("<system-reminder>"), "技能内容应包裹 system-reminder");
        });
    }

    [Test]
    public void Orchestrator_WithoutSkills_FallsBackToLegacyPath()
    {
        // 不注入 SkillRegistry → 走旧 BuildSystemPrompt
        var orchestrator = new AgentOrchestrator(new ToolRegistry());
        var history = new List<ChatMessage> { ChatMessage.User("你好") };

        var messages = orchestrator.BuildMessages(history);

        Assert.Multiple(() =>
        {
            // [0] system: 身份标识
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[0].Content?.ToString(), Is.EqualTo("你是 EasyCon（伊机控）的 AI 助手。"));
            // [1] system: 基础角色，包含 EasyCon
            Assert.That(messages[1].Role, Is.EqualTo("system"));
            Assert.That(messages[1].Content?.ToString(), Does.Contain("EasyCon"), "旧路径仍输出角色 prompt");
            Assert.That(messages[1].Content?.ToString(), Does.Not.Contain("可用技能"), "旧路径基础角色不应输出技能索引");
            Assert.That(messages.Count(m => m.Role == "system"), Is.EqualTo(2));
        });
    }

    // ── 元工具 ──────────────────────────────────────────────

    [Test]
    public async Task ListSkillsTool_EmptyRegistry_ReportsNoSkills()
    {
        var tool = new ListSkillsTool(new SkillRegistry());
        var result = await tool.ExecuteAsync(new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.That(result.Content, Does.Contain("无已注册技能"));
    }

    [Test]
    public async Task ListSkillsTool_ListsRegisteredSkills()
    {
        var registry = new SkillRegistry();
        var tempDir = CreateTempSkill("alpha", "name: alpha\ndescription: 阿尔法技能\n---\nbody");
        registry.Register(SkillLoader.TryParse(tempDir)!);

        var tool = new ListSkillsTool(registry);
        var result = await tool.ExecuteAsync(new Dictionary<string, System.Text.Json.JsonElement>());

        Assert.Multiple(() =>
        {
            Assert.That(result.Content, Does.Contain("alpha"));
            Assert.That(result.Content, Does.Contain("阿尔法技能"));
        });
    }

    [Test]
    public async Task ReadSkillTool_ReturnsBodyByName()
    {
        var registry = new SkillRegistry();
        var tempDir = CreateTempSkill("beta", "name: beta\ndescription: x\n---\n完整指令内容");
        registry.Register(SkillLoader.TryParse(tempDir)!);

        var tool = new ReadSkillTool(registry);
        var args = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["skill_name"] = System.Text.Json.JsonSerializer.SerializeToElement("beta")
        };

        var result = await tool.ExecuteAsync(args);
        Assert.That(result.Content, Does.Contain("完整指令内容"));
    }

    [Test]
    public async Task ReadSkillTool_UnknownSkill_ReturnsError()
    {
        var tool = new ReadSkillTool(new SkillRegistry());
        var args = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["skill_name"] = System.Text.Json.JsonSerializer.SerializeToElement("ghost")
        };

        var result = await tool.ExecuteAsync(args);
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
    }

    [Test]
    public async Task ReadSkillTool_MissingNameParam_ReturnsError()
    {
        var tool = new ReadSkillTool(new SkillRegistry());
        var result = await tool.ExecuteAsync(new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
    }

    // ── 辅助 ────────────────────────────────────────────────

    private static string CreateTempSkill(string folder, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), "easycon-assembler-test-" + Guid.NewGuid().ToString("N")[..8]);
        var skillDir = Path.Combine(root, folder);
        Directory.CreateDirectory(skillDir);
        var normalized = content.StartsWith("---", StringComparison.Ordinal) ? content : "---\n" + content;
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), normalized);
        return skillDir;
    }
}
