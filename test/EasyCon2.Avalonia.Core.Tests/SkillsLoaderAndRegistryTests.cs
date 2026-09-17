using EasyCon.Core.LLM.Skills;
using EasyCon2.Avalonia.Core.AiAgent.Skills;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// SkillLoader / SkillRegistry 的核心不变量测试：
/// frontmatter 解析、搜索优先级（项目覆盖用户覆盖内置）、触发评估。
/// </summary>
[TestFixture]
public class SkillsLoaderAndRegistryTests
{
    private string _tempRoot = "";

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "easycon-skills-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── frontmatter 解析 ────────────────────────────────────

    [Test]
    public void ParseFrontmatter_ReadsScalarFields()
    {
        var skillDir = WriteSkill("demo", """
            ---
            name: demo
            description: 这是一个示例技能
            priority: 10
            always_active: true
            ---
            # 正文
            内容
            """);

        var skill = SkillLoader.TryParse(skillDir)!;

        Assert.Multiple(() =>
        {
            Assert.That(skill, Is.Not.Null);
            Assert.That(skill.Manifest.Name, Is.EqualTo("demo"));
            Assert.That(skill.Manifest.Description, Is.EqualTo("这是一个示例技能"));
            Assert.That(skill.Manifest.Priority, Is.EqualTo(10));
            Assert.That(skill.Manifest.AlwaysActive, Is.True);
        });
    }

    [Test]
    public void ParseFrontmatter_ReadsTriggerToolsList()
    {
        var skillDir = WriteSkill("t", """
            ---
            name: t
            description: test
            trigger_tools:
              - read_script
              - write_script
            ---
            body
            """);

        var skill = SkillLoader.TryParse(skillDir)!;

        Assert.Multiple(() =>
        {
            Assert.That(skill.Manifest.TriggerTools, Does.Contain("read_script"));
            Assert.That(skill.Manifest.TriggerTools, Does.Contain("write_script"));
            Assert.That(skill.Manifest.TriggerTools.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void ParseFrontmatter_ReadsBlockScalarDescription()
    {
        var skillDir = WriteSkill("blk", """
            ---
            name: blk
            description: |
              第一行描述
              第二行描述
            ---
            body
            """);

        var skill = SkillLoader.TryParse(skillDir)!;

        Assert.Multiple(() =>
        {
            Assert.That(skill.Manifest.Description, Does.Contain("第一行描述"));
            Assert.That(skill.Manifest.Description, Does.Contain("第二行描述"));
        });
    }

    [Test]
    public void ParseFrontmatter_ReadsInlineListTriggerTools()
    {
        var skillDir = WriteSkill("inl", """
            ---
            name: inl
            description: test
            trigger_tools: [a, b, c]
            ---
            body
            """);

        var skill = SkillLoader.TryParse(skillDir)!;

        Assert.That(skill.Manifest.TriggerTools, Is.EquivalentTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void TryParse_MissingNameReturnsNull()
    {
        var skillDir = WriteSkill("noname", """
            ---
            description: no name here
            ---
            body
            """);

        var skill = SkillLoader.TryParse(skillDir);

        Assert.That(skill, Is.Null, "缺 name 字段应拒绝加载");
    }

    [Test]
    public void ReadBody_StripsFrontmatter()
    {
        var skillDir = WriteSkill("body", """
            ---
            name: body
            description: x
            ---
            # 标题
            正文内容
            """);

        var body = SkillLoader.ReadBody(skillDir);

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("# 标题"));
            Assert.That(body, Does.Contain("正文内容"));
            Assert.That(body, Does.Not.Contain("description"));
        });
    }

    // ── 搜索优先级 ──────────────────────────────────────────

    [Test]
    public void LoadFromPaths_LaterOverridesEarlier_SameName()
    {
        // 内置（低优先级）与项目（高优先级）同名
        var builtin = Path.Combine(_tempRoot, "builtin");
        var project = Path.Combine(_tempRoot, "project");
        WriteSkill("dup", "name: dup\ndescription: 内置版\n---\nbody-builtin", dir: builtin);
        WriteSkill("dup", "name: dup\ndescription: 项目版\n---\nbody-project", dir: project);

        var skills = SkillLoader.LoadFromPaths(new[] { builtin, project });

        Assert.Multiple(() =>
        {
            Assert.That(skills.Count, Is.EqualTo(1), "同名只保留一个");
            Assert.That(skills[0].Manifest.Description, Is.EqualTo("项目版"), "后者覆盖前者");
        });
    }

    [Test]
    public void LoadFromPaths_SkipsMissingDirectories()
    {
        var skills = SkillLoader.LoadFromPaths(new[]
        {
            Path.Combine(_tempRoot, "does-not-exist"),
        });

        Assert.That(skills, Is.Empty, "不存在的目录应被静默跳过");
    }

    // ── 触发评估 ────────────────────────────────────────────

    [Test]
    public void Registry_AlwaysActiveSkillAlwaysEvaluates()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill
        {
            Manifest = new SkillManifest { Name = "core", AlwaysActive = true, Description = "core" }
        });

        var active = registry.EvaluateActive(new SkillTriggerContext()).ToList();

        Assert.That(active.Select(s => s.Manifest.Name), Does.Contain("core"));
    }

    [Test]
    public void Registry_TriggersOnToolUsage()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill
        {
            Manifest = new SkillManifest
            {
                Name = "authoring",
                Description = "脚本编写",
                TriggerTools = { "write_script" }
            }
        });

