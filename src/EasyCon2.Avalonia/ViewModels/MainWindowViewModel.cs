using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.AiAgent;
using EasyCon2.Avalonia.Core.FileTree;
using EasyCon2.Avalonia.Core.Mcp;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Core.TagEditor;
using EasyCon2.Avalonia.Core.Terminal;
using EasyCon2.Avalonia.Core.ViewModels;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Views;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string NoScriptPathText = L10n.T("Text.Status.NoScriptSelected");
    private static readonly string UntitledScriptText = L10n.T("Text.Status.UntitledScript");

    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly IScriptService _scriptService;
    private readonly IControllerService _controllerService;
    private readonly IDialogService _dialogService;
    private readonly IWindowService _windowService;
    private readonly Func<ConfigState> _loadUserConfig;
    private readonly Action<ConfigState> _saveUserConfig;
    private readonly ToolCallService _toolCallService;
    private readonly IImageProcessor? _imageProcessor;
    private readonly IMcpManager _mcpManager;
    private readonly AnsiParser _ansiParser = new();
    private MonitorViewModel? _monitorViewModel;
    private readonly FileTreeViewModel _fileTreeViewModel;
    private string? _projectDirectoryPath;
    private ConfigState _userConfig = new();
    private bool _isLoadingUserSettings;

    /// <summary>日志环形缓冲，供 AI 工具读取近期运行日志。</summary>
    private const int LogBufferSize = 200;
    private readonly ConcurrentQueue<string> _logBuffer = new();

    /// <summary>UI 日志显示行数上限，超过后丢弃最旧的行，避免内存无限增长。</summary>
    private const int MaxLogLines = 10000;

    // 连接编排子 ViewModel（属性名保持与原绑定一致，见各子类）
    public SwitchConnectionViewModel Switch { get; }
    public CaptureConnectionViewModel Capture { get; }
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

    // 检查更新按钮文本
    [ObservableProperty]
    private string _checkUpdateButtonText = "检查更新";

    // 当前脚本路径
    [ObservableProperty]
    private string _currentScriptPath = NoScriptPathText;

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

    // 标签编辑器 ViewModel
    [ObservableProperty]
    private TagEditorViewModel? _tagEditorViewModel;

    public AiAgentViewModel AiAgent { get; }

    // 脚本路径显示文本（超30字符中间省略）
    public string ScriptDisplayPath
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentScriptPath) || CurrentScriptPath == NoScriptPathText)
                return UntitledScriptText;
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
        ScheduleSaveUserSettings();
    }

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

    public MainWindowViewModel(ILogService logService, IDeviceService deviceService, ICaptureService captureService, IScriptService scriptService, IControllerService controllerService, IDialogService dialogService, IWindowService windowService, EasyCon2.Avalonia.Core.Threading.IUiDispatcher uiDispatcher, IImageProcessor? imageProcessor = null, Func<ConfigState>? loadUserConfig = null, Action<ConfigState>? saveUserConfig = null)
    {
        _loadUserConfig = loadUserConfig ?? ConfigManager.LoadConfig;
        _saveUserConfig = saveUserConfig ?? ConfigManager.SaveConfig;
        // 初始化 AI Agent，注入编辑区服务
        _toolCallService = new ToolCallService(
            scriptService, captureService, _logBuffer,
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
        AiAgent = new AiAgentViewModel(_toolCallService, _mcpManager, uiDispatcher);
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
        CurrentVersion = $"当前版本：v{fullVer}";

        _logService = logService;
        _deviceService = deviceService;
        _captureService = captureService;
        _scriptService = scriptService;
        _controllerService = controllerService;
        _dialogService = dialogService;
        _windowService = windowService;

        // 连接编排子 ViewModel（Switch 必须先于 Controller：后者依赖前者的连接状态）
        Switch = new SwitchConnectionViewModel(deviceService, logService, uiDispatcher);
        Capture = new CaptureConnectionViewModel(captureService, logService, uiDispatcher);
        Controller = new ControllerConnectionViewModel(controllerService, windowService, logService, () => Switch.IsNintendoSwitchConnected, uiDispatcher);
        Firmware = new FlashFirmwareViewModel(deviceService, scriptService, logService,
            () => EditorText,
            () => HasSelectedScriptPath() ? CurrentScriptPath : null);
        Welcome = new WelcomeConsoleViewModel(scriptService, ConfigState.DefaultWelcomeText, uiDispatcher);

        // 子 VM 与主 VM 的联动
        Switch.DeviceLost += () => IsRecording = false;
        Capture.CaptureConnected += ShowMonitor;
        Capture.CaptureTypeChanged += ScheduleSaveUserSettings;
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

        LoadUserSettings();
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
    private void LoadUserSettings()
    {
        _isLoadingUserSettings = true;
        try
        {
            _userConfig = _loadUserConfig();
            _fileTreeViewModel.SelectSortModeCommand.Execute(
                FileTreeSortModeSettings.FromSettingValue(_userConfig.FileTreeSortMode));

            Capture.SelectedCaptureType = NormalizeCaptureType(_userConfig.CaptureType);
            IsAutoCompletionEnabled = _userConfig.EnableAutoCompletion;
            ShowFolding = _userConfig.ShowFolding;
            IsHighResolutionTimingEnabled = _userConfig.HighResolutionTiming;
            _scriptService.HighResolutionTiming = IsHighResolutionTimingEnabled;
            ShowDebugInfo = _userConfig.ShowDebugInfo;
            var language = !string.IsNullOrEmpty(_userConfig.LanguageCode) && LanguageOptions.Contains(_userConfig.LanguageCode)
                ? _userConfig.LanguageCode
                : "zh_CN";
            SelectedLanguageCode = language;
            App.SetLocale(language);
            WelcomeText = _userConfig.WelcomeText ?? ConfigState.DefaultWelcomeText;
            AutoSwitchLayoutEnabled = _userConfig.AutoSwitchLayoutEnabled;
            AutoSwitchColorSchemeEnabled = _userConfig.AutoSwitchColorSchemeEnabled;
            ApplySavedLayoutSettings(_userConfig);

            var colorSchemeName = NormalizeColorSchemeName(ThemeKeys.Restore(_userConfig.ColorSchemeName), _userConfig.DarkMode);
            var themeStyleName = NormalizeThemeStyleName(ThemeKeys.Restore(_userConfig.ThemeStyleName), colorSchemeName);
            ThemeManager.Instance.ApplyColorScheme(colorSchemeName);
            ThemeManager.Instance.ApplyThemeStyle(themeStyleName);
        }
        catch (Exception ex)
        {
            _userConfig = new ConfigState();
            ThemeManager.Instance.ApplyColorScheme(ThemeManager.whiteGraySchemeName);
            ThemeManager.Instance.ApplyThemeStyle(ThemeManager.ClassicStyleName);
            _logService.AddLog($"读取用户配置失败，已使用默认设置: {ex.Message}");
        }
        finally
        {
            _isLoadingUserSettings = false;
        }
    }

    private CancellationTokenSource? _settingsSaveDefer;

    /// <summary>
    /// 防抖保存用户配置。On*Changed 在连续交互（如欢迎语逐键输入）中高频触发，
    /// 合并为静默 500ms 后的一次写盘，避免 UI 线程同步 IO 反复打断交互。
    /// </summary>
    private void ScheduleSaveUserSettings()
    {
        if (_isLoadingUserSettings)
            return;

        _settingsSaveDefer?.Cancel();
        _settingsSaveDefer?.Dispose();
        _settingsSaveDefer = new CancellationTokenSource();
        var token = _settingsSaveDefer.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested)
                    SaveUserSettings();
            });
        });
    }

    /// <summary>立即写入挂起的用户配置（关窗前调用，防止丢失最后一次更改）。</summary>
    private void FlushPendingUserSettings()
    {
        _settingsSaveDefer?.Cancel();
        SaveUserSettings();
    }

    private void SaveUserSettings()
    {
        if (_isLoadingUserSettings)
            return;

        _userConfig.CaptureType = Capture.SelectedCaptureType ?? "ANY";
        _userConfig.EnableAutoCompletion = IsAutoCompletionEnabled;
        _userConfig.ShowFolding = ShowFolding;
        _userConfig.HighResolutionTiming = IsHighResolutionTimingEnabled;
        _userConfig.ShowDebugInfo = ShowDebugInfo;
        _userConfig.WelcomeText = WelcomeText ?? string.Empty;
        _userConfig.LanguageCode = SelectedLanguageCode;
        _userConfig.FileTreeSortMode = FileTreeSortModeSettings.ToSettingValue(_fileTreeViewModel.SortMode);
        _userConfig.AutoSwitchLayoutEnabled = AutoSwitchLayoutEnabled;
        _userConfig.AutoSwitchColorSchemeEnabled = AutoSwitchColorSchemeEnabled;
        _userConfig.IsIdleThreeColumnLayoutSelected = IsIdleThreeColumnLayoutSelected;
        _userConfig.IsIdleTwoColumnLayoutSelected = IsIdleTwoColumnLayoutSelected;
        _userConfig.IsRunningThreeColumnLayoutSelected = IsRunningThreeColumnLayoutSelected;
        _userConfig.IsRunningTwoColumnLayoutSelected = IsRunningTwoColumnLayoutSelected;
        _userConfig.IsRunningOneColumnLayoutSelected = IsRunningOneColumnLayoutSelected;
        _userConfig.ColorSchemeName = ThemeKeys.Persist(ThemeManager.Instance.SelectedColorSchemeName);
        _userConfig.ThemeStyleName = ThemeKeys.Persist(ThemeManager.Instance.SelectedThemeStyleName);
        _userConfig.DarkMode = ThemeManager.Instance.IsDarkMode;

        try
        {
            _saveUserConfig(_userConfig);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"保存用户配置失败: {ex.Message}");
        }
    }

    private void ApplySavedLayoutSettings(ConfigState config)
    {
        var idleTwoColumn = config.IsIdleTwoColumnLayoutSelected;
        IsIdleThreeColumnLayoutSelected = !idleTwoColumn;
        IsIdleTwoColumnLayoutSelected = idleTwoColumn;

        var runningOneColumn = config.IsRunningOneColumnLayoutSelected;
        var runningThreeColumn = config.IsRunningThreeColumnLayoutSelected && !runningOneColumn;
        IsRunningThreeColumnLayoutSelected = runningThreeColumn;
        IsRunningTwoColumnLayoutSelected = !runningThreeColumn && !runningOneColumn;
        IsRunningOneColumnLayoutSelected = runningOneColumn;
    }

    private static string NormalizeCaptureType(string? captureType)
    {
        return captureType switch
        {
            "DSHOW" or "MSMF" or "DC1394" => captureType,
            _ => "ANY"
        };
    }

    private static string NormalizeColorSchemeName(string? colorSchemeName, bool legacyDarkMode)
    {
        return colorSchemeName switch
        {
            ThemeManager.whiteGraySchemeName or "工业灰" => ThemeManager.whiteGraySchemeName,
            ThemeManager.WarmToneSchemeName or "暖色调" => ThemeManager.WarmToneSchemeName,
            ThemeManager.DarkModeSchemeName => ThemeManager.DarkModeSchemeName,
            _ => legacyDarkMode ? ThemeManager.DarkModeSchemeName : ThemeManager.whiteGraySchemeName
        };
    }

    private static string NormalizeThemeStyleName(string? themeStyleName, string colorSchemeName)
    {
        return themeStyleName switch
        {
            ThemeManager.ClassicStyleName => ThemeManager.ClassicStyleName,
            ThemeManager.RoundedStyleName => ThemeManager.RoundedStyleName,
            _ => colorSchemeName == ThemeManager.whiteGraySchemeName
                ? ThemeManager.ClassicStyleName
                : ThemeManager.RoundedStyleName
        };
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
        ScheduleSaveUserSettings();
        _logService.AddLog($"已切换外观: {colorSchemeName}");
    }

    private void SelectThemeStyle(string? themeStyleName)
    {
        if (string.IsNullOrWhiteSpace(themeStyleName))
            return;

        ThemeManager.Instance.ApplyThemeStyle(themeStyleName);
        ScheduleSaveUserSettings();
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
        ScheduleSaveUserSettings();
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
        CheckUpdateButtonText = "正在检查…";
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var data = await client.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags?sort=updated&direction=desc&per_page=3");
            var tags = System.Text.Json.JsonSerializer.Deserialize<VerInfo[]>(data);
            if (tags == null || tags.Length == 0)
            {
                CheckUpdateButtonText = "检查失败";
                return;
            }
            var cutoff = new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc);
            var latest = tags.FirstOrDefault(t => t.commit != null && t.commit.date >= cutoff);
            if (latest == null)
            {
                CheckUpdateButtonText = "当前已是最新版本";
                return;
            }
            // tag 名不保证是合法版本号（v 前缀/任意命名），解析失败按“已是最新”处理，
            // 不能让单条坏 tag 使更新检查报“检查失败”
            var curVer = Assembly.GetEntryAssembly()?.GetName().Version;
            if (ConfigService.TryParseTagVersion(latest.name) is { } latestVer && latestVer > curVer)
                CheckUpdateButtonText = $"发现新版本 v{latestVer}";
            else
                CheckUpdateButtonText = "当前已是最新版本";
        }
        catch
        {
            CheckUpdateButtonText = "检查失败";
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

    private record CommitInfo
    {
        public string sha { get; set; } = "";
        public DateTime date { get; set; }
    }

    private record VerInfo
    {
        public string name { get; set; } = "";
        public CommitInfo? commit { get; set; }
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
                try
                {
                    var label = ECCore.LoadIL(filePath);
                    if (TagEditorViewModel != null)
                    {
                        TagEditorViewModel.LoadFromLabel(label);
                    }
                    else
                    {
                        var tagVm = new TagEditorViewModel(label, _imageProcessor);
                        tagVm.OpenFileRequested += OnTagEditorOpenFileRequested;
                        tagVm.CaptureScreenshotRequested += OnTagEditorCaptureScreenshot;
                        tagVm.LabelTestRequested += OnTagEditorLabelTest;
                        tagVm.LogMessage += message => _logService.AddLog(message);
                        TagEditorViewModel = tagVm;
                    }
                }
                catch (Exception ex)
                {
                    _logService.AddLog($"加载标签文件失败: {ex.Message}");
                }
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
        var tagVm = new TagEditorViewModel(_imageProcessor);
        tagVm.OpenFileRequested += OnTagEditorOpenFileRequested;
        tagVm.CaptureScreenshotRequested += OnTagEditorCaptureScreenshot;
        tagVm.LabelTestRequested += OnTagEditorLabelTest;
        tagVm.LogMessage += message => _logService.AddLog(message);
        TagEditorViewModel = tagVm;
    }

    private void InitializeSampleData()
    {
        // 串口/视频源/控制源列表已由各连接子 ViewModel 在构造时刷新
        _logService.AddLog($"已加载 {Switch.SerialPortOptions.Count} 个可用串口, {Capture.CaptureSourceOptions.Count} 个视频源");
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

        IsSavingScript = true;
        try
        {
            string previousPath = CurrentScriptPath;
            bool needsSaveDialog = saveAs || !HasSelectedScriptPath();
            string targetPath;
            if (needsSaveDialog)
            {
                string suggestedName = HasSelectedScriptPath()
                    ? Path.GetFileName(CurrentScriptPath)
                    : $"{UntitledScriptText}.ecs";
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

            if (!SaveEditorText(targetPath, EditorText))
                return;

            CurrentScriptPath = targetPath;
            IsScriptModified = false;
            _logService.AddLog($"已保存脚本: {targetPath}");

            if (!string.IsNullOrWhiteSpace(_projectDirectoryPath))
            {
                if (IsPathInsideDirectory(targetPath, _projectDirectoryPath))
                    _fileTreeViewModel.NotifyFileSaved(targetPath);
            }
            else if (saveAs || previousPath == UntitledScriptText)
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
            IsSavingScript = false;
        }
    }

    private bool CanSaveScript()
    {
        return !IsSavingScript
            && IsTextEditorTabSelected
            && (CurrentScriptPath == UntitledScriptText || HasSelectedScriptPath());
    }

    private void NotifySaveCommandCanExecuteChanged()
    {
        SaveScriptCommand?.NotifyCanExecuteChanged();
        SaveScriptAsCommand?.NotifyCanExecuteChanged();
    }

    private void OnFileTreeSortModeChanged()
    {
        ScheduleSaveUserSettings();
    }

    private void CloseScript()
    {
        SetEditorTextWithoutDirtyTracking("");
        IsScriptModified = false;
        CurrentScriptPath = NoScriptPathText;
        SelectedEditorTab = 0;
    }

    private void NewScript()
    {
        SetEditorTextWithoutDirtyTracking("");
        IsScriptModified = false;
        CurrentScriptPath = UntitledScriptText;
        SelectedEditorTab = 0;
        _logService.AddLog("已新建脚本");
    }

    private void CloseProject()
    {
        _projectDirectoryPath = null;
        _fileTreeViewModel.LoadDirectory(null);
        _logService.AddLog("已关闭项目");
    }

    private bool HasSelectedScriptPath()
    {
        return !string.IsNullOrWhiteSpace(CurrentScriptPath)
            && CurrentScriptPath != NoScriptPathText
            && CurrentScriptPath != UntitledScriptText;
    }

    private bool SaveEditorText(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            // 保存失败必须显式暴露：保持 IsScriptModified，避免"假成功"导致数据丢失
            _logService.AddLog($"保存脚本失败({path}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从路径打开脚本，加载文件树并初始化内嵌编辑器。
    /// </summary>
    public void OpenScriptFromPath(string path)
    {
        CurrentScriptPath = path;
        SelectedEditorTab = 0;

        // 更新文件树到脚本所在目录
        var dir = Path.GetDirectoryName(path);
        _projectDirectoryPath = dir;
        _fileTreeViewModel.LoadDirectory(dir);

        // 初始化内嵌编辑器（加载文件内容不属于用户修改）
        _suppressDirtyTracking = true;
        try
        {
            InitializeEmbeddedEditor(path);
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
        IsScriptModified = false;
    }

    /// <summary>
    /// 请求主窗口初始化内嵌编辑器。由 MainWindow 调用。
    /// </summary>
    public event Action<string>? EmbeddedEditorInitializeRequested;

    /// <summary>
    /// 当前编辑区文本（TwoWay 绑定到 ScriptEditorControl.EditorText）。
    /// </summary>
    [ObservableProperty]
    private string _editorText = "";

    /// <summary>
    /// 当前脚本内容是否有未保存的修改。
    /// </summary>
    [ObservableProperty]
    private bool _isScriptModified;

    [ObservableProperty]
    private bool _isSavingScript;

    /// <summary>
    /// 程序化设置编辑区文本（打开/新建/关闭脚本）时抑制修改标记。
    /// </summary>
    private bool _suppressDirtyTracking;

    /// <summary>
    /// 在不触发修改标记的情况下设置编辑区文本。
    /// </summary>
    private void SetEditorTextWithoutDirtyTracking(string text)
    {
        _suppressDirtyTracking = true;
        try
        {
            EditorText = text;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    partial void OnEditorTextChanged(string value)
    {
        if (!_suppressDirtyTracking)
            IsScriptModified = true;
        NotifySaveCommandCanExecuteChanged();
    }

    partial void OnIsScriptModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(ScriptDisplayPath));
        NotifySaveCommandCanExecuteChanged();
    }

    partial void OnIsSavingScriptChanged(bool value)
    {
        NotifySaveCommandCanExecuteChanged();
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
        _windowService.ShowESPConfigWindow();
    }

    private void OpenAlertConfig()
    {
        _windowService.ShowAlertConfigWindow();
    }

    private void OpenModelsConfig()
    {
        _windowService.ShowModelsConfigWindow();
    }

    private void OpenMcpConfig()
    {
        _windowService.ShowMcpConfigWindow();
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
        ScheduleSaveUserSettings();
    }

    partial void OnIsIdleTwoColumnLayoutSelectedChanged(bool value)
    {
        if (value)
            IsIdleThreeColumnLayoutSelected = false;
        ScheduleSaveUserSettings();
    }

    partial void OnIsRunningThreeColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningTwoColumnLayoutSelected = false;
            IsRunningOneColumnLayoutSelected = false;
        }
        ScheduleSaveUserSettings();
    }

    partial void OnIsRunningTwoColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningThreeColumnLayoutSelected = false;
            IsRunningOneColumnLayoutSelected = false;
        }
        ScheduleSaveUserSettings();
    }

    partial void OnIsRunningOneColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningThreeColumnLayoutSelected = false;
            IsRunningTwoColumnLayoutSelected = false;
        }
        ScheduleSaveUserSettings();
    }

    private void ShowScriptSyntax()
    {
        _windowService.ShowScriptSyntaxWindow();
    }

    private void OpenAiAgent()
    {
        AiAgent.IsOpen = true;
    }

    partial void OnCurrentScriptPathChanged(string value)
    {
        (OpenEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ScriptDisplayPath));
        NotifySaveCommandCanExecuteChanged();
    }

    private string? GetCurrentScriptRootDirectory()
    {
        var currentPath = CurrentScriptPath;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            currentPath != "未选择脚本" &&
            File.Exists(currentPath))
        {
            if (IsPathInsideDirectory(currentPath, _projectDirectoryPath))
                return _projectDirectoryPath;

            return Path.GetDirectoryName(currentPath);
        }

        return _projectDirectoryPath;
    }

    private static bool IsPathInsideDirectory(string filePath, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
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
        ScheduleSaveUserSettings();
    }

    partial void OnAutoSwitchLayoutEnabledChanged(bool value)
    {
        ScheduleSaveUserSettings();
    }

    partial void OnAutoSwitchColorSchemeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualColorSchemeEnabled));

        if (value)
            ApplySystemColorScheme();

        ScheduleSaveUserSettings();
    }

    partial void OnShowFoldingChanged(bool value)
    {
        FoldingVisibilityChanged?.Invoke(value);
        ScheduleSaveUserSettings();
    }

    partial void OnIsHighResolutionTimingEnabledChanged(bool value)
    {
        _scriptService.HighResolutionTiming = value;
        ScheduleSaveUserSettings();
    }

    partial void OnWelcomeTextChanged(string value)
    {
        Welcome.SetWelcomeText(value);
        ScheduleSaveUserSettings();
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


    private void OnTagEditorOpenFileRequested()
    {
        _ = OpenTagEditorImageFileAsync();
    }

    private void OnTagEditorCaptureScreenshot()
    {
        _ = CaptureScreenshotForTagEditorAsync();
    }

    private void OnTagEditorLabelTest()
    {
        _ = ExecuteLabelTestAsync();
    }

    private async Task ExecuteLabelTestAsync()
    {
        if (TagEditorViewModel == null) return;

        if (!_captureService.IsConnected)
        {
            _logService.AddLog("请先连接视频源");
            return;
        }

        try
        {
            byte[]? resultPng = null;
            double matchDegree = 0;
            string labelName = "";

            // 帧租约与 OpenCV 模板匹配都在线程池完成，避免大图匹配冻结 UI
            await Task.Run(() =>
            {
                using var lease = _captureService.AcquireLatestFrame();
                if (lease == null || lease.Mat.Empty())
                    throw new InvalidOperationException(L10n.T("Text.Msg.TagTestNoFrame"));

                var mat = lease.Mat;
                var label = TagEditorViewModel.Label;
                var result = label.Search(mat, out matchDegree, "");

                byte[]? png = null;
                if (result.Count > 0)
                {
                    var pt = result[0];
                    int roiX = Math.Clamp(label.RangeX + pt.X, 0, mat.Width);
                    int roiY = Math.Clamp(label.RangeY + pt.Y, 0, mat.Height);
                    int roiW = Math.Clamp(label.TargetWidth, 0, mat.Width - roiX);
                    int roiH = Math.Clamp(label.TargetHeight, 0, mat.Height - roiY);

                    if (roiW > 0 && roiH > 0)
                    {
                        using var roi = new Mat(mat, new Rect(roiX, roiY, roiW, roiH));
                        png = roi.ToBytes(".png");
                    }
                }

                resultPng = png;
                labelName = label.name;
            });

            // 更新匹配度显示和结果图
            TagEditorViewModel.SetTestResult(matchDegree, resultPng);

            _logService.AddLog(string.Format(L10n.T("Text.Msg.TagTestResult"), labelName, matchDegree));
        }
        catch (InvalidOperationException ex)
        {
            _logService.AddLog(ex.Message);
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10n.T("Text.Msg.TagTestFail"), ex.Message));
        }
    }

    private async Task OpenTagEditorImageFileAsync()
    {
        try
        {
            var cacheDir = EasyCon.Core.Config.AppPaths.CaptureCacheDir;
            var files = await _dialogService.OpenFilesAsync("选择图片文件",
            [
                new FileDialogFilter("图片文件", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]),
                new FileDialogFilter("所有文件", ["*"])
            ], cacheDir);

            if (files.Count > 0 && TagEditorViewModel != null)
                TagEditorViewModel.LoadImageFromFile(files[0]);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开文件对话框失败: {ex.Message}");
        }
    }

    private async Task CaptureScreenshotForTagEditorAsync()
    {
        if (!_captureService.IsConnected)
        {
            _logService.AddLog("请先连接视频源");
            return;
        }

        try
        {
            using var lease = _captureService.AcquireLatestFrame();
            if (lease == null || lease.Mat.Empty())
            {
                _logService.AddLog("截图失败：无法获取视频帧");
                return;
            }
            var mat = lease.Mat;

            // Mat 编码为 PNG 字节交给 VM（VM 内部解码显示并保留字节供裁剪）；
            // 1080p PNG 编码可达百毫秒级，放后台执行避免冻结 UI（租约保活至编码完成）
            var imageBytes = await Task.Run(() => mat.ToBytes(".png"));
            TagEditorViewModel?.SetScreenshot(imageBytes);
            _logService.AddLog("截图成功");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"截图失败：{ex.Message}");
        }
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
        ScheduleSaveUserSettings();
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

        // 立即写入防抖中挂起的用户配置
        FlushPendingUserSettings();

        // 关闭嵌入式监视器
        if (_monitorViewModel != null)
        {
            _monitorViewModel.Close();
            _monitorViewModel = null;
        }

        // 停止仍在运行的脚本（复位按键、丢弃排队 HID 报文）
        if (_scriptService.IsRunning)
        {
            _scriptService.Stop();
        }

        // 释放控制器资源（SDL3 事件循环等）移至 App desktop.Exit 单一清理入口，
        // 此处不再提前 Dispose（双通道释放靠幂等保护是巧合不是设计）

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
            _logService.AddLog("请先连接单片机");
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
            _logService.AddLog("请先连接单片机");
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