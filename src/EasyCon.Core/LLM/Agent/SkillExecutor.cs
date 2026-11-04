using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Skills;
using EasyCon.Core.LLM.Tools;

namespace EasyCon.Core.LLM.Agent;

/// <summary>
/// Skill 执行器。支持两种执行模式：
/// <list type="bullet">
///   <item><b>inline</b>（默认）：返回 skill body + 消息，由主 LLM 在下一轮处理。</item>
///   <item><b>fork</b>（<c>context: fork</c>）：启动独立子 Agent ReAct 循环，
///       以 skill body 为 system prompt，按 <c>allowed_tools</c> 过滤可用工具，
///       执行完毕后返回子 Agent 的最终回复。</item>
/// </list>
/// </summary>
public class SkillExecutor
{
    private readonly SkillRegistry _skills;
    private readonly ToolRegistry _tools;
    private readonly Func<ProviderConfig?> _getProvider;
    private readonly Func<string> _getModelId;

    private const int MaxSubAgentRounds = 10;
    private const int SubAgentTimeoutSeconds = 30;

    /// <summary>
    /// </summary>
    /// <param name="skills">技能注册中心。</param>
    /// <param name="tools">工具注册中心。</param>
    /// <param name="getProvider">延迟获取当前 ProviderConfig（模型可能切换）；未选择有效供应商时为 null。</param>
    /// <param name="getModelId">延迟获取当前 modelId（模型可能切换）。</param>
    public SkillExecutor(SkillRegistry skills, ToolRegistry tools, Func<ProviderConfig?> getProvider, Func<string> getModelId)
    {
        _skills = skills;
        _tools = tools;
        _getProvider = getProvider;
        _getModelId = getModelId;
    }

    /// <summary>
    /// 执行指定 skill。
    /// </summary>
    /// <param name="skillName">技能名称。</param>
    /// <param name="message">传递给技能的用户消息。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>执行结果；fork 模式可携带子 Agent 产出的最新图片。</returns>
    public async Task<ToolResult> ExecuteAsync(string skillName, string message, CancellationToken ct)
    {
        var skill = _skills.Get(skillName);
        if (skill is null)
            return ToolResult.Error($"[错误] 未知技能: {skillName}。可用 list_skills 查看已注册技能。");

        var isFork = string.Equals(skill.Manifest.Context, "fork", StringComparison.OrdinalIgnoreCase);

        if (!isFork)
        {
            // inline 技能的指令已随上下文按需注入，也可 read_skill 显式获取；
            // execute_skill 收敛为 fork 专用通道，避免同一份正文出现第三个来源
            return ToolResult.Error(
                $"[提示] 技能 {skillName} 为 inline 模式：完整指令可用 read_skill 获取，无需 execute_skill 执行。");
        }

        // Fork 模式：启动子 Agent ReAct 循环
        return await RunSubAgentAsync(skill, message, ct);
    }

    /// <summary>
    /// 子 Agent ReAct 循环。独立 LLM 调用，不共享主 Agent 的对话历史。
    /// </summary>
    private async Task<ToolResult> RunSubAgentAsync(Skill skill, string message, CancellationToken ct)
    {
        var provider = _getProvider();
        if (provider is null || string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(provider.ApiKey))
            return ToolResult.Error("[错误] 未选择有效的模型供应商，无法执行 fork 技能。请先在模型列表中选择可用模型。");

        var body = skill.Body;
        if (string.IsNullOrWhiteSpace(body))
            return ToolResult.Error("(技能无正文指令，无法执行 fork 模式)");

        var history = new List<ChatMessage>
        {
            ChatMessage.System(body),
            ChatMessage.User(message)
        };

        var client = ChatClientFactory.Create(provider);
        var toolDefs = BuildSubAgentToolDefs(skill);
        var hasTools = toolDefs.Count > 0;

        for (var round = 0; round < MaxSubAgentRounds; round++)
        {
            var request = new ChatRequest
            {
                Model = _getModelId(),
                Messages = history
            };
            if (hasTools)
            {
                request.Tools = toolDefs;
                request.ToolChoice = ToolChoice.Auto;
            }

            ChatResponse response;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(SubAgentTimeoutSeconds));
                response = await client.SendAsync(request, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 区分用户主动停止与超时：一律报「超时」会带偏模型的自我修正方向
                return ToolResult.Error(ct.IsCancellationRequested ? "[子Agent] 执行已停止" : "[子Agent] 执行超时");
            }

            if (!response.Success)
                return ToolResult.Error($"[子Agent错误] {response.ErrorMessage ?? "未知错误"}");

            // 无工具调用 → 最终回复
            if (!response.HasToolCalls)
            {
                var content = response.Content;
                return ToolResult.Ok(
                    string.IsNullOrWhiteSpace(content) ? "(子Agent 未返回内容)" : content,
                    FindLatestImage(history));
            }

            // 有工具调用 → 执行并追加结果
            history.Add(ChatMessage.Assistant(response.ToolCalls!));
            foreach (var tc in response.ToolCalls!)
            {
                var result = await ExecuteSubAgentToolAsync(tc, ct);
                history.Add(ChatMessage.Tool(tc.Id, result, tc.Function.Name));
            }
        }

        return ToolResult.Error("[子Agent] 已达到最大工具调用轮次，请简化任务后重试。");
    }

    /// <summary>
    /// 从子 Agent 历史中取最近的图片消息。子 Agent 的中间图片不回填主对话，
    /// 但最终画面是主模型决策需要的观察结果，随 AttachedMessage 一并上交。
    /// </summary>
    private static ChatMessage? FindLatestImage(List<ChatMessage> history)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Content is List<ContentPart> parts && parts.Any(p => p.Type == "image_url"))
                return history[i];
        }
        return null;
    }

    /// <summary>
    /// 按 skill 的 <c>allowed_tools</c> 白名单过滤工具定义。
    /// 白名单为空时允许全部工具，但 <c>execute_skill</c> 一律排除：
    /// 否则子 Agent 可再 fork 子 Agent，LLM 循环嵌套与开销无上界。
    /// </summary>
    private List<ToolDefinition> BuildSubAgentToolDefs(Skill skill)
    {
        var allTools = _tools.ToToolDefinitions()
            .Where(t => t.Function.Name != Tools.ExecuteSkillTool.ToolName)
            .ToList();
        var allowed = skill.Manifest.AllowedTools;
        if (allowed is null || allowed.Count == 0)
            return allTools;

        return allTools
            .Where(t => allowed.Contains(t.Function.Name))
            .ToList();
    }

    /// <summary>
    /// 执行子 Agent 的单个工具调用，返回工具输出文本。
    /// </summary>
    private async Task<string> ExecuteSubAgentToolAsync(ToolCall tc, CancellationToken ct)
    {
        var tool = _tools.Get(tc.Function.Name);
        if (tool is null)
            return $"[错误] 未知工具: {tc.Function.Name}";

        try
        {
            var args = tc.Function.ParseArguments();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(SubAgentTimeoutSeconds));
            var result = await tool.ExecuteAsync(args, timeoutCts.Token);
            return result.Content;
        }
        catch (OperationCanceledException)
        {
            return $"[错误] 工具 {tc.Function.Name} 执行超时";
        }
        catch (Exception ex)
        {
            return $"[工具执行异常] {tc.Function.Name}: {ex.Message}";
        }
    }
}