        // 未用任何工具 → 不激活
        var noTool = registry.EvaluateActive(new SkillTriggerContext { RecentTools = Array.Empty<string>() });
        Assert.That(noTool, Is.Empty);

        // 用了 write_script → 激活
        var withTool = registry.EvaluateActive(new SkillTriggerContext { RecentTools = new[] { "write_script" } });
        Assert.That(withTool.Select(s => s.Manifest.Name), Does.Contain("authoring"));
    }

    [Test]
    public void Registry_TriggersOnDescriptionKeyword()
    {
        var registry = new SkillRegistry();
        registry.Register(new Skill
        {
            Manifest = new SkillManifest { Name = "ecscript-authoring", Description = "脚本编写" }
        });

        var hit = registry.EvaluateActive(new SkillTriggerContext { UserMessage = "帮我写一个脚本" });
        var miss = registry.EvaluateActive(new SkillTriggerContext { UserMessage = "今天天气不错" });

        Assert.Multiple(() =>
        {
            Assert.That(hit.Select(s => s.Manifest.Name), Does.Contain("ecscript-authoring"));
            Assert.That(miss, Is.Empty);
        });
    }

    // ── 资源访问 ────────────────────────────────────────────

    [Test]
    public void ReadReference_LoadsReferencesSubdir()
    {
        var skillDir = WriteSkill("res", "name: res\ndescription: x\n---\nbody");
        var refDir = Path.Combine(skillDir, "references");
        Directory.CreateDirectory(refDir);
        File.WriteAllText(Path.Combine(refDir, "doc.md"), "参考内容");

        var skill = SkillLoader.TryParse(skillDir)!;

        Assert.Multiple(() =>
        {
            Assert.That(skill.ReadReference("doc.md"), Is.EqualTo("参考内容"));
            Assert.That(skill.ReadReference("missing.md"), Is.Null);
            Assert.That(skill.ListReferences(), Does.Contain("doc.md"));
        });
    }

    [Test]
    public void ReadReference_BlocksPathTraversal()
    {
        // 在 references 下放一个符号链接/直接尝试逃逸路径
        var skillDir = WriteSkill("trav", "name: trav\ndescription: x\n---\nbody");
        Directory.CreateDirectory(Path.Combine(skillDir, "references"));

        var skill = SkillLoader.TryParse(skillDir)!;

        // ../../etc/passwd 应被钳制到 references 根目录，文件不存在 → null
        var result = skill.ReadReference("../../../../etc/passwd");
        Assert.That(result, Is.Null, "路径逃逸应被拒绝");
    }

    // ── 内置技能冒烟测试（验证代码初始化的内置 Skill 能正常使用）──

    [Test]
    public void BundledSkills_LoadAndParseCorrectly()
    {
        var skills = BundledSkills.CreateAll();

        Assert.Multiple(() =>
        {
            Assert.That(skills.Count, Is.GreaterThanOrEqualTo(3), "至少 3 个内置技能");
            var names = skills.Select(s => s.Manifest.Name).ToList();
            Assert.That(names, Does.Contain("ecscript-authoring"));
            Assert.That(names, Does.Contain("device-control"));
            Assert.That(names, Does.Contain("vision-analysis"));
        });

        // ecscript-authoring 应带 references
        var authoring = skills.First(s => s.Manifest.Name == "ecscript-authoring");
        Assert.Multiple(() =>
        {
            Assert.That(authoring.Manifest.TriggerTools, Does.Contain("write_script"), "应关联 write_script");
            Assert.That(authoring.ListReferences(), Does.Contain("syntax-full.md"), "应有完整语法参考");
            Assert.That(authoring.Body.Length, Is.GreaterThan(100), "body 非空");
            // 完整语法参考应可读
            Assert.That(authoring.ReadReference("syntax-full.md"), Does.Contain("ECScript"), "语法参考可读");
        });
    }

    // ── 辅助 ────────────────────────────────────────────────

    private string WriteSkill(string folder, string content, string? dir = null)
    {
        var root = dir ?? _tempRoot;
        Directory.CreateDirectory(root);
        var skillDir = Path.Combine(root, folder);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), EnsureFrontmatterDelim(content));
        return skillDir;
    }

    /// <summary>确保 SKILL.md 内容以 --- 开头（标准 frontmatter 分隔符）。</summary>
    private static string EnsureFrontmatterDelim(string content)
        => content.StartsWith("---", StringComparison.Ordinal) ? content : "---\n" + content;
}
