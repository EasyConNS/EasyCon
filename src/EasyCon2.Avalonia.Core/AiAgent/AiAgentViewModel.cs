using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Skills;
using EasyCon2.Avalonia.Core.AiAgent.Skills;
using EasyCon2.Avalonia.Core.AiAgent.Tools;
using EasyCon2.Avalonia.Core.Mcp;
using EasyCon2.Avalonia.Core.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentViewModel : ObservableObject
{
    private readonly List<ChatMessage> _history = [];
    private readonly ToolRegistry _tools = new();
    private readonly SkillRegistry _skills = new();
    private readonly IToolCallService? _toolCallService;
    private readonly IMcpManager? _mcpManager;
    private AgentOrchestrator? _orchestrator;

    // 保留（用于流式内容节流累加）
    private readonly StringBuilder _pendingReply = new();
    private readonly StringBuilder _pendingThinking = new();

    // 新增
    private AssistantMessage? _currentAssistant;
    private readonly Dictionary<string, ToolCallMessage> _pendingTools = new();
    private EasyCon.Core.LLM.Models.ModelsConfig? _cachedConfig;
    private int _totalTokensUsed;
    private readonly List<string> _debugLogs = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private ModelEntry? _selectedEntry;

    [ObservableProperty]
    private string _tokenUsage = "tokens: --";

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    [ObservableProperty]
    private bool _showDebugPanel;

    /// <summary>对话标题，首次请求结束后由模型自动生成。</summary>
    [ObservableProperty]
    private string _sessionTitle = "AI Agent";

    private bool _hasGeneratedTitle;

    /// <summary>调试日志 — 原始 SSE 数据和解析异常，用于诊断模型兼容性问题。</summary>
    public string DebugLogText => string.Join("\n", _debugLogs);
    public bool HasDebugLogs => _debugLogs.Count > 0;

    private CancellationTokenSource? _cts;
    private bool _uiUpdateScheduled;

    public ObservableCollection<ModelEntry> AllModels { get; } = [];

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    private bool _initialized;

    public AiAgentViewModel() : this(null, null) { }

    public AiAgentViewModel(IToolCallService? toolCallService) : this(toolCallService, null) { }

    public AiAgentViewModel(IToolCallService? toolCallService, IMcpManager? mcpManager)
    {
        _toolCallService = toolCallService;
        _mcpManager = mcpManager;
        if (toolCallService is not null)
        {
            DefaultTools.RegisterAll(_tools, toolCallService);
            InitializeSkills();
        }

        // 订阅模型配置变更通知，保存后自动刷新模型列表。
        ConfigManager.ModelsConfigChanged += OnModelsConfigChanged;

        // 订阅 MCP 配置变更通知，保存后差量重连服务器并刷新工具集。
        ConfigManager.McpConfigChanged += OnMcpConfigChanged;

        // 订阅 MCP 工具集变更，重注册适配器。
        if (_mcpManager is not null)
            _mcpManager.ToolsChanged += OnMcpToolsChanged;
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

        // execute_skill：延迟解析 provider/modelId（模型可能切换）
        var executor = new SkillExecutor(_skills, _tools,
            getProvider: () => SelectedEntry is not null
                ? GetProviderConfig(SelectedEntry.ProviderKey)
                : new ProviderConfig(),
            getModelId: () => SelectedEntry?.ModelId ?? "");
        _tools.Register(new ExecuteSkillTool(executor));
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
            var paths = SkillLoader.GetSearchPaths(projectDir).ToList();
            SkillLoader.LoadToRegistry(_skills, paths);

            Console.WriteLine(
                $"[AiAgent] 技能加载完成: 共 {_skills.All.Count} 个技能, " +
                $"搜索路径: [{string.Join(", ", paths)}]");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[AiAgent] 技能加载失败: {ex.Message}");
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

    partial void OnIsOpenChanged(bool value)
    {
        if (value && !_initialized)
        {
            _initialized = true;
            RefreshModels();
            // 首次打开时重新加载技能（确保用户技能目录变更后能被感知）
            ReloadSkills();
            // 首次打开时初始化 MCP 连接（fire-and-forget，不阻塞 UI）。
            if (_mcpManager is not null)
                _ = InitializeMcpAsync();
        }
    }

    private async Task InitializeMcpAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _mcpManager!.InitializeAsync(cts.Token);
        }
        catch (Exception ex)
        {
            // MCP 初始化失败不阻塞 Agent，仅记录日志。
            Dispatcher.UIThread.Post(() => Messages.Add(new ToolCallMessage { Name = "MCP", Summary = $"初始化失败: {ex.Message}", Success = false }));
        }
    }

    private void OnMcpConfigChanged()
    {
        if (_mcpManager is null) return;
        // 配置变更后差量重连（fire-and-forget）。
        _ = RefreshMcpAsync();
    }

    private async Task RefreshMcpAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _mcpManager!.RefreshAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Messages.Add(new ToolCallMessage { Name = "MCP", Summary = $"刷新失败: {ex.Message}", Success = false }));
        }
    }

    private void OnMcpToolsChanged()
    {
        // 移除所有旧的 MCP 工具适配器，重新注册。
        _tools.Unregister(t => t is McpToolAdapter);
        McpTools.RegisterAll(_tools, _mcpManager!);
    }

    [RelayCommand]
    private void ToggleDebugPanel() => ShowDebugPanel = !ShowDebugPanel;

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
        _currentAssistant = null;
        _pendingTools.Clear();
        _totalTokensUsed = 0;
        Messages.Clear();
        TokenUsage = "tokens: --";
        SessionTitle = "AI Agent";
        _hasGeneratedTitle = false;
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
            Messages.Add(new ToolCallMessage { Name = "提示", Summary = hint, Success = false });
            return;
        }

        var message = InputText.Trim();
        InputText = "";
        IsGenerating = true;

        Messages.Add(new UserMessage { Content = message });
        _history.Add(ChatMessage.User(message));

        _cts = new CancellationTokenSource();
        try
        {
            _orchestrator ??= new AgentOrchestrator(_tools, _skills);

            var provider = GetProviderConfig(SelectedEntry.ProviderKey);
            await _orchestrator.RunAsync(_history, SelectedEntry.ModelId, provider, HandleAgentEvent, _cts.Token);

            // 首次请求结束后异步生成对话标题（不阻塞主流程）
            if (!_hasGeneratedTitle)
            {
                _hasGeneratedTitle = true;
                var firstMessage = message;
                var capturedProvider = provider;
                _ = GenerateTitleAsync(firstMessage, capturedProvider, SelectedEntry.ModelId);
            }
        }
        catch (OperationCanceledException)
        {
            var prev = _currentAssistant;
            if (prev is not null)
                Dispatcher.UIThread.Post(() => prev.Content += "\n[已停止]");
        }
        catch (Exception ex)
        {
            var prev = _currentAssistant;
            if (prev is not null)
                Dispatcher.UIThread.Post(() => prev.Content += $"\n[错误] {ex.Message}");
        }
        finally
        {
            _pendingReply.Clear();
            _pendingThinking.Clear();
            var prev = _currentAssistant;
            if (prev is not null)
                Dispatcher.UIThread.Post(() => prev.IsStreaming = false);
            _currentAssistant = null;
            _pendingTools.Clear();
            _cts?.Dispose();
            _cts = null;
            IsGenerating = false;
        }
    }

    /// <summary>
    /// 处理编排器事件，更新 UI 状态。
    /// 连续事件（ContentDelta / ThinkingDelta）经过节流，避免 UI 线程被高频更新淹没。
    /// 离散事件（ToolExecuting / ToolCompleted 等）直接派发到 UI 线程。
    /// </summary>
    private void HandleAgentEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case AgentEvent.RoundStart:
                _pendingReply.Clear();
                _pendingThinking.Clear();
                // 将上一轮正在流式的 AI 消息标记为完成，自动折叠思考
                var prev = _currentAssistant;
                _currentAssistant = new AssistantMessage { IsStreaming = true };
                _pendingTools.Clear();
                if (prev is not null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        prev.IsStreaming = false;
                        if (prev.Thinking is not null)
                            prev.Thinking.IsExpanded = false;
                    }, DispatcherPriority.Background);
                }
                Dispatcher.UIThread.Post(() => Messages.Add(_currentAssistant), DispatcherPriority.Background);
                break;

            case AgentEvent.ContentDelta d:
                _pendingReply.Append(d.Text);
                ScheduleUiUpdate();
                break;

            case AgentEvent.ThinkingDelta d:
                _pendingThinking.Append(d.Text);
                ScheduleUiUpdate();
                break;

            case AgentEvent.ToolExecuting t:
                var toolMsg = new ToolCallMessage { Name = t.Name, Success = false, Summary = "执行中..." };
                _pendingTools[t.Name] = toolMsg;
                Dispatcher.UIThread.Post(() => Messages.Add(toolMsg), DispatcherPriority.Background);
                break;

            case AgentEvent.ToolCompleted t:
                if (_pendingTools.TryGetValue(t.Name, out var pending))
                {
                    var msg = pending;
                    Dispatcher.UIThread.Post(() =>
                    {
                        msg.Success = true;
                        msg.Summary = t.Summary;
                        msg.FullResult = t.FullResult;
                    }, DispatcherPriority.Background);
                }
                break;

            case AgentEvent.UsageUpdated u:
                Dispatcher.UIThread.Post(() =>
                {
                    _totalTokensUsed += u.Total;
                    TokenUsage = $"本轮: {u.Total} tokens | 累计: {_totalTokensUsed}";
                }, DispatcherPriority.Background);
                break;

            case AgentEvent.Retrying r:
                Dispatcher.UIThread.Post(() =>
                {
                    Messages.Add(new ToolCallMessage
                    {
                        Name = "重试",
                        Summary = $"第 {r.Attempt}/{r.MaxAttempts} 次重试: {r.Reason}",
                        Success = false
                    });
                }, DispatcherPriority.Background);
                break;

            case AgentEvent.Error e:
                if (_currentAssistant is not null)
                {
                    var current = _currentAssistant;
                    Dispatcher.UIThread.Post(() => current.Content += $"[错误] {e.Message}", DispatcherPriority.Background);
                }
                break;

            case AgentEvent.DebugInfo d:
                Dispatcher.UIThread.Post(() =>
                {
                    _debugLogs.Add(d.Message);
                    while (_debugLogs.Count > 50)
                        _debugLogs.RemoveAt(0);
                    OnPropertyChanged(nameof(DebugLogText));
                    OnPropertyChanged(nameof(HasDebugLogs));
                }, DispatcherPriority.Background);
                break;

            case AgentEvent.Completed:
                _uiUpdateScheduled = false;
                if (_currentAssistant is not null)
                {
                    var current = _currentAssistant;
                    // 先直接设置内容（节流回调可能尚未执行，Background 优先级最低）
                    current.Content = _pendingReply.ToString().TrimStart('\n', '\r');
                    if (_pendingThinking.Length > 0)
                    {
                        current.Thinking ??= new ThinkingBlock();
                        current.Thinking.Text = _pendingThinking.ToString();
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        current.IsStreaming = false;
                        if (current.Thinking is not null)
                            current.Thinking.IsExpanded = false;
                    }, DispatcherPriority.Background);
                }
                _pendingTools.Clear();
                break;
        }
    }

    /// <summary>
    /// 节流式 UI 更新：将 StringBuilder 中的累积内容写入当前 AssistantMessage。
    /// 用于 ContentDelta / ThinkingDelta 等高频连续事件。
    /// </summary>
    private void ScheduleUiUpdate()
    {
        if (_uiUpdateScheduled) return;
        _uiUpdateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _uiUpdateScheduled = false;
            if (_currentAssistant is not null)
            {
                _currentAssistant.Content = _pendingReply.ToString().TrimStart('\n', '\r');
                if (_pendingThinking.Length > 0)
                {
                    _currentAssistant.Thinking ??= new ThinkingBlock();
                    _currentAssistant.Thinking.Text = _pendingThinking.ToString();
                }
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 异步生成对话标题，通过非流式请求让模型总结会话主题。
    /// 解析 JSON 格式响应 {"title":"..."} 并更新 SessionTitle。
    /// 失败时静默降级，不影响主对话流程。
    /// </summary>
    private async Task GenerateTitleAsync(string userMessage, ProviderConfig provider, string modelId)
    {
        try
        {
            var client = ChatClientFactory.Create(provider);
            var request = new ChatRequest
            {
                Model = modelId,
                Messages =
                [
                    ChatMessage.System("Generate a concise title for this coding session.\n\nRules:\n- Use the user's primary language.\n- Use 3-7 words when possible.\n- Keep it recognizable in a session list.\n- Preserve important proper nouns, file names, APIs, and technology names.\n- Do not use markdown, numbering, quotes, trailing punctuation, or explanations.\n- Return only JSON in this shape: {\"title\":\"...\"}"),
                    ChatMessage.User(userMessage)
                ],
                Temperature = 0.3,
                MaxTokens = 100
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await client.SendAsync(request, cts.Token);

            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                using var doc = JsonDocument.Parse(response.Content);
                if (doc.RootElement.TryGetProperty("title", out var titleEl))
                {
                    var title = titleEl.GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        var trimmed = title.Trim().Trim('"', '\'', '，', '。');
                        Dispatcher.UIThread.Post(() => SessionTitle = trimmed);
                    }
                }
            }
        }
        catch
        {
            // 标题生成失败不影响主对话，静默降级
        }
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
}
