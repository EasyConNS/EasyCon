using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Skills;
using EasyCon2.Avalonia.Core.AiAgent.Skills;
using EasyCon2.Avalonia.Core.AiAgent.Tools;
using EasyCon2.Avalonia.Core.Services;
using System.Collections.ObjectModel;
using System.Text;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentViewModel : ObservableObject
{
    private readonly List<ChatMessage> _history = [];
    private readonly ToolRegistry _tools = new();
    private readonly SkillRegistry _skills = new();
    private readonly IToolCallService? _toolCallService;
    private AgentOrchestrator? _orchestrator;

    private string _conversationPrefix = "";
    private readonly StringBuilder _pendingReply = new();
    private readonly StringBuilder _pendingThinking = new();
    private EasyCon.Core.LLM.Models.ModelsConfig? _cachedConfig;
    private int _totalTokensUsed;

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

    [ObservableProperty]
    private string _tokenUsage = "";

    private CancellationTokenSource? _cts;

    public ObservableCollection<ModelEntry> AllModels { get; } = [];

    private bool _initialized;

    public AiAgentViewModel() : this(null) { }

    public AiAgentViewModel(IToolCallService? toolCallService)
    {
        _toolCallService = toolCallService;
        if (toolCallService is not null)
        {
            DefaultTools.RegisterAll(_tools, toolCallService);
            InitializeSkills();
        }

        // 订阅模型配置变更通知，保存后自动刷新模型列表。
        ConfigManager.ModelsConfigChanged += OnModelsConfigChanged;
    }

    private void OnModelsConfigChanged()
    {
        // RefreshModels 已内部清除 ChatClient 缓存并重载 AllModels。
        RefreshModels();
    }

    /// <summary>
    /// 加载内置/用户/项目级技能并注册元工具（list_skills / read_skill）。
    /// 加载失败时降级为无技能模式（回退到 Orchestrator 的硬编码 prompt）。
    /// </summary>
    private void InitializeSkills()
    {
        ReloadSkills();

        // 元工具始终注册（即使无技能，list_skills 也能给出"无技能"的明确反馈）
        _tools.Register(new ListSkillsTool(_skills));
        _tools.Register(new ReadSkillTool(_skills));
    }

    /// <summary>
    /// 重新加载技能（用户修改了 skills 目录或切换项目后调用）。
    /// 优先级：内置（代码初始化）→ 用户（文件系统）→ 项目级（文件系统），后者覆盖前者。
    /// </summary>
    public void ReloadSkills()
    {
        _skills.Clear();
        try
        {
            // 1. 内置技能（代码直接初始化，优先级最低）
            foreach (var skill in BundledSkills.CreateAll())
                _skills.Register(skill);

            // 2. 用户和项目级技能（文件系统，优先级更高，可覆盖内置）
            var projectDir = _toolCallService?.GetProjectDirectory();
            SkillLoader.LoadToRegistry(_skills, SkillLoader.GetSearchPaths(projectDir));
        }
        catch
        {
            // 技能加载失败不应阻塞 Agent 初始化
        }
    }

    partial void OnSelectedEntryChanged(ModelEntry? value)
    {
        if (_toolCallService is null)
        {
            Console.WriteLine("工具服务为空！！");
            return;
        }

        // 模型切换后重建编排器（注入技能体系）
        _orchestrator = new AgentOrchestrator(_tools, _skills);
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
        _orchestrator?.ResetState();
        _conversationPrefix = "";
        _pendingReply.Clear();
        _pendingThinking.Clear();
        _totalTokensUsed = 0;
        ConversationText = "";
        TokenUsage = "";
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
            _orchestrator ??= new AgentOrchestrator(_tools, _skills);

            var provider = GetProviderConfig(SelectedEntry.ProviderKey);
            await _orchestrator.RunAsync(_history, SelectedEntry.ModelId, provider, HandleAgentEvent, _cts.Token);
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
    /// 处理编排器事件，更新 UI 状态。
    /// </summary>
    private void HandleAgentEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case AgentEvent.RoundStart:
                _conversationPrefix = AppendMessage(_conversationPrefix, "AI", "");
                _pendingReply.Clear();
                _pendingThinking.Clear();
                ConversationText = _conversationPrefix;
                break;

            case AgentEvent.ContentDelta d:
                _pendingReply.Append(d.Text);
                ConversationText = BuildDisplayText();
                break;

            case AgentEvent.ThinkingDelta d:
                _pendingThinking.Append(d.Text);
                ConversationText = BuildDisplayText();
                break;

            case AgentEvent.ToolExecuting t:
                _conversationPrefix = AppendMessage(_conversationPrefix, "工具", $"{t.Name} → 执行中...");
                ConversationText = _conversationPrefix;
                break;

            case AgentEvent.ToolCompleted t:
                // 替换最后一行的"执行中..."为实际结果
                _conversationPrefix = AppendMessage(_conversationPrefix, "工具", $"{t.Name} → {t.Summary}");
                ConversationText = _conversationPrefix;
                break;

            case AgentEvent.UsageUpdated u:
                _totalTokensUsed += u.Total;
                TokenUsage = $"本轮: {u.Total} tokens | 累计: {_totalTokensUsed}";
                break;

            case AgentEvent.Error e:
                _pendingReply.Append($"[错误] {e.Message}");
                ConversationText = BuildDisplayText();
                break;

            case AgentEvent.Completed:
                // 最终内容由编排器写入 history，这里不需要额外操作
                break;
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
        ChatClientFactory.ClearCache();
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
                    ProviderLabel = string.IsNullOrWhiteSpace(provider.Name) ? providerKey : provider.Name,
                    Vision = m.Vision
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
}
