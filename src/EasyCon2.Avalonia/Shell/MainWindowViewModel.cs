using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture;
using EasyCon.Capture.Audio;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon2.Avalonia.AiAgent;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.FileTree;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Mcp;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Monitoring;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.TagEditor;
using EasyCon2.Avalonia.Terminal;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.Shell;

public partial class MainWindowViewModel : ViewModelBase
{
    // 脚本文档四件套（路径/文本/脏标记/保存闸门）的状态机，语义见 IScriptDocumentService
    private readonly IScriptDocumentService _doc;

    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly IScriptService _scriptService;
    private readonly IControllerService _controllerService;
    private readonly IDialogService _dialogService;
    private readonly IWindowService _windowService;
    private readonly IUpdateService _updateService;
    private readonly IUserSettingsService _settings;
    private readonly IFileStorage _fileStorage;
    private readonly IFileSystem _fileSystem;
    private readonly ToolCallService _toolCallService;
    private readonly IImageProcessor? _imageProcessor;
    private readonly IAudioCaptureSource _audioCaptureSource;
    private readonly IMcpManager _mcpManager;
    private readonly AnsiParser _ansiParser = new();
    private MonitorViewModel? _monitorViewModel;
    private readonly FileTreeViewModel _fileTreeViewModel;
    private string? _projectDirectoryPath;

    /// <summary>日志环形缓冲，供 AI 工具读取近期运行日志。</summary>
    private const int LogBufferSize = 200;
    private readonly ConcurrentQueue<string> _logBuffer = new();

    /// <summary>UI 日志显示行数上限，超过后丢弃最旧的行，避免内存无限增长。</summary>
    private const int MaxLogLines = 10000;

    // 连接编排子 ViewModel（属性名保持与原绑定一致，见各子类）
    public SwitchConnectionViewModel Switch { get; }
    public CaptureConnectionViewModel Capture { get; }
    public AudioConnectionViewModel Audio { get; }
    public ControllerConnectionViewModel Controller { get; }
    public FlashFirmwareViewModel Firmware { get; }
    public WelcomeConsoleViewModel Welcome { get; }

    // 窗口标题（含版本号）
    [ObservableProperty]
    private string _windowTitle;

    public string BrandTitle { get; }

    public string VersionBadgeText { get; }

    // 当前版本号（用户配置页显示）
    [ObservableProperty]
    private string _currentVersion = "";

    // 是否正在检查更新
    [ObservableProperty]
    private bool _isCheckingUpdate;

    // 最近一次检查更新结果（null = 尚未检查过），供语言切换后按当前状态重算按钮文本
    private UpdateCheckResult? _lastUpdateCheckResult;

    // 完整版本号（供语言切换后重算 CurrentVersion 文本）
    private string _fullVersion = "";

    // 检查更新按钮文本
    [ObservableProperty]
    private string _checkUpdateButtonText = L10n.T("Text.Config.CheckUpdate");

    // 当前脚本路径（状态由 _doc 持有，代理属性保持 XAML/测试兼容；通知经 OnDocumentPropertyChanged 转发）
    public string CurrentScriptPath { get => _doc.CurrentPath; set => _doc.CurrentPath = value; }

    // 日志输出行集合（TerminalControl 绑定）
    public ObservableCollection<TerminalLine> LogLines { get; } = new();

    [ObservableProperty]
    private bool _isRecording = false;

    public bool IsStartRecordEnabled => !IsRecording;
    public bool IsStopRecordEnabled => IsRecording;
    // 运行脚本相关属性
    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _runButtonText = L10n.T("Text.Btn.Run");

    [ObservableProperty]
    private string _runTimeDisplay = "00:00:00";

    private System.Timers.Timer _runTimer = new System.Timers.Timer(500);
    private DateTime _runStartTime;

    // 监视器可见性
    [ObservableProperty]
    private bool _isMonitorVisible = true;

    // 监视器暂停状态
    [ObservableProperty]
    private bool _isMonitorPaused = false;

    // 监视器暂停按钮文本
    public string MonitorPauseButtonText => IsMonitorPaused ? L10n.T("Text.Btn.Resume") : L10n.T("Text.Btn.Pause");
    public string MonitorVisibilityButtonText => IsMonitorVisible ? L10n.T("Text.Btn.MonitorHide") : L10n.T("Text.Btn.MonitorShow");

    // 监视器 ViewModel（View 在 XAML 中声明）
    public MonitorViewModel? MonitorVM => _monitorViewModel;

    // 文件树 ViewModel（View 在 XAML 中声明）
    public FileTreeViewModel FileTreeVM => _fileTreeViewModel;

    // 编辑器标签页索引（0=文本编辑, 1=标签编辑, 2=用户配置, 3=功能中心）
    [ObservableProperty]
    private int _selectedEditorTab = 0;

    public bool IsTextEditorTabSelected => SelectedEditorTab == 0;
    public bool IsTagEditorTabSelected => SelectedEditorTab == 1;
    public bool IsUserConfigTabSelected => SelectedEditorTab == 2;
    public bool IsFeatureCenterTabSelected => SelectedEditorTab == 3;
    public bool IsCardEditorHeaderVisible => IsTextEditorTabSelected && !AiAgent.IsOpen;

    // 标签编辑器协调器：持有唯一的 TagEditorViewModel 实例，负责截图/标注测试/图片打开流程
    private TagEditorCoordinator? _tagCoordinator;

    // 标签编辑器 ViewModel（转发协调器持有的实例；构造完成后恒非空，实例终生不变）
    public TagEditorViewModel? TagEditorViewModel => _tagCoordinator?.ViewModel;

    public AiAgentViewModel AiAgent { get; }

