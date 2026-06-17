using EasyCon.Core.LLM.Skills;
using EasyCon.Core.LLM.Tools;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// read_skill 元工具：读取技能的完整指令（Level 2）或参考文档（Level 3）。
/// 实现 Claude Agent Skills 的渐进式披露：模型按需主动拉取详细内容，
/// 而非编排器猜测后整段塞入系统提示词。
/// </summary>
public class ReadSkillTool : IAiTool
{
    private readonly SkillRegistry _skills;

    public ReadSkillTool(SkillRegistry skills) => _skills = skills;

    public string Name => "read_skill";

    public string Description =>
        "读取指定技能的完整指令或参考文档。技能名可用 list_skills 查询。" +
        "省略 reference 返回技能完整指令（SKILL.md 正文）；" +
        "指定 reference 返回 references/ 下的具体文档。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["skill_name"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "技能名称（如 ecscript-authoring）"
            },
            ["reference"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "可选：references/ 下的文档相对路径（如 syntax-full.md）。省略则返回技能完整指令。"
            }
        },
        Required = ["skill_name"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("skill_name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return Task.FromResult(ToolResult.Error("[错误] 缺少必填参数 skill_name"));

        var name = nameEl.GetString()!;
        var skill = _skills.Get(name);
        if (skill is null)
            return Task.FromResult(ToolResult.Error($"[错误] 未知技能: {name}。可用 list_skills 查看已注册技能。"));

        // Level 3：读取参考文档
        if (args.TryGetValue("reference", out var refEl) && refEl.ValueKind == JsonValueKind.String)
        {
            var relPath = refEl.GetString()!;
            var content = skill.ReadReference(relPath);
            return content is null
                ? Task.FromResult(ToolResult.Error($"[错误] 参考文档不存在: {relPath}。可用 list_skills 查看该技能的参考文档列表。"))
                : Task.FromResult(ToolResult.Ok(content));
        }

        // Level 2：返回技能完整指令
        var body = skill.Body;
        if (string.IsNullOrWhiteSpace(body))
            return Task.FromResult(ToolResult.Ok($"(技能 {name} 无正文指令)"));

        return Task.FromResult(ToolResult.Ok(body));
    }
}
