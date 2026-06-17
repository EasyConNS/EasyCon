using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Skills;
using System.Text;

namespace EasyCon2.Avalonia.Core.AiAgent;

/// <summary>
/// 系统提示词组装器，替代 <see cref="AgentOrchestrator"/> 中硬编码的 BuildSystemPrompt。
/// 实现基于 Skill 的三级渐进式披露：
/// <list type="number">
///   <item>Level 1：角色 prompt + 技能索引（始终在场，紧凑）</item>
///   <item>Level 2：已激活技能的完整 body（按上下文评估后注入）</item>
///   <item>Level 3：references 等资源（模型通过 read_skill 主动拉取，不在此处理）</item>
/// </list>
/// 不含反思机制（由 <see cref="AgentOrchestrator"/> 在调用后追加）与滑动窗口截断
/// （同样由 Orchestrator 处理），保持单一职责。
/// </summary>
public sealed class PromptAssembler
{
    private readonly SkillRegistry _skills;

    public PromptAssembler(SkillRegistry skills) => _skills = skills;

    /// <summary>
    /// 组装系统提示词。
    /// </summary>
    /// <param name="history">完整对话历史（用于评估触发）。</param>
    /// <param name="round">当前 ReAct 轮次。</param>
    public string Build(IReadOnlyList<ChatMessage> history, int round = 0)
    {
        var sb = new StringBuilder();

        // ── 角色基线（替代 SystemPrompts.Default 的核心部分）──
        sb.Append(BaseRolePrompt);

        // ── Level 1：技能索引 ──
        var index = BuildSkillIndex();
        if (index.Length > 0) sb.Append(index);

        // ── Level 2：已激活技能 body ──
        var ctx = SkillTriggerContext.FromHistory(history, round);
        foreach (var skill in _skills.EvaluateActive(ctx))
        {
            var body = skill.Body;
            if (!string.IsNullOrWhiteSpace(body))
                sb.Append("\n\n").Append(body);
        }

        // ── 轮次预算提醒（保留原 Orchestrator 行为）──
        if (round > 0)
        {
            var remaining = AgentOrchestrator.MaxToolRounds - round;
            sb.Append("\n\n[系统] 当前已使用 ")
              .Append(round).Append(" 轮工具调用，剩余 ").Append(remaining).Append(" 轮。");
            if (remaining <= 10)
                sb.Append(" 轮次即将耗尽，请尽快完成目标并回复用户。");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 技能索引：紧凑列表，告知模型有哪些可激活能力。
    /// 仅当注册了技能时输出。
    /// </summary>
    private string BuildSkillIndex()
    {
        if (_skills.All.Count == 0) return "";

        var sb = new StringBuilder("\n\n## 可用技能\n");
        sb.Append("不确定如何处理任务时，先调用 list_skills 查看详情，");
        sb.Append("再按需调用 read_skill 获取完整指令或参考文档。\n\n");

        foreach (var skill in _skills.All)
        {
            var m = skill.Manifest;
            // 索引只展示一行摘要，避免 token 膨胀
            var summary = OneLineSummary(m.Description);
            sb.Append("- **").Append(m.Name).Append("**: ").Append(summary);
            if (m.AlwaysActive) sb.Append(" (常驻)");
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>取描述的首行或前 120 字符作为索引摘要。</summary>
    private static string OneLineSummary(string description)
    {
        var firstLine = description.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var s = firstLine.Length > 0 ? firstLine[0].Trim() : description.Trim();
        return s.Length > 120 ? s[..120] + "..." : s;
    }

    /// <summary>
    /// 角色 prompt 基线。迁移自 <see cref="SystemPrompts.Default"/>，
    /// 去掉与具体技能（脚本语法等）耦合的部分，仅保留身份与通用行为规范。
    /// </summary>
    private const string BaseRolePrompt = """
        你是 EasyCon（伊机控）的 AI 助手。
        EasyCon 是一个游戏手柄自动化脚本工具，支持脚本编写、图像识别、按键映射等功能。
        请用中文回答问题，回答简洁准确。

        ## 反思意识
        在执行多步骤任务时，养成反思的习惯：
        - 每次工具调用后，快速评估结果是否符合预期
        - 遇到失败时，先分析原因再尝试，避免盲目重试
        - 发现策略无效时，及时调整方向
        - 记录关键发现，为后续决策提供依据
        """;
}
