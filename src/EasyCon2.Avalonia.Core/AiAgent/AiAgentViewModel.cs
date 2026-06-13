using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.AiAgent.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentViewModel : ObservableObject
{
    /// <summary>工具调度最大循环次数，防止模型无限调用工具。</summary>
    private const int MaxToolRounds = 8;

    private readonly List<ChatMessage> _history = [];
    private readonly ToolRegistry _tools = new();

    private string _conversationPrefix = "";
    private readonly StringBuilder _pendingReply = new();
    private readonly StringBuilder _pendingThinking = new();
    private ToolCallAccumulator _toolAccumulator = new();
    private ModelsConfig? _cachedConfig;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _conversationText = "";

    [ObservableProperty]
    private int _outputCaretIndex;

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private ModelEntry? _selectedEntry;

    private CancellationTokenSource? _cts;

    public ObservableCollection<ModelEntry> AllModels { get; } = [];

    private bool _initialized;

    public AiAgentViewModel() : this(null) { }

    public AiAgentViewModel(IToolCallService? toolCallService)
    {
        if (toolCallService is not null)
        {
            _tools.Register(new ReadScriptTool(toolCallService));
            _tools.Register(new WriteScriptTool(toolCallService));
            _tools.Register(new EditScriptTool(toolCallService));
            _tools.Register(new GrepScriptTool(toolCallService));
            _tools.Register(new CompileScriptTool(toolCallService));
            _tools.Register(new FormatScriptTool(toolCallService));
            _tools.Register(new GetDeviceStatusTool(toolCallService));
            _tools.Register(new GetLogsTool(toolCallService));
        }
    }

    partial void OnConversationTextChanged(string value)
    {
        OutputCaretIndex = value.Length;
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value && !_initialized)
        {
            _initialized = true;
            RefreshModels();
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private void NewChat()
    {
        _history.Clear();
        _conversationPrefix = "";
        _pendingReply.Clear();
        _pendingThinking.Clear();
        ConversationText = "";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SendOrStopAsync()
    {
        if (IsGenerating)
        {
            _cts?.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(InputText))
            return;

        if (SelectedEntry is null)
        {
            var hint = AllModels.Count == 0
                ? "未配置模型供应商。请在配置目录下创建 models.json 文件。"
                : "请先选择一个模型。";
            ConversationText = AppendMessage(ConversationText, "系统", hint);
            return;
        }

        var message = InputText.Trim();
        InputText = "";
        IsGenerating = true;

        _conversationPrefix = AppendMessage(_conversationPrefix, "你", message);
        _history.Add(ChatMessage.User(message));

        _cts = new CancellationTokenSource();
        try
        {
            await RunConversationAsync(_cts.Token);
            _conversationPrefix = ConversationText;
        }
        catch (OperationCanceledException)
        {
            ConversationText = _conversationPrefix + "[已停止]";
            _conversationPrefix = ConversationText;
        }
        catch (Exception ex)
        {
            ConversationText = _conversationPrefix + $"[错误] {ex.Message}";
            _conversationPrefix = ConversationText;
        }
        finally
        {
            _pendingReply.Clear();
            _pendingThinking.Clear();
            _cts?.Dispose();
            _cts = null;
            IsGenerating = false;
        }
    }

    /// <summary>
    /// 执行对话主循环：发送请求 → 流式接收 → 若有工具调用则执行并回传 → 循环。
    /// </summary>
    private async Task RunConversationAsync(CancellationToken ct)
    {
        var provider = GetProviderConfig(SelectedEntry!.ProviderKey);
        var toolDefs = _tools.ToToolDefinitions();
        var hasTools = toolDefs.Count > 0;

        for (var round = 0; round < MaxToolRounds; round++)
        {
            // 每轮新建对话前缀占位
            _conversationPrefix = AppendMessage(_conversationPrefix, "AI", "");
            _pendingReply.Clear();
            _pendingThinking.Clear();
            _toolAccumulator = new ToolCallAccumulator();
            ConversationText = _conversationPrefix;

            using var client = ChatClientFactory.Create(provider);
            var request = new ChatRequest
            {
                Model = SelectedEntry.ModelId,
                Messages = BuildMessages()
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
                        _pendingThinking.Append(delta.Text);
                        break;
                    case DeltaType.Content:
                        _pendingReply.Append(delta.Text);
                        hasContent = true;
                        break;
                    case DeltaType.ToolCall:
                        if (delta.ToolCallDelta is not null)
                            _toolAccumulator.Append(delta.ToolCallDelta);
                        break;
                    case DeltaType.Error:
                        _pendingReply.Append($"[错误] {delta.Text}");
                        break;
                }

                ConversationText = BuildDisplayText();
            }

            // 没有工具调用或没有注册工具 → 本轮即最终回复，结束循环
            var toolCalls = _toolAccumulator.Build();
            if (!hasTools || toolCalls.Count == 0)
            {
                if (hasContent)
                    _history.Add(ChatMessage.Assistant(_pendingReply.ToString()));
                return;
            }

            // 将 assistant 的工具调用加入历史（回放上下文）
            _history.Add(ChatMessage.Assistant(toolCalls));

            // 批量执行工具调用（并行执行，保持结果顺序）
            var results = await ExecuteToolCallsAsync(toolCalls, ct);

            // 按顺序将工具结果加入历史
            for (var i = 0; i < toolCalls.Count; i++)
            {
                _history.Add(ChatMessage.Tool(toolCalls[i].Id, results[i], toolCalls[i].Function.Name));
            }

            // 工具结果已追加到历史，下一轮继续对话
        }

        // 达到最大轮次仍未结束
        _history.Add(ChatMessage.Assistant(_pendingReply.ToString()));
        _conversationPrefix += "\n[已达到工具调用最大轮次]";
    }

    /// <summary>
    /// 批量执行工具调用（并行执行，保持结果顺序）。
    /// </summary>
    private async Task<List<string>> ExecuteToolCallsAsync(List<ToolCall> toolCalls, CancellationToken ct)
    {
        // 单个工具调用直接执行
        if (toolCalls.Count == 1)
        {
            var result = await ExecuteToolCallAsync(toolCalls[0], ct);
            return [result];
        }

        // 多个工具调用并行执行
        var tasks = toolCalls.Select(tc => ExecuteToolCallAsync(tc, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// 执行单个工具调用，返回结果文本。
    /// </summary>
    private async Task<string> ExecuteToolCallAsync(ToolCall toolCall, CancellationToken ct)
    {
        var fn = toolCall.Function;
        var tool = _tools.Get(fn.Name);
        if (tool is null)
            return $"[错误] 未知工具: {fn.Name}";

        try
        {
            var args = fn.ParseArguments();
            var result = await tool.ExecuteAsync(args, ct);
            _conversationPrefix = AppendMessage(_conversationPrefix, "工具", $"{fn.Name} → {Truncate(result, 200)}");
            ConversationText = _conversationPrefix;
            return result;
        }
        catch (Exception ex)
        {
            return $"[工具执行异常] {ex.Message}";
        }
    }

    /// <summary>
    /// 拼接当前显示文本（前缀 + 思考块 + 待定回复）。
    /// </summary>
    private string BuildDisplayText()
    {
        var thinkingBlock = _pendingThinking.Length > 0
            ? FormatThinkingBlock(_pendingThinking.ToString())
            : "";
        return string.Concat(_conversationPrefix, thinkingBlock, _pendingReply);
    }

    [RelayCommand]
    private void RefreshModels()
    {
        _cachedConfig = ConfigManager.LoadModelsConfig();
        AllModels.Clear();

        foreach (var (providerKey, provider) in _cachedConfig.Models.Providers)
        {
            if (provider.Api != "openai-completions") continue;

            foreach (var m in provider.Models)
            {
                AllModels.Add(new ModelEntry
                {
                    ProviderKey = providerKey,
                    ModelId = m.Id,
                    ModelName = m.Name,
                    ProviderLabel = providerKey
                });
            }
        }

        SelectedEntry = AllModels.Count > 0 ? AllModels[0] : null;
    }

    private ProviderConfig GetProviderConfig(string providerKey)
    {
        _cachedConfig ??= ConfigManager.LoadModelsConfig();
        return _cachedConfig.Models.Providers[providerKey];
    }

    private List<ChatMessage> BuildMessages()
    {
        // 基础系统提示词
        var systemPrompt = SystemPrompts.Default;

        // 检查最近几条用户消息是否涉及脚本相关话题
        if (IsScriptRelatedQuery(_history))
        {
            systemPrompt += SystemPrompts.ScriptSyntax;
        }

        var messages = new List<ChatMessage>(_history.Count + 1) { ChatMessage.System(systemPrompt) };
        messages.AddRange(_history);
        return messages;
    }

    /// <summary>
    /// 判断最近的用户消息是否涉及脚本编写或理解。
    /// </summary>
    private static bool IsScriptRelatedQuery(List<ChatMessage> history)
    {
        // 检查最近3条用户消息
        var recentUserMessages = history
            .Where(m => m.Role == "user")
            .TakeLast(3)
            .Select(m => m.Content?.ToString()?.ToLowerInvariant() ?? "")
            .ToList();

        var scriptKeywords = new[]
        {
            "脚本", "script", "ecs", "语法", "函数", "循环", "条件", "变量",
            "按键", "摇杆", "图像识别", "编译", "格式化", "代码", "编写",
            "for", "if", "func", "while", "break", "continue",
            "print", "alert", "wait", "time", "rand",
            "怎么写", "如何写", "帮我写", "示例", "教程", "文档"
        };

        return recentUserMessages.Any(msg =>
            scriptKeywords.Any(keyword => msg.Contains(keyword)));
    }

    private static string AppendMessage(string existing, string role, string content)
    {
        var sep = string.IsNullOrEmpty(existing) ? "" : $"{Environment.NewLine}{Environment.NewLine}";
        return $"{existing}{sep}{role}: {content}";
    }

    /// <summary>
    /// 将思考内容格式化为可折叠文本块。
    /// </summary>
    internal static string FormatThinkingBlock(string thinking)
    {
        if (string.IsNullOrEmpty(thinking)) return "";
        return $"<details>{Environment.NewLine}<summary>💭 思考过程</summary>{Environment.NewLine}{thinking}{Environment.NewLine}</details>{Environment.NewLine}{Environment.NewLine}";
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max] + "...";
    }
}
