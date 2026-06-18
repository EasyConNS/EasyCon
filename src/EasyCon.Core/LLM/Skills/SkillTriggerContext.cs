namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// 技能激活评估的上下文。承载本轮用户消息、对话历史中已用工具、当前轮次等。
/// </summary>
public sealed class SkillTriggerContext
{
    /// <summary>本轮最新的用户消息文本（小写化由评估器处理）。</summary>
    public string UserMessage { get; init; } = "";

    /// <summary>对话历史中已调用过的所有工具名（去重）。</summary>
    public IReadOnlyCollection<string> RecentTools { get; init; } = Array.Empty<string>();

    /// <summary>当前 ReAct 轮次（0 表示首轮）。</summary>
    public int Round { get; init; }

    /// <summary>原始对话历史，供高级触发器使用（如扫描最近 N 条 user 消息）。</summary>
    public IReadOnlyList<string> RecentUserMessages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 从对话历史构造上下文。
    /// </summary>
    /// <param name="history">完整对话历史。</param>
    /// <param name="round">当前轮次。</param>
    /// <param name="recentUserCount">扫描最近多少条 user 消息用于关键词匹配，默认 3。</param>
    public static SkillTriggerContext FromHistory(
        IReadOnlyList<LLM.Messages.ChatMessage> history,
        int round = 0,
        int recentUserCount = 3)
    {
        var recentTools = history
            .Where(m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
            .SelectMany(m => m.ToolCalls!)
            .Select(tc => tc.Function.Name)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var recentUserMsgs = history
            .Where(m => m.Role == "user")
            .TakeLast(recentUserCount)
            .Select(m => m.Content?.ToString() ?? "")
            .ToList();

        return new SkillTriggerContext
        {
            UserMessage = recentUserMsgs.LastOrDefault() ?? "",
            RecentTools = recentTools,
            Round = round,
            RecentUserMessages = recentUserMsgs
        };
    }
}