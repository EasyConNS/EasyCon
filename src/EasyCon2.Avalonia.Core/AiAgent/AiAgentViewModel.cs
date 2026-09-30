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
using EasyCon2.Avalonia.Core.Threading;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentViewModel : ObservableObject, IDisposable
{
    private readonly List<ChatMessage> _history = [];
    private readonly ToolRegistry _tools = new();
    private readonly SkillRegistry _skills = new();
    private readonly IToolCallService? _toolCallService;
    private readonly IMcpManager? _mcpManager;
    private readonly IUiDispatcher _ui;
    private AgentOrchestrator? _orchestrator;

    // 保留（用于流式内容节流累加）
    private readonly StringBuilder _pendingReply = new();
    private readonly StringBuilder _pendingThinking = new();

    // 新增
    private AssistantMessage? _currentAssistant;
    // 并发容器：停止/出错的收尾与已派发的并行工具回调（ExecuteToolCallAsync）
    // 可能仍在对它写入，普通 Dictionary 会撞坏。
    // key = 工具调用 Id：同轮并行调用同名工具时两条调用不得共用一个 UI 行
    private readonly ConcurrentDictionary<string, ToolCallMessage> _pendingTools = new();
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
    private int _sessionVersion;

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

    public AiAgentViewModel(IToolCallService? toolCallService, IMcpManager? mcpManager, IUiDispatcher? uiDispatcher = null)
    {
        _toolCallService = toolCallService;
        _mcpManager = mcpManager;
        _ui = uiDispatcher ?? SynchronousUiDispatcher.Instance;
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

        // execute_skill：延迟解析 provider/modelId（模型可能切换）；
        // 无有效供应商时返回 null，由执行器给出可读错误而不是构造空配置
        var executor = new SkillExecutor(_skills, _tools,
            getProvider: () => SelectedEntry is not null
                && TryGetProviderConfig(SelectedEntry.ProviderKey, out var entryProvider)
                    ? entryProvider
                    : null,
            getModelId: () => SelectedEntry?.ModelId ?? "");
        _tools.Register(new ExecuteSkillTool(executor));
    }

    /// <summary>
    /// 重新加载技能（用户修改了 skills 目录或切换项目后调用）。
    /// 优先级：内置（代码初始化）→ 用户（文件系统）→ 项目级（文件系统），后者覆盖前者。
    /// </summary>
    public void ReloadSkills()
    {
        // 生成中技能表正被编排器后台线程读取，清空/重装会与之竞争（与 NewChat 同款守卫）
        if (IsGenerating)
            return;
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

            DiagLog?.Invoke(
                $"[AiAgent] 技能加载完成: 共 {_skills.All.Count} 个技能, " +
                $"搜索路径: [{string.Join(", ", paths)}]");
        }
        catch (Exception ex)
        {
            DiagLog?.Invoke(
                $"[AiAgent] 技能加载失败: {ex.Message}");
        }
    }

    partial void OnSelectedEntryChanged(ModelEntry? value)
    {
        if (_toolCallService is null)
        {
            DiagLog?.Invoke("[AiAgent] 工具服务为空，无法重建编排器");
            return;
        }

        // 生成中重建编排器会与在跑的 RunAsync 并发写同一份历史，与 NewChat 同款守卫
        if (IsGenerating)
            return;

        // 模型切换后重建编排器（注入技能体系 + 脚本项目目录，供 AGENTS.md 装配）
        _orchestrator = new AgentOrchestrator(_tools, _skills,
            clientFactory: null,
            projectDirectoryProvider: () => _toolCallService?.GetProjectDirectory());
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
            _ui.Post(() => Messages.Add(new ToolCallMessage { Name = "MCP", Summary = $"初始化失败: {ex.Message}", Success = false }));
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
            _ui.Post(() => Messages.Add(new ToolCallMessage { Name = "MCP", Summary = $"刷新失败: {ex.Message}", Success = false }));
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
        // 生成中清空会与编排器后台线程的 _history/Messages 写入竞争，直接忽略
        if (IsGenerating)
            return;

        _history.Clear();
        _orchestrator?.ResetState();
        _currentAssistant = null;
        _pendingTools.Clear();
        _totalTokensUsed = 0;
        _sessionVersion++;
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
            // 读入局部变量：首个调用的 finally 可能正在 Dispose 同一实例
            var cts = _cts;
            if (cts is not null)
            {
                try
                {
                    await cts.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // 首个调用刚好收尾完成，无需取消
                }
            }
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

        // 发送前 fail-fast 校验供应商配置：配置错误直接给出可读提示，
        // 而不是发出注定失败的请求后静默（或抛出晦涩的英文异常）
        if (!TryGetProviderConfig(SelectedEntry.ProviderKey, out var provider))
        {
            Messages.Add(new ToolCallMessage
            {
                Name = "提示",
                Summary = ValidateProvider(SelectedEntry.ProviderKey, null)!,
                Success = false
            });
            return;
        }

        if (ValidateProvider(SelectedEntry.ProviderKey, provider) is { } validationError)
        {
            Messages.Add(new ToolCallMessage { Name = "提示", Summary = validationError, Success = false });
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
            _orchestrator ??= new AgentOrchestrator(_tools, _skills,
                clientFactory: null,
                projectDirectoryProvider: () => _toolCallService?.GetProjectDirectory());

            await _orchestrator.RunAsync(_history, SelectedEntry.ModelId, provider, HandleAgentEvent, _cts.Token,
                SelectedEntry.Vision);

            // 首次请求结束后异步生成对话标题（不阻塞主流程）；
            // 请求不绑会话取消令牌，以会话版本号防止旧标题写进新会话
            if (!_hasGeneratedTitle)
            {
                _hasGeneratedTitle = true;
                var firstMessage = message;
                var session = _sessionVersion;
                _ = GenerateTitleAsync(firstMessage, provider, SelectedEntry.ModelId, session);
            }
        }
        catch (OperationCanceledException)
        {
            var prev = _currentAssistant;
            if (prev is not null)
                _ui.Post(() => prev.Content += "\n[已停止]");
        }
        catch (Exception ex)
        {
            DiagLog?.Invoke($"[AiAgent] 发送失败: {ex}");
            var prev = _currentAssistant;
            if (prev is not null)
                _ui.Post(() => prev.Content += $"\n[错误] {ex.Message}");
            else
                _ui.Post(() => Messages.Add(new ToolCallMessage { Name = "错误", Summary = ex.Message, Success = false }));
        }
        finally
        {
            _pendingReply.Clear();
            _pendingThinking.Clear();
            var prev = _currentAssistant;
            if (prev is not null)
                _ui.Post(() => prev.IsStreaming = false);
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
                    _ui.Post(() =>
                    {
                        prev.IsStreaming = false;
                        if (prev.Thinking is not null)
                            prev.Thinking.IsExpanded = false;
                    });
                }
                _ui.Post(() => Messages.Add(_currentAssistant));
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
                _pendingTools[t.Id] = toolMsg;
                _ui.Post(() => Messages.Add(toolMsg));
                break;

            case AgentEvent.ToolCompleted t:
                if (_pendingTools.TryGetValue(t.Id, out var pending))
                {
                    var msg = pending;
                    _ui.Post(() =>
                    {
                        msg.Success = true;
                        msg.Summary = t.Summary;
                        msg.FullResult = t.FullResult;
                    });
                }
                break;

            case AgentEvent.UsageUpdated u:
                _ui.Post(() =>
                {
                    _totalTokensUsed += u.Total;
                    TokenUsage = $"本轮: {u.Total} tokens | 累计: {_totalTokensUsed}";
                });
                break;

            case AgentEvent.Retrying r:
                // 编排器重发同一轮请求会丢弃失败尝试已收到的部分数据，
                // 缓冲同步清空，防止重连后内容重复累积
                _pendingReply.Clear();
                _pendingThinking.Clear();
                _ui.Post(() =>
                {
                    Messages.Add(new ToolCallMessage
                    {
                        Name = "重试",
                        Summary = $"第 {r.Attempt}/{r.MaxAttempts} 次重试: {r.Reason}",
                        Success = false
                    });
                });
                break;

            case AgentEvent.Error e:
                DiagLog?.Invoke($"[AiAgent] {e.Message}");
                if (_currentAssistant is { } current)
                {
                    _ui.Post(() => current.Content += $"[错误] {e.Message}");
                }
                else
                {
                    // 无占位气泡时兜底为独立错误行，错误不允许静默丢弃
                    _ui.Post(() => Messages.Add(new ToolCallMessage { Name = "错误", Summary = e.Message, Success = false }));
                }
                break;

            case AgentEvent.DebugInfo d:
                // 原始 SSE 数据量大且仅调试面板可见（Release 版不渲染），
                // 只把解析失败等关键诊断同步写入日志文件，保证可追溯
                if (!d.Message.StartsWith("[SSE] ", StringComparison.Ordinal))
                    DiagLog?.Invoke($"[AiAgent] {d.Message}");
                _ui.Post(() =>
                {
                    _debugLogs.Add(d.Message);
                    while (_debugLogs.Count > 50)
                        _debugLogs.RemoveAt(0);
                    OnPropertyChanged(nameof(DebugLogText));
                    OnPropertyChanged(nameof(HasDebugLogs));
                });
                break;

            case AgentEvent.Completed c:
                HandleCompleted(c.FinalContent);
                _pendingTools.Clear();
                break;
        }
    }

    /// <summary>
    /// 会话结束：以编排器的 FinalContent 为最终内容真源——它包含编排器追加的
    /// [错误]/[连接中断]/[已达到工具调用最大轮次] 等标记。此前用 VM 本地缓冲覆盖
    /// Content 会把这些文本抹掉，气泡随之隐藏，表现为"出错却毫无提示"。
    /// 空回复同样必须可见，不允许气泡静默消失。
    /// </summary>
    private void HandleCompleted(string finalContent)
    {
        var trimmed = finalContent.TrimStart('\n', '\r');
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = "[错误] 模型返回了空回复";

        var completed = _currentAssistant;
        if (completed is null)
        {
            // 无占位气泡时兜底为独立行（如首轮 RoundStart 之前就结束）
            _ui.Post(() => Messages.Add(new ToolCallMessage { Name = "提示", Summary = trimmed, Success = false }));
            return;
        }

        // 内容与流式状态的写入统一封送到 UI 线程（事件可能来自编排器后台线程）；
        // Post 排在节流回调之后执行，最终以完整 FinalContent 覆盖部分内容
        _ui.Post(() =>
        {
            completed.Content = trimmed;
            if (_pendingThinking.Length > 0)
            {
                completed.Thinking ??= new ThinkingBlock();
                completed.Thinking.Text = _pendingThinking.ToString();
            }

            completed.IsStreaming = false;
            if (completed.Thinking is not null)
                completed.Thinking.IsExpanded = false;
        });
    }

    /// <summary>
    /// 节流式 UI 更新：将 StringBuilder 中的累积内容写入当前 AssistantMessage。
    /// 用于 ContentDelta / ThinkingDelta 等高频连续事件。
    /// </summary>
    private void ScheduleUiUpdate()
    {
        if (_uiUpdateScheduled) return;
        _uiUpdateScheduled = true;
        _ui.Post(() =>
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
        });
    }

    /// <summary>
    /// 异步生成对话标题，通过非流式请求让模型总结会话主题。
    /// 解析 JSON 格式响应 {"title":"..."} 并更新 SessionTitle。
    /// 失败时静默降级，不影响主对话流程。
    /// </summary>
    private async Task GenerateTitleAsync(string userMessage, ProviderConfig provider, string modelId, int sessionVersion)
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
                    if (!string.IsNullOrWhiteSpace(title) && sessionVersion == _sessionVersion)
                    {
                        var trimmed = title.Trim().Trim('"', '\'', '，', '。');
                        _ui.Post(() => SessionTitle = trimmed);
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
            if (provider.Api != "openai-completions")
            {
                // 非 openai 协议的供应商无法接入，记录原因而不是从模型列表无声消失
                DiagLog?.Invoke($"[AiAgent] 供应商 \"{providerKey}\" 的 API 类型 \"{provider.Api}\" 不受支持，已从模型列表忽略");
                continue;
            }

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

    private bool TryGetProviderConfig(string providerKey, out ProviderConfig provider)
    {
        _cachedConfig ??= ConfigManager.LoadModelsConfig();
        // models.json 可能被外部修改而未触发刷新事件，缺失时返回 false 由调用方给出提示
        return _cachedConfig.Models.Providers.TryGetValue(providerKey, out provider!);
    }

    /// <summary>默认 models.json 模板中的占位 API Key，命中说明用户还没填真实 Key。</summary>
    private const string PlaceholderApiKey = "sk-REPLACE_WITH_YOUR_KEY";

    /// <summary>
    /// 发送前校验供应商配置，返回用户可读的错误提示；配置可用时返回 null。
    /// 纯函数，便于单元测试。
    /// </summary>
    internal static string? ValidateProvider(string providerKey, ProviderConfig? provider)
    {
        if (provider is null)
            return $"供应商 \"{providerKey}\" 已不在 models.json 中，请重新选择模型或检查配置文件。";

        var label = string.IsNullOrWhiteSpace(provider.Name) ? providerKey : provider.Name;

        if (provider.Api != "openai-completions")
            return $"供应商 \"{label}\" 的 API 类型 \"{provider.Api}\" 不受支持，当前仅支持 openai-completions。";

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            return $"供应商 \"{label}\" 未配置 BaseUrl。";

        // 与 OpenAIChatClient 构造一致：拼接尾部斜杠后必须是绝对 http(s) 地址
        var baseUri = provider.BaseUrl.TrimEnd('/') + "/";
        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return $"供应商 \"{label}\" 的 BaseUrl \"{provider.BaseUrl}\" 不是有效的 http(s) 地址。";

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            return $"供应商 \"{label}\" 未配置 API Key。";

        if (provider.ApiKey.Trim() == PlaceholderApiKey)
            return $"供应商 \"{label}\" 仍在使用默认占位 API Key，请在模型配置中填入真实 Key。";

        return null;
    }

    /// <summary>
    /// 诊断日志汇点（GUI 启动时注入），替代散落的 Console.WriteLine——
    /// GUI 进程没有控制台，那些输出此前全部丢失。
    /// </summary>
    public static Action<string>? DiagLog { get; set; }

    /// <summary>
    /// 取消在途请求并退订静态事件。应用级单例（MainWindowViewModel）在主窗口
    /// 关闭时调用；设计时/测试等反复实例化的场景也必须调用，
    /// 否则订阅会随静态事件永久累积。
    /// </summary>
    public void Dispose()
    {
        // 关窗时不留后台 LLM 请求/编排任务继续跑完
        if (_cts is not null)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            _cts.Dispose();
            _cts = null;
        }

        ConfigManager.ModelsConfigChanged -= OnModelsConfigChanged;
        ConfigManager.McpConfigChanged -= OnMcpConfigChanged;

        if (_mcpManager is not null)
            _mcpManager.ToolsChanged -= OnMcpToolsChanged;
    }
}