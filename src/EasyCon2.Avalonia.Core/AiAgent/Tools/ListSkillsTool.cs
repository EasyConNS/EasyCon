using EasyCon.Core.LLM.Skills;
using EasyCon.Core.LLM.Tools;
using System.Text;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// list_skills 元工具：列出所有已注册技能的名称、描述与可用参考文档。
/// 属于 Skill 体系的 Level 1 入口，供模型感知有哪些可激活能力。
/// </summary>
public class ListSkillsTool : IAiTool
{
    private readonly SkillRegistry _skills;

    public ListSkillsTool(SkillRegistry skills) => _skills = skills;

    public string Name => "list_skills";

    public string Description =>
        "列出当前可用的所有技能（name + 描述 + 关联工具 + 参考文档列表）。" +
        "不确定某任务需要哪个技能时先调用此工具查看索引。";

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (_skills.All.Count == 0)
            return Task.FromResult(ToolResult.Ok("(无已注册技能)"));

        var sb = new StringBuilder();
        sb.Append("共 ").Append(_skills.All.Count).Append(" 个技能：\n\n");

        foreach (var skill in _skills.All)
        {
            var m = skill.Manifest;
            sb.Append("## ").Append(m.Name).Append('\n');
            // 描述可能较长，截断到 300 字符避免 token 膨胀
            var desc = m.Description.Trim();
            sb.Append(desc.Length > 300 ? desc[..300] + "..." : desc).Append('\n');

            if (m.TriggerTools.Count > 0)
                sb.Append("关联工具: ").Append(string.Join(", ", m.TriggerTools)).Append('\n');

            if (m.AlwaysActive)
                sb.Append("(常驻激活)\n");

            var refs = skill.ListReferences();
            if (refs.Count > 0)
            {
                sb.Append("参考文档（用 read_skill 拉取）:\n");
                foreach (var r in refs)
                    sb.Append("  - ").Append(r).Append('\n');
            }
            sb.Append('\n');
        }

        sb.Append("如需某技能的完整指令，调用 read_skill(skill_name=...)；");
        sb.Append("如需特定参考文档，调用 read_skill(skill_name=..., reference=...)。");

        return Task.FromResult(ToolResult.Ok(sb.ToString()));
    }
}
