using EasyCon.Core.LLM.Tools;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// execute_skill 工具：按名称执行已注册的 skill。
/// 支持两种模式：
/// <list type="bullet">
///   <item><b>inline</b>（默认）：返回 skill body + 消息，由主 LLM 在下一轮处理。</item>
///   <item><b>fork</b>（<c>context: fork</c>）：启动独立子 Agent 执行，返回子 Agent 回复。</item>
/// </list>
/// 可用 <c>list_skills</c> 查看所有已注册技能及其执行模式。
/// </summary>
internal class ExecuteSkillTool : IAiTool
{
    public const string ToolName = "execute_skill";

    private readonly SkillExecutor _executor;

    public ExecuteSkillTool(SkillExecutor executor) => _executor = executor;

    public string Name => ToolName;

    public string Description =>
        "执行一个 fork 型 skill（context: fork，独立子任务执行，只回传最终结果）。" +
        "inline 技能的指令会按需自动注入上下文，也可用 read_skill 获取，无需经此工具。" +
        "可用 list_skills 查看所有已注册 skill 的名称与执行模式。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["skill_name"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "技能名称（如 ecscript-authoring）。可用 list_skills 查看所有已注册技能。"
            },
            ["message"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "传递给技能的用户消息或输入内容。"
            }
        },
        Required = ["skill_name", "message"]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        // 解析 skill_name
        if (!args.TryGetValue("skill_name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return ToolResult.Error("[错误] 缺少必填参数 skill_name");

        var skillName = nameEl.GetString()!;

        // 解析 message
        var message = "";
        if (args.TryGetValue("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            message = msgEl.GetString() ?? "";

        return await _executor.ExecuteAsync(skillName, message, ct);
    }
}