    // 脚本路径显示文本（超30字符中间省略）
    public string ScriptDisplayPath
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentScriptPath) || CurrentScriptPath == ScriptPathSentinels.NoScriptPath)
                return ScriptPathSentinels.UntitledScript;
            if (CurrentScriptPath.Length <= 30)
                return CurrentScriptPath;
            var fileName = Path.GetFileName(CurrentScriptPath);
            var dir = Path.GetDirectoryName(CurrentScriptPath) ?? "";
            // 计算可容纳的前缀长度：30 - "...\" - fileName
            var available = 30 - fileName.Length - 4; // 4 = "...\"
            if (available > 3 && dir.Length > available)
                return dir[..available] + "..." + Path.DirectorySeparatorChar + fileName;
            return "..." + Path.DirectorySeparatorChar + fileName;
        }
    }

    public ICommand OpenScriptCommand { get; }

    [ObservableProperty]
    private bool _isAutoCompletionEnabled = false;

    [ObservableProperty]
    private bool _autoSwitchLayoutEnabled = false;

    [ObservableProperty]
    private bool _autoSwitchColorSchemeEnabled = false;

    private bool? _systemPrefersDarkColorScheme;

    public bool IsManualColorSchemeEnabled => !AutoSwitchColorSchemeEnabled;

    // 显示代码折叠
    [ObservableProperty]
    private bool _showFolding = true;

    [ObservableProperty]
    private bool _isHighResolutionTimingEnabled = false;

    [ObservableProperty]
    private string _welcomeText = ConfigState.DefaultWelcomeText;

    /// <summary>界面语言（与 Resources/Locales 下的字典键一致）。</summary>
    public string[] LanguageOptions { get; } = { "zh_CN", "en_US" };

    [ObservableProperty]
    private string _selectedLanguageCode = "zh_CN";

    partial void OnSelectedLanguageCodeChanged(string value)
    {
        App.SetLocale(value);
        RefreshLocalizedTexts();
        _settings.ScheduleSave(CollectUserSettings);
    }

    /// <summary>
    /// 语言切换后重算全部快照文本（L10n.T 在赋值时快照，不会随字典切换自动刷新）。
    /// 只换语言、不改变状态：按钮文案按当前状态（运行中/检查中/连接状态等）取对应译法。
    /// </summary>
    private void RefreshLocalizedTexts()
    {
        RunButtonText = IsRunning ? L10n.T("Text.Btn.Stop") : L10n.T("Text.Btn.Run");
        CheckUpdateButtonText = IsCheckingUpdate
            ? L10n.T("Text.Status.CheckingUpdate")
            : FormatUpdateCheckButtonText(_lastUpdateCheckResult);
        CurrentVersion = string.Format(L10n.T("Text.Config.CurrentVersion"), _fullVersion);
        OnPropertyChanged(nameof(MonitorPauseButtonText));
        OnPropertyChanged(nameof(MonitorVisibilityButtonText));
        Switch.RefreshLocalizedTexts();
        Capture.RefreshLocalizedTexts();
        Controller.RefreshLocalizedTexts();
    }

    /// <summary>按检查结果取按钮文案；null = 尚未检查过，显示初始文案。</summary>
    private string FormatUpdateCheckButtonText(UpdateCheckResult? r) => r == null
        ? L10n.T("Text.Config.CheckUpdate")
        : r.Status switch
        {
            UpdateStatus.UpdateAvailable => string.Format(L10n.T("Text.Status.UpdateAvailable"), r.LatestVersion),
            UpdateStatus.UpToDate => L10n.T("Text.Status.UpToDate"),
            _ => L10n.T("Text.Status.UpdateCheckFailed"),
        };

    [ObservableProperty]
    private bool _isIdleThreeColumnLayoutSelected = true;

    [ObservableProperty]
    private bool _isIdleTwoColumnLayoutSelected = false;

    [ObservableProperty]
    private bool _isRunningThreeColumnLayoutSelected = false;

    [ObservableProperty]
    private bool _isRunningTwoColumnLayoutSelected = true;

    [ObservableProperty]
    private bool _isRunningOneColumnLayoutSelected = false;

    // 远程控制模块属性（命令保留，UI已隐藏）
    public IAsyncRelayCommand SaveScriptCommand { get; }
    public IAsyncRelayCommand SaveScriptAsCommand { get; }
    public ICommand CloseScriptCommand { get; }
    public ICommand FormatScriptCommand { get; }
    public ICommand OpenEditorCommand { get; }
    public ICommand RunScriptCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand DropFileCommand { get; }
    public ICommand StartRecordCommand { get; }
    public ICommand StopRecordCommand { get; }
    public ICommand ShowMonitorCommand { get; }
    public ICommand OpenTagEditorCommand { get; }
    public ICommand OpenESPConfigCommand { get; }
    public ICommand OpenAlertConfigCommand { get; }
    public ICommand OpenModelsConfigCommand { get; }
    public ICommand OpenMcpConfigCommand { get; }
    public ICommand ToggleMonitorPauseCommand { get; }

    public ICommand ShowScriptSyntaxCommand { get; }
    public ICommand OpenAiAgentCommand { get; }

    public ICommand ToggleMonitorVisibilityCommand { get; }
    public ICommand SelectEditorTabCommand { get; }
    public ICommand SelectThemeStyleCommand { get; }
    public ICommand SelectColorSchemeCommand { get; }
    public ICommand RestoreDefaultLayoutCommand { get; }
    public ICommand ResetWelcomeTextCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }
    public IRelayCommand OpenGitHubCommand { get; }

    public MainWindowViewModel(ILogService logService, IDeviceService deviceService, ICaptureService captureService, IScriptService scriptService, IControllerService controllerService, IDialogService dialogService, IWindowService windowService, EasyCon2.Avalonia.Services.IUiDispatcher uiDispatcher, IImageProcessor? imageProcessor = null, EasyCon2.Avalonia.Services.IUpdateService? updateService = null, EasyCon2.Avalonia.Services.IUserSettingsService? userSettings = null, IFileStorage? fileStorage = null, IFileSystem? fileSystem = null, IAudioCaptureSource? audioSource = null)
    {
        _settings = userSettings ?? new UserSettingsService();
        // 脚本文档状态机（必须在任何读代理属性之前就位）；状态变化的对外通知统一经订阅块同步转发，
        // 复刻原 ObservableProperty 生成 setter + On*Changed partial 的全部通知语义（无 Dispatcher 转发）。
        _doc = new ScriptDocumentService(logService);
        _doc.PropertyChanged += OnDocumentPropertyChanged;
        // 初始化 AI Agent，注入编辑区服务
        _toolCallService = new ToolCallService(
            scriptService, captureService, deviceService, _logBuffer,
            () => _projectDirectoryPath,
            () => EditorText ?? string.Empty,
            v => EditorText = v,
            () => HasSelectedScriptPath() ? CurrentScriptPath : null,
            () => HasSelectedScriptPath(),
            () => new DeviceStatusInfo(
                Switch.IsNintendoSwitchConnected,
                Capture.IsCaptureSourceConnected,
                Controller.IsControllerConnected,
                scriptService.IsRunning)
        );
        _mcpManager = new McpManager(logService);
        // AI Agent 诊断路由到日志文件（替代丢失的 Console 输出；原为 AiAgentViewModel.DiagLog 静态汇点）
        AiAgent = new AiAgentViewModel(_toolCallService, _mcpManager, uiDispatcher,
            diagLog: message => logService.AddLog(message));
        _imageProcessor = imageProcessor;
        AiAgent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiAgent.IsOpen))
                OnPropertyChanged(nameof(IsCardEditorHeaderVisible));
        };

        // 窗口标题
        var fullVer = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var ver = fullVer;
        var plusIdx = ver.IndexOf('+');
        if (plusIdx > 0) ver = ver[..plusIdx];
        BrandTitle = "伊机控 EasyCon";
        VersionBadgeText = $"v{ver}";
        WindowTitle = $"{BrandTitle} {VersionBadgeText}";
        _fullVersion = fullVer;
        CurrentVersion = string.Format(L10n.T("Text.Config.CurrentVersion"), fullVer);

        _logService = logService;
        _deviceService = deviceService;
        _captureService = captureService;
        _scriptService = scriptService;
        _controllerService = controllerService;
        _dialogService = dialogService;
        _windowService = windowService;
        _updateService = updateService ?? new GiteeUpdateService();
        _fileStorage = fileStorage ?? new LocalFileStorage();
        _fileSystem = fileSystem ?? LocalFileSystem.Instance;
        _audioCaptureSource = audioSource ?? new WasapiAudioCapture();

        // 连接编排子 ViewModel（Switch 必须先于 Controller：后者依赖前者的连接状态）
        Switch = new SwitchConnectionViewModel(deviceService, logService, uiDispatcher);
        Capture = new CaptureConnectionViewModel(captureService, logService, uiDispatcher);
        Audio = new AudioConnectionViewModel(_audioCaptureSource, logService, uiDispatcher);
        Controller = new ControllerConnectionViewModel(controllerService, windowService, logService, () => Switch.IsNintendoSwitchConnected, uiDispatcher);
        Firmware = new FlashFirmwareViewModel(deviceService, scriptService, logService,
            () => EditorText,
            () => HasSelectedScriptPath() ? CurrentScriptPath : null);
        Welcome = new WelcomeConsoleViewModel(scriptService, ConfigState.DefaultWelcomeText, uiDispatcher);

        // 子 VM 与主 VM 的联动
        Switch.DeviceLost += () => IsRecording = false;
        Capture.CaptureConnected += ShowMonitor;
        Capture.CaptureTypeChanged += () => _settings.ScheduleSave(CollectUserSettings);
        Capture.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CaptureConnectionViewModel.IsCaptureSourceConnected) && TagEditorViewModel != null)
                TagEditorViewModel.IsCaptureConnected = Capture.IsCaptureSourceConnected;
        };

        // 初始化文件树
        _fileTreeViewModel = new FileTreeViewModel();
        _fileTreeViewModel.FileActivated += OnFileTreeFileActivated;
        _fileTreeViewModel.SortModeChanged += OnFileTreeSortModeChanged;
        _fileTreeViewModel.OpenProjectRequested += OnOpenProjectRequested;
        _fileTreeViewModel.NewScriptRequested += NewScript;
        _fileTreeViewModel.OpenScriptRequested += OnOpenScriptRequested;
        _fileTreeViewModel.OpenProjectFolderRequested += OnOpenProjectFolderRequested;
        _fileTreeViewModel.SaveScriptRequested += OnSaveScriptRequested;
        _fileTreeViewModel.SaveScriptAsRequested += OnSaveScriptAsRequested;
        _fileTreeViewModel.CloseProjectRequested += CloseProject;
        _fileTreeViewModel.FileOperationMessage += message => _logService.AddLog(message);

        // 初始化监视器 ViewModel
        InitializeMonitorViewModel();

        // 初始化标签编辑器（默认空实例，始终可用）
        InitializeTagEditor();

        // 订阅日志事件（LogService 已批量合并，此处每 100ms 最多触发一次）
        _logService.LogAppended += (text, color) =>
        {
            if (text == null)
            {
                LogLines.Clear();
                _logBuffer.Clear();
            }
            else
            {
                // 按换行拆分，每行生成一个 TerminalLine
                var rawLines = text.Split('\n');
                foreach (var rawLine in rawLines)
                {
                    if (string.IsNullOrEmpty(rawLine)) continue;
                    LogLines.Add(ParseLogLine(rawLine, color));

                    // 同步入环形缓冲，供 AI 工具读取
                    _logBuffer.Enqueue(rawLine);
                    while (_logBuffer.Count > LogBufferSize)
                        _logBuffer.TryDequeue(out _);
                }

                // 有界显示：丢弃最旧的日志行，防止内存无限增长
                if (LogLines.Count > MaxLogLines)
                {
                    int excess = LogLines.Count - MaxLogLines;
                    for (int i = 0; i < excess; i++)
                        LogLines.RemoveAt(0);
                }
            }
        };

        // 订阅脚本运行状态变化
        _scriptService.IsRunningChanged += running =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = running;
                RunButtonText = running ? L10n.T("Text.Btn.Stop") : L10n.T("Text.Btn.Run");
                Welcome.Refresh();

                if (running)
                {
                    _runStartTime = DateTime.Now;
                    _runTimer.Start();
                }
                else
                {
                    _runTimer.Stop();
                    RunTimeDisplay = "00:00:00";
                }
            });
        };

        // 初始化命令
        OpenScriptCommand = new AsyncRelayCommand(OpenScriptAsync);
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync, CanSaveScript);
        SaveScriptAsCommand = new AsyncRelayCommand(SaveScriptAsAsync, CanSaveScript);
        CloseScriptCommand = new RelayCommand(CloseScript);
        FormatScriptCommand = new AsyncRelayCommand(FormatScriptAsync);
        OpenEditorCommand = new RelayCommand(OpenEditor, CanOpenEditor);
        RunScriptCommand = new AsyncRelayCommand(RunScriptAsync);
        ClearLogCommand = new RelayCommand(ClearLog);
        DropFileCommand = new RelayCommand<string>(DropFile);
        StartRecordCommand = new RelayCommand(StartRecord);
        StopRecordCommand = new RelayCommand(StopRecord);
        ShowMonitorCommand = new RelayCommand(ShowMonitor);
        OpenTagEditorCommand = new RelayCommand(OpenTagEditor);
        OpenESPConfigCommand = new RelayCommand(OpenESPConfig);
        OpenAlertConfigCommand = new RelayCommand(OpenAlertConfig);
        OpenModelsConfigCommand = new RelayCommand(OpenModelsConfig);
        OpenMcpConfigCommand = new RelayCommand(OpenMcpConfig);
        ToggleMonitorPauseCommand = new RelayCommand(ToggleMonitorPause);
        ShowScriptSyntaxCommand = new RelayCommand(ShowScriptSyntax);
        OpenAiAgentCommand = new RelayCommand(OpenAiAgent);
        ToggleMonitorVisibilityCommand = new RelayCommand(ToggleMonitorVisibility);
        SelectEditorTabCommand = new RelayCommand<string>(SelectEditorTab);
        SelectThemeStyleCommand = new RelayCommand<string>(SelectThemeStyle);
        SelectColorSchemeCommand = new RelayCommand<string>(SelectColorScheme);
        RestoreDefaultLayoutCommand = new RelayCommand(RestoreDefaultLayout);
        ResetWelcomeTextCommand = new RelayCommand(ResetWelcomeText);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync);
        OpenGitHubCommand = new RelayCommand(OpenGitHub);

        _settings.Load(ApplyUserSettings);
        InitializeSampleData();

        _runTimer.Elapsed += (s, e) =>
        {
            var elapsed = DateTime.Now - _runStartTime;
            Dispatcher.UIThread.Post(() =>
            {
                RunTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");
            });
        };

    }
    private TerminalLine ParseLogLine(string rawLine, string? color)
    {
        // 仅含 ANSI 转义序列的行才需要解析器；纯文本行直接构造单段，避免 StringBuilder + 拷贝开销。
        if (rawLine.Contains('\x1b'))
            return _ansiParser.ParseLine(rawLine);

        var foreground = TryParseLogColor(color);
        var line = new TerminalLine();
        line.Segments.Add(new TextSegment(rawLine, foreground));
        return line;
    }

    private static RgbColor? TryParseLogColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        return color.Trim() switch
        {
            "Lime" => new RgbColor(0x32, 0xD7, 0x4B),
            "Orange" => new RgbColor(0xF5, 0x9E, 0x0B),
            "OrangeRed" => new RgbColor(0xFF, 0x5A, 0x3D),
            "Red" => new RgbColor(0xEF, 0x44, 0x44),
            "Yellow" => new RgbColor(0xF4, 0xD0, 0x3F),
            "Green" => new RgbColor(0x22, 0xC5, 0x5E),
            "Cyan" => new RgbColor(0x22, 0xD3, 0xEE),
            "Blue" => new RgbColor(0x60, 0xA5, 0xFA),
            "Magenta" => new RgbColor(0xE8, 0x79, 0xF9),
            _ => TryParseHexColor(color)
        };
    }

    /// <summary>解析 #RGB / #RRGGBB / #AARRGGBB 十六进制颜色，其余格式返回 null。</summary>
    private static RgbColor? TryParseHexColor(string color)
    {
        var s = color.Trim();
        if (s.Length == 0 || s[0] != '#') return null;

        try
        {
            byte Hex(int i) => Convert.ToByte(s.Substring(i, 2), 16);
            return s.Length switch
            {
                9 => new RgbColor(Hex(3), Hex(5), Hex(7)), // #AARRGGBB，忽略 alpha
                7 => new RgbColor(Hex(1), Hex(3), Hex(5)),
                4 => new RgbColor(
                    Convert.ToByte(new string(s[1], 2), 16),
                    Convert.ToByte(new string(s[2], 2), 16),
                    Convert.ToByte(new string(s[3], 2), 16)),
                _ => null
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }
    /// <summary>启动时应用用户配置到 VM 属性（apply 方向），由 _settings.Load 调用（IsLoading 保护期内）。</summary>
    private void ApplyUserSettings(ConfigState cfg)
    {
        _fileTreeViewModel.SelectSortModeCommand.Execute(
            FileTreeSortModeSettings.FromSettingValue(cfg.FileTreeSortMode));

        Capture.SelectedCaptureType = UserSettingsService.NormalizeCaptureType(cfg.CaptureType);
        IsAutoCompletionEnabled = cfg.EnableAutoCompletion;
        ShowFolding = cfg.ShowFolding;
        IsHighResolutionTimingEnabled = cfg.HighResolutionTiming;
        _scriptService.HighResolutionTiming = IsHighResolutionTimingEnabled;
        ShowDebugInfo = cfg.ShowDebugInfo;
        var language = !string.IsNullOrEmpty(cfg.LanguageCode) && LanguageOptions.Contains(cfg.LanguageCode)
            ? cfg.LanguageCode
            : "zh_CN";
        SelectedLanguageCode = language;
        App.SetLocale(language);
        RefreshLocalizedTexts();
        WelcomeText = cfg.WelcomeText ?? ConfigState.DefaultWelcomeText;
        AutoSwitchLayoutEnabled = cfg.AutoSwitchLayoutEnabled;
        AutoSwitchColorSchemeEnabled = cfg.AutoSwitchColorSchemeEnabled;
        var idleTwoColumn = cfg.IsIdleTwoColumnLayoutSelected;
        IsIdleThreeColumnLayoutSelected = !idleTwoColumn;
        IsIdleTwoColumnLayoutSelected = idleTwoColumn;

        var runningOneColumn = cfg.IsRunningOneColumnLayoutSelected;
        var runningThreeColumn = cfg.IsRunningThreeColumnLayoutSelected && !runningOneColumn;
        IsRunningThreeColumnLayoutSelected = runningThreeColumn;
        IsRunningTwoColumnLayoutSelected = !runningThreeColumn && !runningOneColumn;
        IsRunningOneColumnLayoutSelected = runningOneColumn;

        var colorSchemeName = UserSettingsService.NormalizeColorSchemeName(ThemeKeys.Restore(cfg.ColorSchemeName), cfg.DarkMode);
        var themeStyleName = UserSettingsService.NormalizeThemeStyleName(ThemeKeys.Restore(cfg.ThemeStyleName), colorSchemeName);
        ThemeManager.Instance.ApplyColorScheme(colorSchemeName);
        ThemeManager.Instance.ApplyThemeStyle(themeStyleName);
    }

    /// <summary>把 VM 属性收集回 ConfigState（collect 方向），写盘由 _settings 负责。</summary>
    private void CollectUserSettings(ConfigState cfg)
    {
        cfg.CaptureType = Capture.SelectedCaptureType ?? "ANY";
        cfg.EnableAutoCompletion = IsAutoCompletionEnabled;
        cfg.ShowFolding = ShowFolding;
        cfg.HighResolutionTiming = IsHighResolutionTimingEnabled;
        cfg.ShowDebugInfo = ShowDebugInfo;
        cfg.WelcomeText = WelcomeText ?? string.Empty;
        cfg.LanguageCode = SelectedLanguageCode;
        cfg.FileTreeSortMode = FileTreeSortModeSettings.ToSettingValue(_fileTreeViewModel.SortMode);
        cfg.AutoSwitchLayoutEnabled = AutoSwitchLayoutEnabled;
        cfg.AutoSwitchColorSchemeEnabled = AutoSwitchColorSchemeEnabled;
        cfg.IsIdleThreeColumnLayoutSelected = IsIdleThreeColumnLayoutSelected;
        cfg.IsIdleTwoColumnLayoutSelected = IsIdleTwoColumnLayoutSelected;
        cfg.IsRunningThreeColumnLayoutSelected = IsRunningThreeColumnLayoutSelected;
        cfg.IsRunningTwoColumnLayoutSelected = IsRunningTwoColumnLayoutSelected;
        cfg.IsRunningOneColumnLayoutSelected = IsRunningOneColumnLayoutSelected;
        cfg.ColorSchemeName = ThemeKeys.Persist(ThemeManager.Instance.SelectedColorSchemeName);
        cfg.ThemeStyleName = ThemeKeys.Persist(ThemeManager.Instance.SelectedThemeStyleName);
        cfg.DarkMode = ThemeManager.Instance.IsDarkMode;
    }

    private void OnOpenProjectRequested()
    {
        _ = OpenProjectFolderAsync();
    }

    private void SelectColorScheme(string? colorSchemeName)
    {
        if (AutoSwitchColorSchemeEnabled || string.IsNullOrWhiteSpace(colorSchemeName))
            return;

        ThemeManager.Instance.ApplyColorScheme(colorSchemeName);
        _settings.ScheduleSave(CollectUserSettings);
        _logService.AddLog($"已切换外观: {colorSchemeName}");
    }

    private void SelectThemeStyle(string? themeStyleName)
    {
        if (string.IsNullOrWhiteSpace(themeStyleName))
            return;

        ThemeManager.Instance.ApplyThemeStyle(themeStyleName);
        _settings.ScheduleSave(CollectUserSettings);
        _logService.AddLog($"已切换风格: {themeStyleName}");
    }

    public void UpdateSystemColorScheme(bool prefersDark)
    {
        _systemPrefersDarkColorScheme = prefersDark;

        if (AutoSwitchColorSchemeEnabled)
            ApplySystemColorScheme();
    }

    private void ApplySystemColorScheme()
    {
        if (_systemPrefersDarkColorScheme is not { } prefersDark)
            return;

        var colorSchemeName = prefersDark
            ? ThemeManager.DarkModeSchemeName
            : ThemeManager.whiteGraySchemeName;

        ThemeManager.Instance.ApplyColorScheme(colorSchemeName, followSystemThemeVariant: true);
        _settings.ScheduleSave(CollectUserSettings);
    }

    private void SelectEditorTab(string? tabIndexText)
    {
        if (!int.TryParse(tabIndexText, out var tabIndex))
            return;

        SelectedEditorTab = Math.Clamp(tabIndex, 0, 3);
    }

    private void ResetWelcomeText()
    {
        WelcomeText = ConfigState.DefaultWelcomeText;
    }

    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        CheckUpdateButtonText = L10n.T("Text.Status.CheckingUpdate");
        try
        {
            var r = await _updateService.CheckAsync();
            _lastUpdateCheckResult = r;
            CheckUpdateButtonText = FormatUpdateCheckButtonText(r);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/EasyConNS/EasyCon") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开浏览器失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 由 MainWindow 调用：用户选择了项目目录后加载文件树。
    /// </summary>
    public void OpenProjectFromDirectory(string directoryPath)
    {
        _projectDirectoryPath = directoryPath;
        _fileTreeViewModel.LoadDirectory(directoryPath);
        _logService.AddLog($"已打开项目: {directoryPath}");
    }

    /// <summary>
    /// 获取当前项目目录路径（供 MainWindow 调用）。
    /// </summary>
    public string? GetCurrentProjectDirectory() => _projectDirectoryPath;

    private void OnFileTreeFileActivated(string filePath)
    {
        // 双击文件时不更新目录，只在右侧标签页打开内容
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".txt":
            case ".ecs":
            case ".md":
            case ".py":
                CurrentScriptPath = filePath;
                SelectedEditorTab = 0;
                InitializeEmbeddedEditor(filePath);
                break;
            case ".il":
                SelectedEditorTab = 1;
                _tagCoordinator?.LoadLabelFile(filePath);
                break;
            default:
                _logService.AddLog($"暂不支持打开该文件类型: {ext}");
                break;
        }
    }

    private void InitializeMonitorViewModel()
    {
        try
        {
            _monitorViewModel = new MonitorViewModel(_captureService);
            IsMonitorVisible = true;
            OnPropertyChanged(nameof(MonitorVM));
        }
        catch (Exception ex)
        {
            _logService.AddLog($"初始化监视器失败: {ex.Message}");
        }
    }

    private void InitializeTagEditor()
    {
        _tagCoordinator = new TagEditorCoordinator(_captureService, _dialogService, _logService, _imageProcessor);
        _tagCoordinator.Bind(_tagCoordinator.ViewModel);
    }

    private void InitializeSampleData()
    {
        // 串口/视频源/控制源列表已由各连接子 ViewModel 在构造时刷新
        _logService.AddLog($"已加载 {Switch.SerialPortOptions.Count} 个可用串口, {Capture.CaptureSourceOptions.Count} 个视频源, {Audio.AudioSourceOptions.Count} 个音频源");
    }
    private async Task OpenScriptAsync()
    {
        IReadOnlyList<string> files;
        try
        {
            files = await _dialogService.OpenFilesAsync("打开脚本文件",
            [
                new FileDialogFilter("ECS脚本文件", ["*.ecs"]),
                new FileDialogFilter("文本文件", ["*.txt"]),
                new FileDialogFilter("所有文件", ["*"])
            ]);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开文件对话框失败: {ex.Message}");
            return;
        }

        if (files.Count > 0)
            OpenScriptFromPath(files[0]);
    }

    private void OnOpenScriptRequested()
    {
        _ = OpenScriptAsync();
    }

    private void OnOpenProjectFolderRequested()
    {
        _ = OpenProjectFolderAsync();
    }

    private void OnSaveScriptRequested()
    {
        ExecuteSaveCommand(SaveScriptCommand);
    }

    private void OnSaveScriptAsRequested()
    {
        ExecuteSaveCommand(SaveScriptAsCommand);
    }

    private static void ExecuteSaveCommand(IAsyncRelayCommand command)
    {
        if (command.CanExecute(null))
            _ = command.ExecuteAsync(null);
    }

    private async Task OpenProjectFolderAsync()
    {
        var startPath = _projectDirectoryPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string? folder;
        try
        {
            folder = await _dialogService.OpenFolderAsync("打开项目目录", startPath);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开项目目录失败: {ex.Message}");
            return;
        }

        if (folder != null)
            OpenProjectFromDirectory(folder);
    }

    private async Task SaveScriptAsync()
    {
        await SaveScriptCoreAsync(saveAs: false);
    }

    private async Task SaveScriptAsAsync()
    {
        await SaveScriptCoreAsync(saveAs: true);
    }

    private async Task SaveScriptCoreAsync(bool saveAs)
    {
        if (IsSavingScript || !CanSaveScript())
            return;

        if (!_doc.TryBeginSave())
            return;
        try
        {
            bool hadSelectedScriptPath = HasSelectedScriptPath();
            bool needsSaveDialog = saveAs || _doc.NeedsSaveDialog;
            string targetPath;
            if (needsSaveDialog)
            {
                string suggestedName = HasSelectedScriptPath()
                    ? Path.GetFileName(CurrentScriptPath)
                    : $"{ScriptPathSentinels.UntitledScript}.ecs";
                string? selectedPath;
                try
                {
                    selectedPath = await _dialogService.SaveFileAsync("另存为", "ecs",
                    [
                        new FileDialogFilter("ECS脚本文件", ["*.ecs"]),
                        new FileDialogFilter("文本文件", ["*.txt"]),
                        new FileDialogFilter("所有文件", ["*"])
                    ], suggestedName);
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"保存对话框失败: {ex.Message}");
                    return;
                }

                if (selectedPath == null)
                    return;

                targetPath = selectedPath;
            }
            else
            {
                targetPath = CurrentScriptPath;
            }

            if (!_doc.WriteToFile(targetPath))
                return;

            CurrentScriptPath = targetPath;
            IsScriptModified = false;
            _logService.AddLog($"已保存脚本: {targetPath}");

            if (!string.IsNullOrWhiteSpace(_projectDirectoryPath))
            {
                if (IsPathInsideDirectory(targetPath, _projectDirectoryPath, _fileSystem))
                    _fileTreeViewModel.NotifyFileSaved(targetPath);
            }
            else if (saveAs || !hadSelectedScriptPath)
            {
                string? directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    _projectDirectoryPath = directory;
                    await _fileTreeViewModel.LoadDirectoryAsync(directory);
                }
            }
        }
        finally
        {
            _doc.EndSave();
        }
    }

    private bool CanSaveScript()
    {
        return !IsSavingScript
            && IsTextEditorTabSelected
            && (CurrentScriptPath == ScriptPathSentinels.UntitledScript || HasSelectedScriptPath() || IsScriptModified);
    }

    private void NotifySaveCommandCanExecuteChanged()
    {
        SaveScriptCommand?.NotifyCanExecuteChanged();
        SaveScriptAsCommand?.NotifyCanExecuteChanged();
    }

    private void OnFileTreeSortModeChanged()
    {
        _settings.ScheduleSave(CollectUserSettings);
    }

    private void CloseScript()
    {
        _doc.Close();
        SelectedEditorTab = 0;
    }

    private void NewScript()
    {
        _doc.NewUntitled();
        SelectedEditorTab = 0;
        _logService.AddLog("已新建脚本");
    }

    private void CloseProject()
    {
        _projectDirectoryPath = null;
        _fileTreeViewModel.LoadDirectory(null);
        _logService.AddLog("已关闭项目");
    }

    private bool HasSelectedScriptPath() => _doc.HasSelectedPath;

    /// <summary>
    /// 从路径打开脚本，加载文件树并初始化内嵌编辑器。
    /// </summary>
    public void OpenScriptFromPath(string path)
    {
        // 状态段迁移至文档服务：设路径 + 静默载文本 + 清脏（顺序同原实现）
        var fileText = File.Exists(path) ? _fileStorage.ReadAllText(path) : string.Empty;
        _doc.OpenFromPath(path, fileText);
        SelectedEditorTab = 0;

        // 更新文件树到脚本所在目录
        var dir = Path.GetDirectoryName(path);
        _projectDirectoryPath = dir;
        _fileTreeViewModel.LoadDirectory(dir);

        // 初始化内嵌编辑器（加载文件内容不属于用户修改）。编辑器 Clear/Load 会经 TwoWay
        // 绑定回写文本并瞬时置脏，行尾清脏等价于原实现的 IsScriptModified = false。
        InitializeEmbeddedEditor(path);
        _doc.IsModified = false;
    }

    /// <summary>
    /// 请求主窗口初始化内嵌编辑器。由 MainWindow 调用。
    /// </summary>
    public event Action<string>? EmbeddedEditorInitializeRequested;

    /// <summary>
    /// 当前编辑区文本（TwoWay 绑定到 ScriptEditorControl.EditorText）。状态由 _doc 持有。
    /// </summary>
    public string EditorText { get => _doc.Text; set => _doc.Text = value; }

    /// <summary>
    /// 当前脚本内容是否有未保存的修改。状态由 _doc 持有。
    /// </summary>
    public bool IsScriptModified { get => _doc.IsModified; set => _doc.IsModified = value; }

    /// <summary>保存闸门代理：置 true 走幂等 TryBeginSave，置 false 走 EndSave。</summary>
    public bool IsSavingScript { get => _doc.IsSaving; set { if (value) _doc.TryBeginSave(); else _doc.EndSave(); } }

    /// <summary>
    /// 脚本文档服务状态变化 → VM 对外通知（同步执行，无 Dispatcher 转发）。
    /// 逐条复刻被删除的 partial 回调语义；四个属性原本均无 [NotifyCanExecuteChangedFor] 特性，
    /// 命令通知全部来自 partial（SaveScript/SaveScriptAs 经 NotifySaveCommandCanExecuteChanged，
    /// OpenEditor 仅在 CurrentPath 变化时通知）。
    /// </summary>
    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IScriptDocumentService.Text):
                // 原 OnEditorTextChanged：脏标记已由服务内置；文本变化仍需通知绑定与命令重估
                OnPropertyChanged(nameof(EditorText));
                NotifySaveCommandCanExecuteChanged();
                break;
            case nameof(IScriptDocumentService.IsModified):
                // 原 OnIsScriptModifiedChanged
                OnPropertyChanged(nameof(IsScriptModified));
                OnPropertyChanged(nameof(ScriptDisplayPath));
                NotifySaveCommandCanExecuteChanged();
                break;
            case nameof(IScriptDocumentService.IsSaving):
                // 原 OnIsSavingScriptChanged
                OnPropertyChanged(nameof(IsSavingScript));
                NotifySaveCommandCanExecuteChanged();
                break;
            case nameof(IScriptDocumentService.CurrentPath):
                // 原 OnCurrentScriptPathChanged
                OnPropertyChanged(nameof(CurrentScriptPath));
                (OpenEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ScriptDisplayPath));
                NotifySaveCommandCanExecuteChanged();
                break;
        }
    }

    /// <summary>
    /// 格式化当前脚本（委托给 ToolCallService，供 FormatScriptCommand 绑定）。
    /// </summary>
    private Task<string> FormatScriptAsync() => _toolCallService.FormatScriptAsync();

    /// <summary>
    /// 请求主窗口弹出打开项目目录对话框。
    /// </summary>

    /// <summary>
    /// 请求主窗口切换代码折叠显示状态。
    /// </summary>
    public event Action<bool>? FoldingVisibilityChanged;

    private void InitializeEmbeddedEditor(string filePath)
    {
        EmbeddedEditorInitializeRequested?.Invoke(filePath);
    }
    private void ShowMonitor()
    {
        try
        {
            if (_monitorViewModel == null)
            {
                _monitorViewModel = new MonitorViewModel(_captureService);
                OnPropertyChanged(nameof(MonitorVM));
            }

            IsMonitorVisible = true;
            _monitorViewModel.StartMonitoring();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"显示监视器失败: {ex.Message}");
        }
    }

    private bool CanOpenEditor()
    {
        return HasSelectedScriptPath();
    }

    private void OpenEditor()
    {
        if (!CanOpenEditor()) return;
        SelectedEditorTab = 0;
        InitializeEmbeddedEditor(CurrentScriptPath);
    }

    private void OpenTagEditor()
    {
        // 切换到标签编辑标签页
        SelectedEditorTab = 1;
    }

    private void ToggleMonitorPause()
    {
        IsMonitorPaused = !IsMonitorPaused;
    }

    private void OpenESPConfig()
    {
        _windowService.Show(WindowKeys.EspConfig);
    }

    private void OpenAlertConfig()
    {
        _windowService.Show(WindowKeys.AlertConfig);
    }

    private void OpenModelsConfig()
    {
        _windowService.Show(WindowKeys.ModelsConfig);
    }

    private void OpenMcpConfig()
    {
        _windowService.Show(WindowKeys.McpConfig);
    }

    private void ToggleMonitorVisibility()
    {
        IsMonitorVisible = !IsMonitorVisible;
        if (_monitorViewModel == null) return;

        if (IsMonitorVisible)
            _monitorViewModel.StartMonitoring();
        else
            _monitorViewModel.StopMonitoring();
    }

    private void RestoreDefaultLayout()
    {
        IsIdleThreeColumnLayoutSelected = true;
        IsIdleTwoColumnLayoutSelected = false;
        IsRunningThreeColumnLayoutSelected = false;
        IsRunningTwoColumnLayoutSelected = true;
        IsRunningOneColumnLayoutSelected = false;
    }

    partial void OnIsIdleThreeColumnLayoutSelectedChanged(bool value)
    {
        if (value)
            IsIdleTwoColumnLayoutSelected = false;
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnIsIdleTwoColumnLayoutSelectedChanged(bool value)
    {
        if (value)
            IsIdleThreeColumnLayoutSelected = false;
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnIsRunningThreeColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningTwoColumnLayoutSelected = false;
            IsRunningOneColumnLayoutSelected = false;
        }
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnIsRunningTwoColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningThreeColumnLayoutSelected = false;
            IsRunningOneColumnLayoutSelected = false;
        }
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnIsRunningOneColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningThreeColumnLayoutSelected = false;
            IsRunningTwoColumnLayoutSelected = false;
        }
        _settings.ScheduleSave(CollectUserSettings);
    }

    private void ShowScriptSyntax()
    {
        _windowService.Show(WindowKeys.ScriptSyntax);
    }

    private void OpenAiAgent()
    {
        AiAgent.IsOpen = true;
    }

    private string? GetCurrentScriptRootDirectory()
    {
        var currentPath = CurrentScriptPath;
        // 用 HasSelectedScriptPath 语义判定（原实现硬编码中文哨兵 "未选择脚本"，英文界面下失效）
        if (HasSelectedScriptPath() && File.Exists(currentPath))
        {
            if (IsPathInsideDirectory(currentPath, _projectDirectoryPath, _fileSystem))
                return _projectDirectoryPath;

            return Path.GetDirectoryName(currentPath);
        }

        return _projectDirectoryPath;
    }

    private static bool IsPathInsideDirectory(string filePath, string? directoryPath, IFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !fileSystem.DirectoryExists(directoryPath))
            return false;

        try
        {
            var relativePath = Path.GetRelativePath(directoryPath, filePath);
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return relativePath == "."
                || (relativePath != ".."
                    && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", pathComparison)
                    && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", pathComparison)
                    && !Path.IsPathRooted(relativePath));
        }
        catch
        {
            return false;
        }
    }

    partial void OnIsMonitorPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitorPauseButtonText));
        if (_monitorViewModel == null) return;
        if (value)
            _monitorViewModel.StopMonitoring();
        else
            _monitorViewModel.StartMonitoring();
    }

    partial void OnIsMonitorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitorVisibilityButtonText));
    }
    partial void OnIsAutoCompletionEnabledChanged(bool value)
    {
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnAutoSwitchLayoutEnabledChanged(bool value)
    {
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnAutoSwitchColorSchemeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualColorSchemeEnabled));

        if (value)
            ApplySystemColorScheme();

        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnShowFoldingChanged(bool value)
    {
        FoldingVisibilityChanged?.Invoke(value);
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnIsHighResolutionTimingEnabledChanged(bool value)
    {
        _scriptService.HighResolutionTiming = value;
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnWelcomeTextChanged(string value)
    {
        Welcome.SetWelcomeText(value);
        _settings.ScheduleSave(CollectUserSettings);
    }

    partial void OnSelectedEditorTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsTextEditorTabSelected));
        OnPropertyChanged(nameof(IsTagEditorTabSelected));
        OnPropertyChanged(nameof(IsUserConfigTabSelected));
        OnPropertyChanged(nameof(IsFeatureCenterTabSelected));
        OnPropertyChanged(nameof(IsCardEditorHeaderVisible));
        NotifySaveCommandCanExecuteChanged();
    }


    private void DropFile(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            OpenScriptFromPath(path);
    }

    [ObservableProperty]
    private bool _showDebugInfo;

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStartRecordEnabled));
        OnPropertyChanged(nameof(IsStopRecordEnabled));
    }

    partial void OnShowDebugInfoChanged(bool value)
    {
        _deviceService.ShowDebugInfo = value;
        _settings.ScheduleSave(CollectUserSettings);
    }

    private async Task RunScriptAsync()
    {
        try
        {
            if (_scriptService.IsRunning)
            {
                _scriptService.Stop();
                return;
            }

            string[]? args = null;
            if (HasArgsShebang(EditorText))
            {
                args = await _dialogService.ShowScriptArgsDialogAsync();
                if (args == null) return; // 用户取消
            }

            string? fileName = HasSelectedScriptPath() ? CurrentScriptPath : null;
            _scriptService.RunFromContent(EditorText, args, fileName);
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10n.T("Text.Msg.RunScriptFail"), ex.Message));
        }
    }

    private static bool HasArgsShebang(string script)
    {
        var firstLine = script.Split('\n', '\r').FirstOrDefault()?.Trim();
        return firstLine != null && firstLine.StartsWith("#! args");
    }

    private void ClearLog()
    {
        _logService.Clear();
    }

    // 公共方法用于添加日志
    public void AddLog(string message)
    {
        _logService.AddLog(message);
    }

    /// <summary>
    /// 关窗前确认放弃未保存的脚本修改。返回 true 表示放弃并继续关闭。
    /// </summary>
    public async Task<bool> ConfirmCloseWithoutSavingAsync()
    {
        try
        {
            return await _dialogService.ConfirmAsync("未保存的修改",
                "当前脚本有未保存的修改，确定不保存直接退出吗？");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"确认对话框失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 主窗口关闭时调用，关闭所有子窗口和监视器。
    /// </summary>
    public void OnMainWindowClosing()
    {
        Welcome.Stop();

        // 解除标签编辑器协调器的事件订阅
        _tagCoordinator?.Dispose();

        // 立即写入防抖中挂起的用户配置
        _settings.Flush(CollectUserSettings);

        // 关闭嵌入式监视器
        if (_monitorViewModel != null)
        {
            _monitorViewModel.Close();
            _monitorViewModel = null;
        }

        Audio.Dispose();

        // 停止仍在运行的脚本（复位按键、丢弃排队 HID 报文）
        if (_scriptService.IsRunning)
        {
            _scriptService.Stop();
        }

        // 主窗口一关 vpad 必须退出（覆盖层 + SDL 键盘钩子），不能等 desktop.Exit：
        // 任何残留窗口拖住 lifetime 都会让 vpad 挂着（ac7e16c 回归）。Disconnect 幂等；
        // Dispose（SDL3 事件循环等）仍留在 App desktop.Exit 单一清理入口。
        _controllerService.Disconnect();

        // 取消在途 AI 请求并退订其静态事件订阅
        AiAgent.Dispose();

        // 释放 MCP 连接（终止子进程）。DisposeAsync 内含最长数秒的进程等待，
        // 放到后台执行，避免拖住窗口关闭；异常仅记日志（进程即将退出）。
        _ = Task.Run(async () =>
        {
            try
            {
                await _mcpManager.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logService.AddLog($"MCP 清理失败: {ex.Message}");
            }
        });
    }
    private void StartRecord()
    {
        if (IsRecording)
        {
            _logService.AddLog("脚本录制已在进行中");
            return;
        }

        if (!Switch.IsNintendoSwitchConnected)
        {
            _logService.AddLog(L10n.T("Text.Msg.ConnectFirst"));
            return;
        }

        if (!Controller.IsControllerConnected)
        {
            _logService.AddLog("请先连接虚拟手柄");
            return;
        }

        _logService.AddLog(L10n.T("Text.Msg.RecordStart"));
        try
        {
            var device = _deviceService.GetDevice();
            device.StartRecord();
            IsRecording = true;
        }
        catch (Exception ex)
        {
            _logService.AddLog($"开始录制失败: {ex.Message}");
        }
    }

    private void StopRecord()
    {
        if (!Switch.IsNintendoSwitchConnected)
        {
            _logService.AddLog(L10n.T("Text.Msg.ConnectFirst"));
            return;
        }

        if (!IsRecording)
        {
            _logService.AddLog("当前没有正在录制的脚本");
            return;
        }

        var device = _deviceService.GetDevice();
        try
        {
            device.StopRecord();
            IsRecording = false;
            var script = device.GetRecordScript();
            if (!string.IsNullOrEmpty(script))
            {
                EditorText = script;
                _logService.AddLog("录制完成，脚本已加载到编辑器");
            }
            else
            {
                _logService.AddLog("录制完成，但没有生成脚本内容");
            }
        }
        catch (Exception ex)
        {
            // StopRecord 失败时保持 IsRecording=true，允许用户重试停止
            _logService.AddLog($"停止录制失败: {ex.Message}");
        }
    }
}
