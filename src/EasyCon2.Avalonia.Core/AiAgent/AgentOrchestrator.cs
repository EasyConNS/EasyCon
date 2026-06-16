using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.AiAgent.Tools;
using System.Text;

namespace EasyCon2.Avalonia.Core.AiAgent;

/// <summary>
/// Agent 编排事件，ViewModel 据此更新 UI。
/// </summary>
public abstract record AgentEvent
{
    /// <summary>新一轮开始，携带当前对话前缀（用于 UI 占位）。</summary>
    public record RoundStart(string Prefix) : AgentEvent;

    /// <summary>正文内容增量。</summary>
    public record ContentDelta(string Text) : AgentEvent;

    /// <summary>思考/推理内容增量。</summary>
    public record ThinkingDelta(string Text) : AgentEvent;

    /// <summary>工具调用开始执行。</summary>
    public record ToolExecuting(string Name) : AgentEvent;

    /// <summary>工具调用完成，携带结果摘要。</summary>
    public record ToolCompleted(string Name, string Summary) : AgentEvent;

    /// <summary>Token 用量更新。</summary>
    public record UsageUpdated(int Prompt, int Completion, int Total) : AgentEvent;

    /// <summary>错误信息。</summary>
    public record Error(string Message) : AgentEvent;

    /// <summary>对话循环结束。</summary>
    public record Completed(string FinalContent) : AgentEvent;
}

/// <summary>
/// AI Agent 核心编排器，管理 ReAct 循环、工具调度、prompt 组装。
/// 与 UI 完全解耦，可独立测试。
/// </summary>
public class AgentOrchestrator
{
    public const int MaxToolRounds = 50;
    public const int MaxHistoryMessages = 40;
    private const int ToolTimeoutSeconds = 30;

    private readonly ToolRegistry _tools;
    private int _frameImageIndex = -1;
    private readonly ReflectionState _reflectionState = new();

    /// <summary>
    /// 反思状态管理器，追踪执行失败和重复结果，控制反思注入。
    /// </summary>
    private class ReflectionState
    {
        private const int MaxReflections = 3;
        private const int FailureThreshold = 2;
        private const int SameResultThreshold = 2;

        private int _reflectionCount;
        private int _consecutiveFailures;
        private int _consecutiveSameResults;
        private string _lastToolResultHash = string.Empty;
        private bool _shouldTriggerReflection;

        /// <summary>用户发送新消息时重置反思状态。</summary>
        public void Reset()
        {
            _reflectionCount = 0;
            _consecutiveFailures = 0;
            _consecutiveSameResults = 0;
            _lastToolResultHash = string.Empty;
            _shouldTriggerReflection = false;
        }

        /// <summary>追踪工具执行结果。</summary>
        public void TrackResult(string toolResult)
        {
            if (string.IsNullOrEmpty(toolResult))
                return;

            if (toolResult.Contains("[错误]") || toolResult.Contains("[工具执行异常]") || toolResult.Contains("[错误] 工具"))
            {
                _consecutiveFailures++;
                _consecutiveSameResults = 0; // 失败不算相同结果
                if (_consecutiveFailures >= FailureThreshold)
                    _shouldTriggerReflection = true;
            }
            else
            {
                _consecutiveFailures = 0; // 成功重置失败计数
                var hash = toolResult.GetHashCode().ToString();
                if (hash == _lastToolResultHash)
                {
                    _consecutiveSameResults++;
                    if (_consecutiveSameResults >= SameResultThreshold)
                        _shouldTriggerReflection = true;
                }
                else
                {
                    _consecutiveSameResults = 0;
                }
                _lastToolResultHash = hash;
            }
        }

        /// <summary>追踪助手回复中的反思标记。</summary>
        public void TrackReflectionMarker(string assistantContent)
        {
            if (!string.IsNullOrEmpty(assistantContent) && assistantContent.Contains("反思："))
                _reflectionCount++;
        }

        /// <summary>判断是否需要注入反思提示。</summary>
        public bool ShouldInjectReflection()
        {
            if (_reflectionCount >= MaxReflections)
                return false;

            if (!_shouldTriggerReflection)
                return false;

            _shouldTriggerReflection = false;
            return true;
        }

        /// <summary>获取反思深度。</summary>
        public string GetReflectionDepth()
        {
            // 已反思过但仍在循环，或连续失败超过阈值 → 深度反思
            if (_reflectionCount > 0 || _consecutiveFailures > FailureThreshold)
                return "deep";
            return "shallow";
        }

        /// <summary>获取反思提示文本。</summary>
        public string GetReflectionPrompt()
        {
            return GetReflectionDepth() == "deep"
                ? SystemPrompts.ReflectionDeep
                : SystemPrompts.Reflection;
        }
    }

