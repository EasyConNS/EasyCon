using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Skills;
using EasyCon.Core.LLM.Tools;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent;

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

    /// <summary>工具调用开始执行（Id 关联同轮并行同名调用）。</summary>
    public record ToolExecuting(string Name, string Id) : AgentEvent;

    /// <summary>工具调用完成，携带结果摘要（Id 与 <see cref="ToolExecuting"/> 对应）。</summary>
    public record ToolCompleted(string Name, string Id, string Summary, string FullResult) : AgentEvent;

    /// <summary>工具等待人工确认（UI 据此弹确认框；拒绝后该工具以错误结果回传模型）。</summary>
    public record ToolAwaitingConfirmation(string Name, string Id, string Reason) : AgentEvent;

    /// <summary>Token 用量更新。</summary>
    public record UsageUpdated(int Prompt, int Completion, int Total) : AgentEvent;

    /// <summary>错误信息。</summary>
    public record Error(string Message) : AgentEvent;

    /// <summary>流式传输中断，正在重试。</summary>
    public record Retrying(int Attempt, int MaxAttempts, string Reason) : AgentEvent;

    /// <summary>对话循环结束。</summary>
    public record Completed(string FinalContent) : AgentEvent;

    /// <summary>调试信息（原始 SSE 数据、解析异常等）。</summary>
    public record DebugInfo(string Message) : AgentEvent;
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

    /// <summary>取帧工具名。get_frame 的图片消息编排有专属语义，见 AttachFrameImage。</summary>
    public const string FrameToolName = "get_frame";

    /// <summary>旧帧图降级后的占位文本（保留对话时间轴位置，不再携带像素）。</summary>
    public const string FrameOmittedPlaceholder = "[旧画面已省略：最新画面见后文]";

    /// <summary>get_frame 与其它工具同轮混调时的拒绝文案（fail-closed，指导模型下轮单独调用）。</summary>
    public const string FrameMixedRoundError =
        "[错误] get_frame 必须单独调用：与其它工具同轮执行会让画面早于本轮动作，请本轮只调用 get_frame，其余动作下一轮再调。";

    /// <summary>当前模型不支持视觉输入时 get_frame 的拒绝文案。</summary>
    public const string FrameVisionUnsupportedError =
        "[错误] 当前模型不支持视觉输入，无法使用 get_frame。请在模型列表选择具备视觉能力的模型后再试。";

    /// <summary>单个工具结果写入历史的字符预算，超出即截断并附显式尾注。</summary>
    public const int MaxToolResultChars = 20_000;

    private readonly ToolRegistry _tools;
    private readonly PromptAssembler? _promptAssembler;
    private readonly Func<ProviderConfig, IChatClient>? _clientFactory;
    private readonly Func<string?>? _projectDirectoryProvider;

    /// <summary>
    /// 危险工具（<see cref="IAiTool.RequiresConfirmation"/>）的人工确认回调。
    /// 返回 true 才执行；为 null 时 fail-closed 直接拒绝该工具调用。
    /// </summary>
    private readonly Func<ToolCall, Task<bool>>? _confirmationHandler;
    private int _latestFrameImageIndex = -1;
    private readonly ReflectionState _reflectionState = new();

    /// <summary>
    /// 反思状态管理器：失败按结构化状态计数，重复调用按「工具名+参数」签名检测。
    /// 两者都与结果文本解耦——结果可携带时间戳等每次变化的凭据，不得参与检测。
    /// </summary>
    private class ReflectionState
    {
        private const int MaxReflections = 3;
        private const int FailureThreshold = 2;
        private const int RepeatedCallThreshold = 3;

        private int _reflectionCount;
        private int _consecutiveFailures;
        private int _repeatedCallStreak;
        private string _lastCallSignature = "";
        private string _lastCallTool = "";
        private string _triggerReason = "";
        private bool _shouldTriggerReflection;

        /// <summary>用户发送新消息时重置反思状态。</summary>
        public void Reset()
        {
            _reflectionCount = 0;
            _consecutiveFailures = 0;
            _repeatedCallStreak = 0;
            _lastCallSignature = "";
            _lastCallTool = "";
            _triggerReason = "";
            _shouldTriggerReflection = false;
        }

        /// <summary>按工具结果的结构化状态计数失败（Error/Retryable 均计入，Success 重置）。</summary>
        public void TrackResult(ToolResultStatus status)
        {
            if (status == ToolResultStatus.Success)
            {
                _consecutiveFailures = 0;
                return;
            }

            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureThreshold)
                SetTrigger($"已连续 {_consecutiveFailures} 次工具执行失败");
        }

        /// <summary>按「工具名+参数签名」检测重复调用，参数变化即重置连击。</summary>
        public void TrackCall(string toolName, string argumentsJson)
        {
            var signature = toolName + "|" + StableArgumentsKey(argumentsJson);
            if (signature != _lastCallSignature)
            {
                _lastCallSignature = signature;
                _lastCallTool = toolName;
                _repeatedCallStreak = 1;
                return;
            }

            _repeatedCallStreak++;
            if (_repeatedCallStreak < RepeatedCallThreshold)
                return;

            SetTrigger($"已连续 {_repeatedCallStreak} 次以相同参数调用 {_lastCallTool}");
            _repeatedCallStreak = 0;
        }

        /// <summary>追踪助手回复中的反思标记。</summary>
        public void TrackReflectionMarker(string assistantContent)
        {
            if (!string.IsNullOrEmpty(assistantContent) && assistantContent.Contains("反思："))
                _reflectionCount++;
        }

        /// <summary>判断是否需要注入反思提示（触发即消费，一轮最多一条）。</summary>
        public bool ShouldInjectReflection()
        {
            if (_reflectionCount >= MaxReflections)
                return false;

            if (!_shouldTriggerReflection)
                return false;

            _shouldTriggerReflection = false;
            return true;
        }

        /// <summary>反思深度：已反思过仍循环，或失败远超阈值，升级为深度反思。</summary>
        public string GetReflectionDepth() =>
            _reflectionCount > 0 || _consecutiveFailures > FailureThreshold ? "deep" : "shallow";

        /// <summary>触发原因（现场数据），随反思提示一同注入。</summary>
        public string DescribeTrigger() => _triggerReason;

        public string GetReflectionPrompt()
        {
            var body = GetReflectionDepth() == "deep"
                ? SystemPrompts.ReflectionDeep
                : SystemPrompts.Reflection;
            return $"{DescribeTrigger()}。{body}";
        }

        private void SetTrigger(string reason)
        {
            _triggerReason = reason;
            _shouldTriggerReflection = true;
        }

        /// <summary>参数的稳定签名：顶层键排序后拼接，参数微调即产生不同签名。</summary>
        private static string StableArgumentsKey(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)) return "";
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return argumentsJson;
                return string.Join(";", doc.RootElement.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => p.Name + "=" + p.Value.GetRawText()));
            }
            catch
            {
                return argumentsJson;
            }
        }
    }

    public AgentOrchestrator(ToolRegistry tools) : this(tools, skillRegistry: null) { }

    /// <summary>
    /// 创建带技能体系的编排器。传入的 <paramref name="skillRegistry"/> 为空时，
    /// 回退到旧的硬编码 SystemPrompts 路径（向后兼容）。
    /// </summary>
    public AgentOrchestrator(ToolRegistry tools, SkillRegistry? skillRegistry)
        : this(tools, skillRegistry, clientFactory: null)
    {
    }

    /// <summary>
    /// 创建编排器并可注入客户端工厂。<paramref name="clientFactory"/> 为空时走
    /// ChatClientFactory（生产路径）；测试注入 fake client 以覆盖错误链路。
    /// <paramref name="projectDirectoryProvider"/> 返回当前脚本项目目录（未打开脚本为 null），
    /// 用于装配 AGENTS.md 项目指令与工作区文件工具的根目录。
    /// </summary>
    public AgentOrchestrator(
        ToolRegistry tools,
        SkillRegistry? skillRegistry,
        Func<ProviderConfig, IChatClient>? clientFactory,
        Func<string?>? projectDirectoryProvider = null)
        : this(tools, skillRegistry, clientFactory, projectDirectoryProvider, confirmationHandler: null)
    {
    }

    /// <summary>
    /// 完整构造：<paramref name="confirmationHandler"/> 为危险工具的人工确认回调
    /// （返回 true 才执行；null 时危险工具被 fail-closed 拒绝）。
    /// </summary>
    public AgentOrchestrator(
        ToolRegistry tools,
        SkillRegistry? skillRegistry,
        Func<ProviderConfig, IChatClient>? clientFactory,
        Func<string?>? projectDirectoryProvider,
        Func<ToolCall, Task<bool>>? confirmationHandler)
    {
        _tools = tools;
        _clientFactory = clientFactory;
        _projectDirectoryProvider = projectDirectoryProvider;
        _confirmationHandler = confirmationHandler;
        if (skillRegistry is { All.Count: > 0 })
            _promptAssembler = new PromptAssembler(skillRegistry);
    }

    /// <summary>
    /// 重置帧图片索引和反思状态（新对话时调用）。
    /// </summary>
    public void ResetState()
    {
        _latestFrameImageIndex = -1;
        _reflectionState.Reset();
    }

    /// <summary>
    /// 注入帧图片消息：新帧永远紧跟本轮 tool 文本结果追加到历史末尾，
    /// 旧帧降级为纯文本占位（保留对话时间轴上的位置，不再携带像素）。
    /// 不变式：历史中至多一张真图，且总在最近一次 get_frame 的 tool 消息之后——
    /// 视觉模型按消息位置归属画面时序，若把新图原地回写到旧槽位，
    /// 它会落在后续动作轮之前而被模型解读为"行动前的画面"（旧画面问题的根因）。
    /// </summary>
    private void AttachFrameImage(List<ChatMessage> history, ChatMessage img)
    {
        if (_latestFrameImageIndex >= 0 && _latestFrameImageIndex < history.Count)
            history[_latestFrameImageIndex] = ChatMessage.User(FrameOmittedPlaceholder);

        _latestFrameImageIndex = history.Count;
        history.Add(img);
    }

    /// <summary>
    /// 执行完整的 Agent 对话循环。
    /// </summary>
    /// <param name="history">对话历史（会被修改，追加新消息）。</param>
    /// <param name="modelId">模型 ID。</param>
    /// <param name="provider">API 供应商配置。</param>
    /// <param name="onEvent">事件回调，ViewModel 据此更新 UI。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="visionSupported">当前模型是否支持视觉输入，决定 get_frame 是否可用。</param>
    public async Task RunAsync(
        List<ChatMessage> history,
        string modelId,
        ProviderConfig provider,
        Action<AgentEvent> onEvent,
        CancellationToken ct,
        bool visionSupported = true)
    {
        const int maxStreamRetries = 2;
        var toolDefs = _tools.ToToolDefinitions();
        var hasTools = toolDefs.Count > 0;
        var pendingReply = new StringBuilder();
        var pendingThinking = new StringBuilder();

        // 重置反思状态（用户新消息）
        _reflectionState.Reset();

        // 订阅调试日志与请求阶段重试，转发到 UI
        var debugHandler = new Action<string>(msg => onEvent(new AgentEvent.DebugInfo(msg)));
        OpenAIChatClient.DebugLog += debugHandler;
        var retryHandler = new Action<int, int, string>((attempt, maxAttempts, reason) =>
            onEvent(new AgentEvent.Retrying(attempt, maxAttempts, reason)));
        OpenAIChatClient.RequestRetrying += retryHandler;
        try
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var streamSuccess = false;
                var accumulator = new ToolCallAccumulator();
                var hasContent = false;

                // RoundStart 在流式重试循环之外发射，确保即使重试 UI 也只创建一个 AssistantMessage
                onEvent(new AgentEvent.RoundStart(""));
                pendingReply.Clear();
                pendingThinking.Clear();

                // ── 流式断线重连循环 ──
                for (var streamRetry = 0; streamRetry <= maxStreamRetries; streamRetry++)
                {
                    accumulator = new ToolCallAccumulator();

                    var client = _clientFactory?.Invoke(provider) ?? ChatClientFactory.Create(provider);
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

                    var retryableError = false;
                    hasContent = false;

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
                                if (delta.Retryable)
                                {
                                    // 流式传输中断 → 标记为重试，不追加到回复
                                    retryableError = true;
                                }
                                else
                                {
                                    pendingReply.Append($"[错误] {delta.Text}");
                                    onEvent(new AgentEvent.Error(delta.Text));
                                }
                                break;
                            case DeltaType.Usage:
                                onEvent(new AgentEvent.UsageUpdated(delta.PromptTokens, delta.CompletionTokens, delta.TotalTokens));
                                break;
                        }

                        if (retryableError) break;
                    }

                    // 可重试错误 → 丢弃本轮部分数据，等待后重发同一轮请求
                    if (retryableError && streamRetry < maxStreamRetries)
                    {
                        pendingReply.Clear();
                        pendingThinking.Clear();
                        onEvent(new AgentEvent.Retrying(
                            streamRetry + 1, maxStreamRetries, "流式传输中断，正在重新连接..."));
                        await Task.Delay(TimeSpan.FromSeconds(2 * (streamRetry + 1)), ct);
                        continue;
                    }

                    // 可重试错误但重试已耗尽
                    if (retryableError)
                    {
                        onEvent(new AgentEvent.Error("流式传输多次中断，无法恢复"));
                        var aborted = ComposeFinal(pendingReply, "[连接中断：多次重试后仍无法完成本轮响应]");
                        history.Add(ChatMessage.Assistant(aborted));
                        onEvent(new AgentEvent.Completed(aborted));
                        return;
                    }

                    streamSuccess = true;
                    break;
                }

                if (!streamSuccess) continue;

                // 没有工具调用或没有注册工具 → 本轮即最终回复
                var toolCalls = accumulator.Build();
                if (!hasTools || toolCalls.Count == 0)
                {
                    // 终局必须落历史：正常回复原样入档，流内错误文本与空回复也要留下交代，
                    // 模型下轮不应对自己上轮的输出或沉默零认知
                    history.Add(ChatMessage.Assistant(ComposeFinal(pendingReply, "[模型返回了空回复]")));
                    if (hasContent)
                        _reflectionState.TrackReflectionMarker(pendingReply.ToString());
                    onEvent(new AgentEvent.Completed(pendingReply.ToString()));
                    return;
                }

                // 将 assistant 的工具调用加入历史，并检测重复调用签名
                foreach (var tc in toolCalls)
                    _reflectionState.TrackCall(tc.Function.Name, tc.Function.Arguments);

                var callMessage = ChatMessage.Assistant(toolCalls);
                if (pendingReply.Length > 0)
                {
                    // 中间轮的助手正文（含按提示词写下的"反思：/思考："）必须随工具调用一起入历史
                    callMessage.Content = pendingReply.ToString();
                    _reflectionState.TrackReflectionMarker(pendingReply.ToString());
                }
                history.Add(callMessage);

                // 执行工具调用
                var results = await ExecuteToolCallsAsync(toolCalls, history, onEvent, ct, visionSupported);

                // 按顺序将工具结果加入历史，并处理多模态附加消息
                for (var i = 0; i < toolCalls.Count; i++)
                {
                    var content = ApplyResultBudget(results[i].Content);
                    history.Add(ChatMessage.Tool(toolCalls[i].Id, content, toolCalls[i].Function.Name));
                    _reflectionState.TrackResult(results[i].Status);

                    // 处理附加的多模态消息（如 get_frame 的图片）
                    if (results[i].AttachedMessage is { } img)
                        AttachFrameImage(history, img);
                }
            }

            // 达到最大轮次
            var final = ComposeFinal(pendingReply, "[已达到工具调用最大轮次，任务被中止]");
            history.Add(ChatMessage.Assistant(final));
            onEvent(new AgentEvent.Completed(final));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {            // 停止事实写入历史：下一轮模型需要知道自己曾被用户打断
            history.Add(ChatMessage.Assistant(ComposeFinal(pendingReply, "[已停止]")));
            throw;
        }
        finally
        {
            OpenAIChatClient.DebugLog -= debugHandler;
            OpenAIChatClient.RequestRetrying -= retryHandler;
        }
    }

    /// <summary>终止交代 = 已流出正文 + 终止标记；正文为空时仅保留标记。</summary>
    private static string ComposeFinal(StringBuilder pending, string marker)
    {
        var text = pending.ToString().Trim();
        return text.Length == 0 ? marker : text + "\n" + marker;
    }

    /// <summary>
    /// 批量执行工具调用。按模型声明顺序分组调度：Exclusive 工具独占串行，
    /// 连续声明的 Parallel（只读）工具并行走，写类工具因此天然不与任何工具并发。
    /// </summary>
    private async Task<List<ToolResult>> ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        List<ChatMessage> history,
        Action<AgentEvent> onEvent,
        CancellationToken ct,
        bool visionSupported)
    {
        // get_frame 有两条 fail-closed 约束：需要视觉能力；必须单独调用
        // （混轮并行会让画面早于本轮动作而被模型误当行动结果）。
        var mixedRound = toolCalls.Count > 1;

        Task<ToolResult> Dispatch(ToolCall tc)
        {
            if (tc.Function.Name == FrameToolName)
            {
                if (!visionSupported)
                    return RejectFrameCall(tc, FrameVisionUnsupportedError, onEvent);
                if (mixedRound)
                    return RejectFrameCall(tc, FrameMixedRoundError, onEvent);
            }

            // 危险工具确认门：无回调 fail-closed 拒绝；回调拒绝则以错误结果回传模型
            if (_tools.Get(tc.Function.Name) is { RequiresConfirmation: true })
                return ExecuteWithConfirmationAsync(tc, onEvent, ct);

            return ExecuteToolCallAsync(tc, onEvent, ct);
        }

        var results = new ToolResult[toolCalls.Count];
        var index = 0;
        while (index < toolCalls.Count)
        {
            if (!IsParallelTool(toolCalls[index]))
            {
                results[index] = await Dispatch(toolCalls[index]);
                index++;
                continue;
            }

            var end = index + 1;
            while (end < toolCalls.Count && IsParallelTool(toolCalls[end]))
                end++;

            var batch = Enumerable.Range(index, end - index)
                .Select(k => Dispatch(toolCalls[k]))
                .ToArray();
            var batchResults = await Task.WhenAll(batch);
            for (var k = index; k < end; k++)
                results[k] = batchResults[k - index];

            index = end;
        }

        return results.ToList();
    }

    private bool IsParallelTool(ToolCall toolCall) =>
        _tools.Get(toolCall.Function.Name)?.Concurrency == ToolConcurrency.Parallel;

    /// <summary>拒绝 get_frame 调用：不执行工具，直接回传错误结果（事件照常发射，保持 UI 行完整）。</summary>
    private static Task<ToolResult> RejectFrameCall(ToolCall toolCall, string error, Action<AgentEvent> onEvent)
    {
        onEvent(new AgentEvent.ToolExecuting(toolCall.Function.Name, toolCall.Id));
        onEvent(new AgentEvent.ToolCompleted(toolCall.Function.Name, toolCall.Id, error, error));
        return Task.FromResult(ToolResult.Error(error));
    }

    /// <summary>
    /// 危险工具执行路径：先向宿主请求人工确认。
    /// 未配置确认回调时 fail-closed 拒绝；用户拒绝时以可读错误回传模型，工具不执行。
    /// </summary>
    private async Task<ToolResult> ExecuteWithConfirmationAsync(
        ToolCall toolCall,
        Action<AgentEvent> onEvent,
        CancellationToken ct)
    {
        var reason = $"工具 {toolCall.Function.Name} 需要人工确认";
        if (_confirmationHandler is null)
        {
            var denied = $"{reason}，但当前环境未提供确认机制，已拒绝执行。";
            onEvent(new AgentEvent.ToolExecuting(toolCall.Function.Name, toolCall.Id));
            onEvent(new AgentEvent.ToolCompleted(toolCall.Function.Name, toolCall.Id, denied, denied));
            return ToolResult.Error(denied);
        }

        onEvent(new AgentEvent.ToolAwaitingConfirmation(toolCall.Function.Name, toolCall.Id, reason));
        bool approved;
        try
        {
            approved = await _confirmationHandler(toolCall).WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var fault = $"{reason}时确认流程出错: {ex.Message}，已拒绝执行。";
            onEvent(new AgentEvent.ToolCompleted(toolCall.Function.Name, toolCall.Id, fault, fault));
            return ToolResult.Error(fault);
        }

        if (!approved)
        {
            var rejected = $"{reason}，用户已拒绝执行。请调整方案或向用户说明后再试。";
            onEvent(new AgentEvent.ToolCompleted(toolCall.Function.Name, toolCall.Id, rejected, rejected));
            return ToolResult.Error(rejected);
        }

        return await ExecuteToolCallAsync(toolCall, onEvent, ct);
    }

    /// <summary>
    /// 执行单个工具调用，返回完整 ToolResult（含可能的多模态附加消息）。
    /// </summary>
    private async Task<ToolResult> ExecuteToolCallAsync(
        ToolCall toolCall,
        Action<AgentEvent> onEvent,
        CancellationToken ct)
    {
        var fn = toolCall.Function;
        var tool = _tools.Get(fn.Name);
        if (tool is null)
            return ToolResult.Error($"[错误] 未知工具: {fn.Name}");

        try
        {
            onEvent(new AgentEvent.ToolExecuting(fn.Name, toolCall.Id));

            var args = fn.ParseArguments();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(ToolTimeoutSeconds));

            // 工具体可能包含长时间同步 CPU 工作（截图编码、grep、目录遍历），
            // 放到线程池执行，避免整轮 ReAct 循环占用 UI 线程导致界面卡顿。
            // 注意：同步工具一旦开始执行仍无法被超时中断，取消经 token 尽力传播。
            var result = await Task.Run(() => tool.ExecuteAsync(args, timeoutCts.Token));
            var summary = Truncate(result.Content, 200);
            onEvent(new AgentEvent.ToolCompleted(fn.Name, toolCall.Id, summary, result.Content));
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
    /// 测试钩子：模拟连续工具失败以触发反思注入。走真实的 TrackResult 阈值路径。
    /// 仅供单元测试使用，生产代码不应调用。
    /// </summary>
    internal void TestHook_SimulateReflectionTrigger()
    {
        // FailureThreshold = 2：连续两次失败结果即触发
        _reflectionState.TrackResult(ToolResultStatus.Error);
        _reflectionState.TrackResult(ToolResultStatus.Error);
    }

    /// <summary>
    /// 组装发送给模型的消息列表。
    /// 结构：
    ///   [0] system: 基础角色提示词（干净，不包裹）
    ///   [1..N] user: <system-reminder> 包裹的技能内容 / 反思 / 截断
    ///   最后为对话历史。
    /// </summary>
    internal List<ChatMessage> BuildMessages(List<ChatMessage> history, int currentRound = 0)
    {
        // 基础角色提示词（始终使用 system 角色，不包裹）
        var identityPrompt = _promptAssembler is not null
            ? _promptAssembler.GetIdentityPrompt()
            : SystemPrompts.Identity;
        var baseRolePrompt = _promptAssembler is not null
            ? _promptAssembler.GetBaseRolePrompt()
            : SystemPrompts.Default;

        // 技能内容（路径 A：PromptAssembler / 路径 B：Legacy BuildSystemPrompt）
        var skillContent = _promptAssembler is not null
            ? _promptAssembler.Build(history, currentRound)
            : BuildSystemPrompt(history, currentRound);

        // 反思提示
        var reflectionPrompt = _reflectionState.ShouldInjectReflection()
            ? _reflectionState.GetReflectionPrompt()
            : null;

        // 滑动窗口截断
        List<ChatMessage> body;
        string? truncationNotice = null;
        if (history.Count <= MaxHistoryMessages)
        {
            body = history;
        }
        else
        {
            var start = SkipToSafeBoundaryIndex(history, history.Count - MaxHistoryMessages);

            // 最新帧图必须随其 tool 结果一同出现在请求内：若被滑窗裁掉，
            // 模型只收到"已获取当前画面"的文本而无图，只能凭旧记忆行动。
            // 回扩窗口把 [assistant(tool_calls), tool, user(image)] 三联整体带上。
            if (_latestFrameImageIndex >= 0 && _latestFrameImageIndex < start)
            {
                start = _latestFrameImageIndex;
                while (start > 0 && IsToolSequenceMessage(history[start - 1]))
                    start--;
            }

            truncationNotice = $"[系统提示] 为控制上下文长度，前面的 {start} 条对话已被省略。";
            body = history.Skip(start).ToList();
        }

        var messages = new List<ChatMessage>();

        // ── [0] system: 身份标识（仅一句）──
        messages.Add(ChatMessage.System(identityPrompt));

        // ── [1] system: 基础角色提示词（干净，不包裹）──
        messages.Add(ChatMessage.System(baseRolePrompt));

        // ── user: <system-reminder> 技能内容 ──
        if (!string.IsNullOrWhiteSpace(skillContent))
            messages.Add(ChatMessage.User($"<system-reminder>\n{skillContent}\n</system-reminder>"));

        // ── user: <system-reminder> 截断说明 ──
        if (truncationNotice is not null)
            messages.Add(ChatMessage.User($"<system-reminder>\n{truncationNotice}\n</system-reminder>"));

        // ── user: <system-reminder> AGENTS.md 项目指令（存在才注入，内容真实读取）──
        var agentsMd = LoadAgentsMd();
        if (agentsMd is not null)
        {
            var currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            messages.Add(ChatMessage.User($@"<system-reminder>
As you answer the user's questions, you can use the following context:
Codebase and user instructions are shown below. Be sure to adhere to these instructions. IMPORTANT: These instructions OVERRIDE any default behavior and you MUST follow them exactly as written.

Contents of AGENTS.md (user default instructions):

{agentsMd}

# currentDate
Today's date is {currentDate}.

IMPORTANT: this context may or may not be relevant to your tasks. You should not respond to this context unless it is highly relevant to your task.
</system-reminder>"));
        }

        // ── [n..] 对话历史 ──
        messages.AddRange(body);

        // 反思提醒贴着失败现场：追加在对话末尾（而非上下文前部），文本自带触发原因
        if (reflectionPrompt is not null)
            messages.Add(ChatMessage.User($"<system-reminder>\n{reflectionPrompt}\n</system-reminder>"));

        return messages;
    }

    /// <summary>
    /// 滑动窗口截断时，把截断点对齐到可作为对话起点的消息下标。
    /// 直接截断可能落在 tool 消息上，形成"孤儿 tool"（缺少前置的
    /// assistant.tool_calls），违反多数供应商的消息顺序约束。
    /// 这里向前跳过连续的 tool 消息，直到落在 user / assistant。
    /// </summary>
    private static int SkipToSafeBoundaryIndex(List<ChatMessage> history, int skip)
    {
        var start = Math.Min(skip, history.Count);
        while (start < history.Count && history[start].Role == "tool")
            start++;
        return start;
    }

    /// <summary>帧图三联回扩时需要吞并的前置消息：tool 结果，或携带 tool_calls 的 assistant。</summary>
    private static bool IsToolSequenceMessage(ChatMessage m) =>
        m.Role == "tool" || (m.ToolCalls is { Count: > 0 });

    /// <summary>AGENTS.md 注入内容的字符上限，超出即截断并注明。</summary>
    internal const int MaxAgentsMdChars = 20_000;

    /// <summary>
    /// 读取当前脚本项目目录下的 AGENTS.md（用户项目指令）。
    /// 未打开脚本、目录不存在或无此文件时不注入；读取失败静默跳过，不阻塞请求。
    /// </summary>
    private string? LoadAgentsMd()
    {
        var projectDir = _projectDirectoryProvider?.Invoke();
        if (string.IsNullOrEmpty(projectDir))
            return null;

        var path = Path.Combine(projectDir, "AGENTS.md");
        if (!File.Exists(path))
            return null;

        try
        {
            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
                return null;
            if (content.Length > MaxAgentsMdChars)
                content = content[..MaxAgentsMdChars]
                          + $"\n[AGENTS.md 内容过长已截断：仅保留前 {MaxAgentsMdChars} 字符]";
            return content;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// [Legacy fallback] 旧的硬编码技能内容组装逻辑（不含基础角色提示词）。
    /// 仅当编排器未注入 SkillRegistry 时使用（向后兼容旧测试与旧调用方）。
    /// 基础角色提示词（SystemPrompts.Default）由 BuildMessages 以 system 角色单独发送。
    ///
    /// 基于工具调用历史决定注入哪些 prompt 段落，关键词检测作为 fallback。
    /// </summary>
    internal string BuildSystemPrompt(List<ChatMessage> history, int currentRound)
    {
        var sb = new StringBuilder();

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
            sb.Append(SystemPrompts.ScriptSyntax);

        if (needsVision && needsExecution)
        {
            sb.Append(SystemPrompts.ReActLoop);
        }
        else
        {
            if (needsVision)
                sb.Append(SystemPrompts.Vision);
            if (needsExecution)
                sb.Append(SystemPrompts.ScriptExecution);
        }

        if (currentRound > 0)
        {
            var remaining = MaxToolRounds - currentRound;
            sb.Append("\n\n[系统] 当前已使用 ")
              .Append(currentRound).Append(" 轮工具调用，剩余 ").Append(remaining).Append(" 轮。");
            if (remaining <= 10)
                sb.Append(" 轮次即将耗尽，请尽快完成目标并回复用户。");
        }

        return sb.ToString();
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

    /// <summary>
    /// 单条工具结果的体积预算：超限截断并附显式尾注。
    /// 防线设在编排层入口，内置工具与 MCP 工具的长输出都不得静默挤占上下文。
    /// </summary>
    internal static string ApplyResultBudget(string content)
    {
        if (content.Length <= MaxToolResultChars)
            return content;
        return string.Concat(content.AsSpan(0, MaxToolResultChars),
            $"\n\n[工具输出过长已截断：原始 {content.Length} 字符，仅保留前 {MaxToolResultChars} 字符。]");
    }
}