    public AgentOrchestrator(ToolRegistry tools)
    {
        _tools = tools;
    }

    /// <summary>
    /// 重置帧图片索引和反思状态（新对话时调用）。
    /// </summary>
    public void ResetState()
    {
        _frameImageIndex = -1;
        _reflectionState.Reset();
    }

    /// <summary>
    /// 执行完整的 Agent 对话循环。
    /// </summary>
    /// <param name="history">对话历史（会被修改，追加新消息）。</param>
    /// <param name="modelId">模型 ID。</param>
    /// <param name="provider">API 供应商配置。</param>
    /// <param name="onEvent">事件回调，ViewModel 据此更新 UI。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task RunAsync(
        List<ChatMessage> history,
        string modelId,
        ProviderConfig provider,
        Action<AgentEvent> onEvent,
        CancellationToken ct)
    {
        var toolDefs = _tools.ToToolDefinitions();
        var hasTools = toolDefs.Count > 0;
        var pendingReply = new StringBuilder();
        var pendingThinking = new StringBuilder();

        // 重置反思状态（用户新消息）
        _reflectionState.Reset();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            onEvent(new AgentEvent.RoundStart(""));
            pendingReply.Clear();
            pendingThinking.Clear();
            var accumulator = new ToolCallAccumulator();

            var client = ChatClientFactory.Create(provider);
            var request = new ChatRequest
            {
                Model = modelId,
                Messages = BuildMessages(history, round)
            };
            if (hasTools)
            {
                request.Tools = toolDefs;
                request.ToolChoice = ToolChoice.Auto;
            }

            var hasContent = false;
            await foreach (var delta in client.SendStreamAsync(request, ct))
            {
                switch (delta.Type)
                {
                    case DeltaType.Thinking:
                        pendingThinking.Append(delta.Text);
                        onEvent(new AgentEvent.ThinkingDelta(delta.Text));
                        break;
                    case DeltaType.Content:
                        pendingReply.Append(delta.Text);
                        hasContent = true;
                        onEvent(new AgentEvent.ContentDelta(delta.Text));
                        break;
                    case DeltaType.ToolCall:
                        if (delta.ToolCallDelta is not null)
                            accumulator.Append(delta.ToolCallDelta);
                        break;
                    case DeltaType.Error:
                        pendingReply.Append($"[错误] {delta.Text}");
                        onEvent(new AgentEvent.Error(delta.Text));
                        break;
                    case DeltaType.Usage:
                        onEvent(new AgentEvent.UsageUpdated(delta.PromptTokens, delta.CompletionTokens, delta.TotalTokens));
                        break;
                }
            }

            // 没有工具调用或没有注册工具 → 本轮即最终回复
            var toolCalls = accumulator.Build();
            if (!hasTools || toolCalls.Count == 0)
            {
                if (hasContent)
                {
                    history.Add(ChatMessage.Assistant(pendingReply.ToString()));
                    _reflectionState.TrackReflectionMarker(pendingReply.ToString());
                }
                onEvent(new AgentEvent.Completed(pendingReply.ToString()));
                return;
            }

            // 将 assistant 的工具调用加入历史
            history.Add(ChatMessage.Assistant(toolCalls));

            // 执行工具调用
            var results = await ExecuteToolCallsAsync(toolCalls, history, onEvent, ct);

            // 按顺序将工具结果加入历史，并处理多模态附加消息
            for (var i = 0; i < toolCalls.Count; i++)
            {
                history.Add(ChatMessage.Tool(toolCalls[i].Id, results[i].Content, toolCalls[i].Function.Name));
                _reflectionState.TrackResult(results[i].Content);

                // 处理附加的多模态消息（如 get_frame 的图片）
                if (results[i].AttachedMessage is { } img)
                {
                    if (_frameImageIndex >= 0 && _frameImageIndex < history.Count)
                        history[_frameImageIndex] = img;
                    else
                    {
                        _frameImageIndex = history.Count;
                        history.Add(img);
                    }
                }
            }
        }

        // 达到最大轮次
        history.Add(ChatMessage.Assistant(pendingReply.ToString()));
        onEvent(new AgentEvent.Completed(pendingReply.ToString() + "\n[已达到工具调用最大轮次]"));
    }

    /// <summary>
    /// 批量执行工具调用（并行执行，保持结果顺序）。
    /// </summary>
    private async Task<List<ToolResult>> ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        List<ChatMessage> history,
        Action<AgentEvent> onEvent,
        CancellationToken ct)
    {
        if (toolCalls.Count == 1)
        {
            var result = await ExecuteToolCallAsync(toolCalls[0], history, onEvent, ct);
            return [result];
        }

        var tasks = toolCalls.Select(tc => ExecuteToolCallAsync(tc, history, onEvent, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// 执行单个工具调用，返回完整 ToolResult（含可能的多模态附加消息）。
    /// </summary>
    private async Task<ToolResult> ExecuteToolCallAsync(
        ToolCall toolCall,
        List<ChatMessage> history,
        Action<AgentEvent> onEvent,
        CancellationToken ct)
    {
        var fn = toolCall.Function;
        var tool = _tools.Get(fn.Name);
        if (tool is null)
            return ToolResult.Error($"[错误] 未知工具: {fn.Name}");

        try
        {
            onEvent(new AgentEvent.ToolExecuting(fn.Name));

            var args = fn.ParseArguments();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(ToolTimeoutSeconds));

            var result = await tool.ExecuteAsync(args, timeoutCts.Token);
            var summary = Truncate(result.Content, 200);
            onEvent(new AgentEvent.ToolCompleted(fn.Name, summary));
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResult.Error($"[错误] 工具 {fn.Name} 执行超时（{ToolTimeoutSeconds}秒），请调整参数后重试。");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[工具执行异常] {ex.Message}");
        }
    }

    /// <summary>
    /// 组装发送给模型的消息列表（含系统提示词 + 截断后的历史）。
    /// </summary>
    internal List<ChatMessage> BuildMessages(List<ChatMessage> history, int currentRound = 0)
    {
        var systemPrompt = BuildSystemPrompt(history, currentRound);
        var messages = new List<ChatMessage>(history.Count + 2) { ChatMessage.System(systemPrompt) };

        // 反思提示注入（在系统提示后、历史消息前）
        if (_reflectionState.ShouldInjectReflection())
        {
            var reflectionPrompt = _reflectionState.GetReflectionPrompt();
            messages.Add(ChatMessage.System(reflectionPrompt));
        }

        // 滑动窗口截断
        if (history.Count <= MaxHistoryMessages)
        {
            messages.AddRange(history);
        }
        else
        {
            var skip = history.Count - MaxHistoryMessages;
            messages.Add(ChatMessage.System($"[系统提示] 为控制上下文长度，前面的 {skip} 条对话已被省略。"));
            messages.AddRange(history.Skip(skip));
        }

        return messages;
    }

    /// <summary>
    /// 组装系统提示词：基于工具调用历史决定注入哪些 prompt 段落，
    /// 关键词检测作为 fallback。
    /// </summary>
    internal string BuildSystemPrompt(List<ChatMessage> history, int currentRound)
    {
        var systemPrompt = SystemPrompts.Default;

        // 扫描历史中实际的工具调用
        var usedTools = history
            .Where(m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
            .SelectMany(m => m.ToolCalls!)
            .Select(tc => tc.Function.Name)
            .ToHashSet(StringComparer.Ordinal);

        var needsScriptSyntax = usedTools.Any(t =>
            t is "read_script" or "write_script" or "edit_script"
              or "compile_script" or "format_script" or "grep_script");
        if (!needsScriptSyntax)
            needsScriptSyntax = HasRecentUserKeyword(history, ["脚本", "script", "ecs", "语法", "代码", "编写"]);

        var needsVision = usedTools.Contains("get_frame");
        if (!needsVision)
            needsVision = HasRecentUserKeyword(history, ["画面", "屏幕", "截图", "看看", "识别", "看到", "图像"]);

        var needsExecution = usedTools.Any(t => t is "run_script" or "stop_script");
        if (!needsExecution)
            needsExecution = HasRecentUserKeyword(history, ["运行", "执行", "自动", "跑脚本"]);

        if (needsScriptSyntax)
            systemPrompt += SystemPrompts.ScriptSyntax;

        if (needsVision && needsExecution)
        {
            systemPrompt += SystemPrompts.ReActLoop;
        }
        else
        {
            if (needsVision)
                systemPrompt += SystemPrompts.Vision;
            if (needsExecution)
                systemPrompt += SystemPrompts.ScriptExecution;
        }

        if (currentRound > 0)
        {
            var remaining = MaxToolRounds - currentRound;
            systemPrompt += $"\n\n[系统] 当前已使用 {currentRound} 轮工具调用，剩余 {remaining} 轮。" +
                            (remaining <= 10 ? " 轮次即将耗尽，请尽快完成目标并回复用户。" : "");
        }

        return systemPrompt;
    }

    private static bool HasRecentUserKeyword(List<ChatMessage> history, string[] keywords)
    {
        return history
            .Where(m => m.Role == "user")
            .TakeLast(3)
            .Select(m => m.Content?.ToString()?.ToLowerInvariant() ?? "")
            .Any(msg => keywords.Any(k => msg.Contains(k)));
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max] + "...";
    }
}