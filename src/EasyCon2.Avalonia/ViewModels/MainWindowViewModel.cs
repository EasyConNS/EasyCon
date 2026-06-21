using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.AiAgent;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Core.TagEditor;
using EasyCon2.Avalonia.Core.Terminal;
using EasyCon2.Avalonia.Services;
using EzCv;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string NoScriptPathText = "未选择脚本";
    private const string UntitledScriptText = "未命名脚本";
    private static readonly Color[] WelcomePalette =
    [
        Color.FromRgb(0xF9, 0x5D, 0x6A),
        Color.FromRgb(0xF8, 0xB4, 0x4C),
        Color.FromRgb(0x9C, 0xD8, 0x5B),
        Color.FromRgb(0x46, 0xD6, 0xC8),
        Color.FromRgb(0x5A, 0x9C, 0xFF),
        Color.FromRgb(0xC7, 0x7D, 0xFF)
    ];

    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly IScriptService _scriptService;
    private readonly IControllerService _controllerService;
    private readonly IDialogService _dialogService;
    private readonly IWindowService _windowService;
    private readonly ToolCallService _toolCallService;
    private readonly AnsiParser _ansiParser = new();
    private MonitorViewModel? _monitorViewModel;
    private readonly FileTreeViewModel _fileTreeViewModel;
    private readonly System.Timers.Timer _welcomeTimer = new(120);
    private string? _projectDirectoryPath;
    private ConfigState _userConfig = new();
    private bool _isLoadingUserSettings;
    private int _welcomeColorOffset;

    /// <summary>日志环形缓冲，供 AI 工具读取近期运行日志。</summary>
    private const int LogBufferSize = 200;
    private readonly Queue<string> _logBuffer = new();

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
    public ObservableCollection<TerminalLine> WelcomeLines { get; } = new();
    public ObservableCollection<TerminalLine> LogLines { get; } = new();

    [ObservableProperty]
    private bool _showDebugInfo;

    // 单片机连接相关属性
    [ObservableProperty]
    private ObservableCollection<string> _serialPortOptions = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty]
    private string _nintendoSwitchStatus = "未连接";

    [ObservableProperty]
    private bool _isConnectingNintendoSwitch = false;

    [ObservableProperty]
    private bool _isNintendoSwitchConnected = false;

    [ObservableProperty]
    private string _nintendoSwitchButtonText = "连接单片机";

    // 视频源连接相关属性
    [ObservableProperty]
    private ObservableCollection<string> _captureSourceOptions = new();

    [ObservableProperty]
    private string? _selectedCaptureSource;

    [ObservableProperty]
    private string _captureSourceStatus = "未连接";

    [ObservableProperty]
    private bool _isConnectingCaptureSource = false;

    [ObservableProperty]
    private bool _isCaptureSourceConnected = false;

    [ObservableProperty]
    private string _captureSourceButtonText = "连接视频源";

    // 虚拟手柄相关属性
    [ObservableProperty]
    private ObservableCollection<string> _controlSourceOptions = new();

    [ObservableProperty]
    private string? _selectedControlSource;

    [ObservableProperty]
    private string _controlSourceStatus = "未连接";

    [ObservableProperty]
    private bool _isConnectingController = false;

    [ObservableProperty]
    private bool _isControllerConnected = false;

    [ObservableProperty]
    private string _controllerButtonText = "开启映射";

    [ObservableProperty]
    private bool _isEditKeyMappingEnabled = true;

    [ObservableProperty]
    private bool _isRecording = false;

    public bool IsStartRecordEnabled => !IsRecording;
    public bool IsStopRecordEnabled => IsRecording;

    // 运行脚本相关属性
    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _runButtonText = "运行";

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
    public string MonitorPauseButtonText => IsMonitorPaused ? "继续" : "暂停";
    public string MonitorVisibilityButtonText => IsMonitorVisible ? "监视器关闭" : "监视器显示";

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

    // 固件类型列表
    [ObservableProperty]
    private ObservableCollection<string> _firmwareOptions = new() { "leonardo" };

    public ICommand OpenScriptCommand { get; }
    [ObservableProperty]
    private string _selectedFirmware = "leonardo";

    [ObservableProperty]
    private ObservableCollection<string> _captureTypeOptions = new() { "ANY", "DSHOW", "MSMF", "DC1394" };

    [ObservableProperty]
    private string _selectedCaptureType = "ANY";

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
    public ICommand SaveScriptCommand { get; }
    public ICommand SaveScriptAsCommand { get; }
    public ICommand CloseScriptCommand { get; }
    public ICommand FormatScriptCommand { get; }
    public ICommand OpenEditorCommand { get; }
    public ICommand ConnectNintendoSwitchCommand { get; }
    public ICommand AutoConnectNintendoSwitchCommand { get; }
    public ICommand ConnectCaptureSourceCommand { get; }
    public ICommand ConnectControllerCommand { get; }
    public ICommand EditKeyMappingCommand { get; }
    public ICommand RunScriptCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand DropFileCommand { get; }
    public ICommand RemoteRunCommand { get; }
    public ICommand RemoteStopCommand { get; }
    public ICommand CompileFlashCommand { get; }
    public ICommand ClearFlashCommand { get; }
    public ICommand GenerateFirmwareCommand { get; }
    public ICommand StartRecordCommand { get; }
    public ICommand StopRecordCommand { get; }
    public ICommand ShowMonitorCommand { get; }
    public ICommand OpenTagEditorCommand { get; }
    public ICommand OpenESPConfigCommand { get; }
    public ICommand OpenAlertConfigCommand { get; }
    public ICommand OpenModelsConfigCommand { get; }
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

    // 刷新数据源命令
    public ICommand RefreshSerialPortsCommand { get; }
    public ICommand RefreshCaptureSourcesCommand { get; }
    public ICommand RefreshControlSourcesCommand { get; }

    public MainWindowViewModel(ILogService logService, IDeviceService deviceService, ICaptureService captureService, IScriptService scriptService, IControllerService controllerService, IDialogService dialogService, IWindowService windowService)
    {
        // 初始化 AI Agent，注入编辑区服务
        _toolCallService = new ToolCallService(
            scriptService, captureService, _logBuffer,
            () => _projectDirectoryPath,
            () => EditorText ?? string.Empty,
            v => EditorText = v,
            () => HasSelectedScriptPath() ? CurrentScriptPath : null,
            () => HasSelectedScriptPath(),
            () => new DeviceStatusInfo(
                IsNintendoSwitchConnected,
                IsCaptureSourceConnected,
                IsControllerConnected,
                scriptService.IsRunning)
        );
        AiAgent = new AiAgentViewModel(_toolCallService);
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

        // 初始化文件树
        _fileTreeViewModel = new FileTreeViewModel();
        _fileTreeViewModel.FileActivated += OnFileTreeFileActivated;
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
                        _logBuffer.Dequeue();
                }
            }
        };

        // 订阅设备外部断开事件
        _deviceService.ConnectionLost += () =>
        {
            if (!IsNintendoSwitchConnected) return;
            IsNintendoSwitchConnected = false;
            NintendoSwitchStatus = "已断开";
            NintendoSwitchButtonText = "连接单片机";
            IsRecording = false;
        };

        // 订阅视频源外部断开事件
        _captureService.ConnectionLost += () =>
        {
            if (!IsCaptureSourceConnected) return;
            IsCaptureSourceConnected = false;
            CaptureSourceStatus = "已断开";
            CaptureSourceButtonText = "连接视频源";
        };

        // 订阅脚本运行状态变化
        _scriptService.IsRunningChanged += running =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = running;
                RunButtonText = running ? "停止" : "运行";
                RefreshWelcomeConsole();

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

        // 订阅手柄热插拔事件
        _controllerService.AvailableSourcesChanged += () =>
        {
            Dispatcher.UIThread.Post(RefreshControlSources);
        };

        // 订阅控制器外部断开事件（VPad 中键/ESC 退出）
        _controllerService.Disconnected += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsControllerConnected = false;
                ControlSourceStatus = "未连接";
                ControllerButtonText = "开启映射";
                UpdateEditKeyMappingEnabled();
                _logService.AddLog("手柄已断开连接");
            });
        };

        // 初始化命令
        OpenScriptCommand = new AsyncRelayCommand(OpenScriptAsync);
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
        SaveScriptAsCommand = new AsyncRelayCommand(SaveScriptAsAsync);
        CloseScriptCommand = new RelayCommand(CloseScript);
        FormatScriptCommand = new AsyncRelayCommand(FormatScriptAsync);
        OpenEditorCommand = new RelayCommand(OpenEditor, CanOpenEditor);
        ConnectNintendoSwitchCommand = new RelayCommand(ConnectNintendoSwitch);
        AutoConnectNintendoSwitchCommand = new RelayCommand(AutoConnectNintendoSwitch);
        ConnectCaptureSourceCommand = new RelayCommand(ConnectCaptureSource);
        ConnectControllerCommand = new RelayCommand(ConnectController);
        EditKeyMappingCommand = new RelayCommand(EditKeyMapping);
        RunScriptCommand = new RelayCommand(RunScript);
        ClearLogCommand = new RelayCommand(ClearLog);
        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPorts);
        RefreshCaptureSourcesCommand = new RelayCommand(RefreshCaptureSources);
        RefreshControlSourcesCommand = new RelayCommand(RefreshControlSources);
        DropFileCommand = new RelayCommand<string>(DropFile);
        RemoteRunCommand = new RelayCommand(RemoteRun);
        RemoteStopCommand = new RelayCommand(RemoteStop);
        CompileFlashCommand = new AsyncRelayCommand(CompileFlashAsync);
        ClearFlashCommand = new RelayCommand(ClearFlash);
        GenerateFirmwareCommand = new AsyncRelayCommand(GenerateFirmwareAsync);
        StartRecordCommand = new RelayCommand(StartRecord);
        StopRecordCommand = new RelayCommand(StopRecord);
        ShowMonitorCommand = new RelayCommand(ShowMonitor);
        OpenTagEditorCommand = new RelayCommand(OpenTagEditor);
        OpenESPConfigCommand = new RelayCommand(OpenESPConfig);
        OpenAlertConfigCommand = new RelayCommand(OpenAlertConfig);
        OpenModelsConfigCommand = new RelayCommand(OpenModelsConfig);
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

        // 初始化示例数据
        InitializeSampleData();
        InitializeWelcomeConsole();

        _runTimer.Elapsed += (s, e) =>
        {
            var elapsed = DateTime.Now - _runStartTime;
            Dispatcher.UIThread.Post(() =>
            {
                RunTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");
            });
        };

    }

    private void InitializeWelcomeConsole()
    {
        RefreshWelcomeConsole();
        _welcomeTimer.Elapsed += (_, _) =>
        {
            if (!_scriptService.IsRunning)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                _welcomeColorOffset++;
                UpdateWelcomeConsoleColors();
            });
        };
        _welcomeTimer.Start();
    }

    private void RefreshWelcomeConsole()
    {
        WelcomeLines.Clear();
        WelcomeLines.Add(CreateWelcomeLine());
    }

    private TerminalLine CreateWelcomeLine()
    {
        var line = new TerminalLine();
        FillWelcomeLine(line);
        return line;
    }

    private void UpdateWelcomeConsoleColors()
    {
        if (WelcomeLines.Count == 0)
        {
            RefreshWelcomeConsole();
            return;
        }

        var line = WelcomeLines[0];
        line.Segments.Clear();
        FillWelcomeLine(line);

        // 仅触发重绘通知，不让集合经历 Clear 状态，避免跑马灯偏移被重置。
        WelcomeLines[0] = line;
    }

    private void FillWelcomeLine(TerminalLine line)
    {
        var welcomeText = WelcomeText ?? string.Empty;
        for (var i = 0; i < welcomeText.Length; i++)
        {
            var color = WelcomePalette[Mod(i - _welcomeColorOffset, WelcomePalette.Length)];
            line.Segments.Add(new TextSegment(welcomeText[i].ToString(), color));
        }
    }

    private TerminalLine ParseLogLine(string rawLine, string? color)
    {
        if (rawLine.Contains('\x1b'))
            return _ansiParser.ParseLine(rawLine);

        var foreground = TryParseLogColor(color);
        if (foreground == null)
            return _ansiParser.ParseLine(rawLine);

        var line = new TerminalLine();
        line.Segments.Add(new TextSegment(rawLine, foreground));
        return line;
    }

    private static Color? TryParseLogColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        return color.Trim() switch
        {
            "Lime" => Color.FromRgb(0x32, 0xD7, 0x4B),
            "Orange" => Color.FromRgb(0xF5, 0x9E, 0x0B),
            "OrangeRed" => Color.FromRgb(0xFF, 0x5A, 0x3D),
            "Red" => Color.FromRgb(0xEF, 0x44, 0x44),
            "Yellow" => Color.FromRgb(0xF4, 0xD0, 0x3F),
            "Green" => Color.FromRgb(0x22, 0xC5, 0x5E),
            "Cyan" => Color.FromRgb(0x22, 0xD3, 0xEE),
            "Blue" => Color.FromRgb(0x60, 0xA5, 0xFA),
            "Magenta" => Color.FromRgb(0xE8, 0x79, 0xF9),
            _ => TryParseAvaloniaColor(color)
        };
    }

    private static Color? TryParseAvaloniaColor(string color)
    {
        try
        {
            return Color.Parse(color);
        }
        catch
        {
            return null;
        }
    }

    private static int Mod(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private void LoadUserSettings()
    {
        _isLoadingUserSettings = true;
        try
        {
            _userConfig = ConfigManager.LoadConfig();

            SelectedCaptureType = NormalizeCaptureType(_userConfig.CaptureType);
            _captureService.CaptureType = SelectedCaptureType;
            IsAutoCompletionEnabled = _userConfig.EnableAutoCompletion;
            ShowFolding = _userConfig.ShowFolding;
            IsHighResolutionTimingEnabled = _userConfig.HighResolutionTiming;
            _scriptService.HighResolutionTiming = IsHighResolutionTimingEnabled;
            ShowDebugInfo = _userConfig.ShowDebugInfo;
            WelcomeText = _userConfig.WelcomeText ?? ConfigState.DefaultWelcomeText;
            AutoSwitchLayoutEnabled = _userConfig.AutoSwitchLayoutEnabled;
            AutoSwitchColorSchemeEnabled = _userConfig.AutoSwitchColorSchemeEnabled;
            ApplySavedLayoutSettings(_userConfig);

            var colorSchemeName = NormalizeColorSchemeName(_userConfig.ColorSchemeName, _userConfig.DarkMode);
            var themeStyleName = NormalizeThemeStyleName(_userConfig.ThemeStyleName, colorSchemeName);
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

    private void SaveUserSettings()
    {
        if (_isLoadingUserSettings)
            return;

        _userConfig.CaptureType = NormalizeCaptureType(SelectedCaptureType);
        _userConfig.EnableAutoCompletion = IsAutoCompletionEnabled;
        _userConfig.ShowFolding = ShowFolding;
        _userConfig.HighResolutionTiming = IsHighResolutionTimingEnabled;
        _userConfig.ShowDebugInfo = ShowDebugInfo;
        _userConfig.WelcomeText = WelcomeText ?? string.Empty;
        _userConfig.AutoSwitchLayoutEnabled = AutoSwitchLayoutEnabled;
        _userConfig.AutoSwitchColorSchemeEnabled = AutoSwitchColorSchemeEnabled;
        _userConfig.IsIdleThreeColumnLayoutSelected = IsIdleThreeColumnLayoutSelected;
        _userConfig.IsIdleTwoColumnLayoutSelected = IsIdleTwoColumnLayoutSelected;
        _userConfig.IsRunningThreeColumnLayoutSelected = IsRunningThreeColumnLayoutSelected;
        _userConfig.IsRunningTwoColumnLayoutSelected = IsRunningTwoColumnLayoutSelected;
        _userConfig.IsRunningOneColumnLayoutSelected = IsRunningOneColumnLayoutSelected;
        _userConfig.ColorSchemeName = ThemeManager.Instance.SelectedColorSchemeName;
        _userConfig.ThemeStyleName = ThemeManager.Instance.SelectedThemeStyleName;
        _userConfig.DarkMode = ThemeManager.Instance.IsDarkMode;

        try
        {
            ConfigManager.SaveConfig(_userConfig);
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
        OpenFolderDialogRequested?.Invoke();
    }

    private void SelectColorScheme(string? colorSchemeName)
    {
        if (AutoSwitchColorSchemeEnabled || string.IsNullOrWhiteSpace(colorSchemeName))
            return;

        ThemeManager.Instance.ApplyColorScheme(colorSchemeName);
        SaveUserSettings();
        _logService.AddLog($"已切换外观: {colorSchemeName}");
    }

    private void SelectThemeStyle(string? themeStyleName)
    {
        if (string.IsNullOrWhiteSpace(themeStyleName))
            return;

        ThemeManager.Instance.ApplyThemeStyle(themeStyleName);
        SaveUserSettings();
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
        SaveUserSettings();
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
            var curVer = Assembly.GetEntryAssembly()?.GetName().Version;
            CheckUpdateButtonText = latest.Ver > curVer
                ? $"发现新版本 v{latest.Ver}"
                : "当前已是最新版本";
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
        Process.Start(new ProcessStartInfo("https://github.com/EasyConNS/EasyCon") { UseShellExecute = true });
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
        public Version Ver => new(name ?? "");
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
                        var tagVm = new TagEditorViewModel(label);
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
        var tagVm = new TagEditorViewModel();
        tagVm.OpenFileRequested += OnTagEditorOpenFileRequested;
        tagVm.CaptureScreenshotRequested += OnTagEditorCaptureScreenshot;
        tagVm.LabelTestRequested += OnTagEditorLabelTest;
        tagVm.LogMessage += message => _logService.AddLog(message);
        TagEditorViewModel = tagVm;
    }

    private void InitializeSampleData()
    {
        // 获取可用串口列表
        var ports = _deviceService.GetAvailablePorts();
        SerialPortOptions = new ObservableCollection<string>(ports);

        // 获取可用视频源列表
        var captureSources = _captureService.GetAvailableSources();
        CaptureSourceOptions = new ObservableCollection<string>(captureSources);

        ControlSourceOptions = new ObservableCollection<string>(_controllerService.GetAvailableSources());

        SelectedSerialPort = SerialPortOptions.FirstOrDefault();
        SelectedCaptureSource = CaptureSourceOptions.FirstOrDefault();
        SelectedControlSource = ControlSourceOptions.FirstOrDefault();

        _logService.AddLog($"已加载 {ports.Length} 个可用串口, {captureSources.Length} 个视频源");
    }

    private void RefreshSerialPorts()
    {
        var ports = _deviceService.GetAvailablePorts();
        var oldSelected = SelectedSerialPort;
        SerialPortOptions = new ObservableCollection<string>(ports);
        SelectedSerialPort = SerialPortOptions.FirstOrDefault();
        if (oldSelected != null && SerialPortOptions.Contains(oldSelected))
            SelectedSerialPort = oldSelected;
    }

    private void RefreshCaptureSources()
    {
        var captureSources = _captureService.GetAvailableSources();
        var oldSelected = SelectedCaptureSource;
        CaptureSourceOptions = new ObservableCollection<string>(captureSources);
        SelectedCaptureSource = CaptureSourceOptions.FirstOrDefault();
        if (oldSelected != null && CaptureSourceOptions.Contains(oldSelected))
            SelectedCaptureSource = oldSelected;
    }

    private void RefreshControlSources()
    {
        var oldSelected = SelectedControlSource;
        ControlSourceOptions = new ObservableCollection<string>(_controllerService.GetAvailableSources());
        SelectedControlSource = ControlSourceOptions.FirstOrDefault();
        if (oldSelected != null && ControlSourceOptions.Contains(oldSelected))
            SelectedControlSource = oldSelected;
    }

    private async Task OpenScriptAsync()
    {
        var files = await _dialogService.OpenFilesAsync("打开脚本文件",
        [
            new FilePickerFileType("ECS脚本文件") { Patterns = ["*.ecs"] },
            new FilePickerFileType("文本文件") { Patterns = ["*.txt"] },
            new FilePickerFileType("所有文件") { Patterns = ["*"] }
        ]);

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
        _ = SaveScriptAsync();
    }

    private void OnSaveScriptAsRequested()
    {
        _ = SaveScriptAsAsync();
    }

    private async Task OpenProjectFolderAsync()
    {
        var startPath = _projectDirectoryPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var folder = await _dialogService.OpenFolderAsync("打开项目目录", startPath);

        if (folder != null)
            OpenProjectFromDirectory(folder);
    }

    private async Task SaveScriptAsync()
    {
        if (!HasSelectedScriptPath())
        {
            await SaveScriptAsAsync();
            return;
        }

        SaveEditorText(CurrentScriptPath);
    }

    private async Task SaveScriptAsAsync()
    {
        var suggestedName = HasSelectedScriptPath() ? Path.GetFileName(CurrentScriptPath) : $"{UntitledScriptText}.ecs";
        var file = await _dialogService.SaveFileAsync("另存为", "ecs",
        [
            new FilePickerFileType("ECS脚本文件") { Patterns = ["*.ecs"] },
            new FilePickerFileType("文本文件") { Patterns = ["*.txt"] },
            new FilePickerFileType("所有文件") { Patterns = ["*"] }
        ], suggestedName);

        if (file == null)
            return;

        CurrentScriptPath = file;
        SaveEditorText(CurrentScriptPath);

        var dir = Path.GetDirectoryName(CurrentScriptPath);
        if (!string.IsNullOrEmpty(dir))
        {
            _projectDirectoryPath = dir;
            _fileTreeViewModel.LoadDirectory(dir);
        }
    }

    private void CloseScript()
    {
        EditorText = "";
        CurrentScriptPath = NoScriptPathText;
        SelectedEditorTab = 0;
    }

    private void NewScript()
    {
        EditorText = "";
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

    private void SaveEditorText(string path)
    {
        File.WriteAllText(path, EditorText, new UTF8Encoding(false));
        _logService.AddLog($"已保存脚本: {path}");
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

        // 初始化内嵌编辑器
        InitializeEmbeddedEditor(path);
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
    /// 格式化当前脚本（委托给 ToolCallService，供 FormatScriptCommand 绑定）。
    /// </summary>
    private Task<string> FormatScriptAsync() => _toolCallService.FormatScriptAsync();

    /// <summary>
    /// 请求主窗口弹出打开项目目录对话框。
    /// </summary>
    public event Action? OpenFolderDialogRequested;

    /// <summary>
    /// 请求主窗口切换代码折叠显示状态。
    /// </summary>
    public event Action<bool>? FoldingVisibilityChanged;

    private void InitializeEmbeddedEditor(string filePath)
    {
        EmbeddedEditorInitializeRequested?.Invoke(filePath);
    }

    private void ConnectNintendoSwitch()
    {
        if (!string.IsNullOrEmpty(SelectedSerialPort))
        {
            if (IsNintendoSwitchConnected)
            {
                _deviceService.Disconnect();
                IsNintendoSwitchConnected = false;
                NintendoSwitchStatus = "未连接";
                NintendoSwitchButtonText = "连接单片机";
                _logService.AddLog("单片机已断开连接");
                return;
            }

            IsConnectingNintendoSwitch = true;
            NintendoSwitchStatus = "连接中...";
            _logService.AddLog($"准备连接单片机({SelectedSerialPort})...");

            var port = SelectedSerialPort;
            Task.Run(() =>
            {
                var ok = _deviceService.TryConnect(port);

                Dispatcher.UIThread.Post(() =>
                {
                    IsConnectingNintendoSwitch = false;
                    if (ok)
                    {
                        IsNintendoSwitchConnected = true;
                        NintendoSwitchStatus = $"已连接{port}";
                        NintendoSwitchButtonText = "断开连接";
                        _logService.AddLog($"单片机 ({port}) 连接成功");
                    }
                    else
                    {
                        NintendoSwitchStatus = "连接失败";
                    }
                });
            });
        }
    }

    private void AutoConnectNintendoSwitch()
    {
        if (IsNintendoSwitchConnected || IsConnectingNintendoSwitch) return;

        IsConnectingNintendoSwitch = true;
        NintendoSwitchStatus = "自动连接中...";
        _logService.AddLog("开始自动扫描串口...");

        Task.Run(() =>
        {
            var connectedPort = _deviceService.AutoConnect();
            Dispatcher.UIThread.Post(() =>
            {
                IsConnectingNintendoSwitch = false;
                if (connectedPort != null)
                {
                    IsNintendoSwitchConnected = true;
                    SelectedSerialPort = connectedPort;
                    NintendoSwitchStatus = $"已连接{connectedPort}";
                    NintendoSwitchButtonText = "断开连接";
                    _logService.AddLog($"自动连接成功: {connectedPort}");
                }
                else
                {
                    NintendoSwitchStatus = "自动连接失败";
                    _logService.AddLog("自动连接失败，未找到可用设备");
                }
            });
        });
    }

    private void ConnectCaptureSource()
    {
        if (!string.IsNullOrEmpty(SelectedCaptureSource))
        {
            if (IsCaptureSourceConnected)
            {
                _captureService.Disconnect();
                IsCaptureSourceConnected = false;
                CaptureSourceStatus = "未连接";
                CaptureSourceButtonText = "连接视频源";
                _logService.AddLog("视频源已断开连接");
                return;
            }

            IsConnectingCaptureSource = true;
            CaptureSourceStatus = "连接中...";
            var sourceName = SelectedCaptureSource ?? "";
            _logService.AddLog($"准备打开视频源({sourceName})...");

            Task.Run(() =>
            {
                try
                {
                    var ok = _captureService.TryConnect(sourceName);
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnectingCaptureSource = false;
                        if (ok)
                        {
                            IsCaptureSourceConnected = true;
                            CaptureSourceStatus = "已连接";
                            CaptureSourceButtonText = "关闭视频源";
                            _logService.AddLog($"视频源 ({sourceName}) 已连接");

                            // 自动显示监视器
                            ShowMonitor();
                        }
                        else
                        {
                            CaptureSourceStatus = "连接失败";
                            _logService.AddLog("视频源打开失败");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnectingCaptureSource = false;
                        CaptureSourceStatus = "连接失败";
                        _logService.AddLog($"视频源连接异常: {ex.Message}");
                    });
                }
            });
        }
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
        SaveUserSettings();
    }

    partial void OnIsIdleTwoColumnLayoutSelectedChanged(bool value)
    {
        if (value)
            IsIdleThreeColumnLayoutSelected = false;
        SaveUserSettings();
    }

    partial void OnIsRunningThreeColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningTwoColumnLayoutSelected = false;
            IsRunningOneColumnLayoutSelected = false;
        }
        SaveUserSettings();
    }

    partial void OnIsRunningTwoColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningThreeColumnLayoutSelected = false;
            IsRunningOneColumnLayoutSelected = false;
        }
        SaveUserSettings();
    }

    partial void OnIsRunningOneColumnLayoutSelectedChanged(bool value)
    {
        if (value)
        {
            IsRunningThreeColumnLayoutSelected = false;
            IsRunningTwoColumnLayoutSelected = false;
        }
        SaveUserSettings();
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
            return !relativePath.StartsWith("..", StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relativePath);
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

    partial void OnSelectedCaptureTypeChanged(string value)
    {
        _captureService.CaptureType = NormalizeCaptureType(value);
        SaveUserSettings();
    }

    partial void OnIsAutoCompletionEnabledChanged(bool value)
    {
        SaveUserSettings();
    }

    partial void OnAutoSwitchLayoutEnabledChanged(bool value)
    {
        SaveUserSettings();
    }

    partial void OnAutoSwitchColorSchemeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualColorSchemeEnabled));

        if (value)
            ApplySystemColorScheme();

        SaveUserSettings();
    }

    partial void OnShowFoldingChanged(bool value)
    {
        FoldingVisibilityChanged?.Invoke(value);
        SaveUserSettings();
    }

    partial void OnIsHighResolutionTimingEnabledChanged(bool value)
    {
        _scriptService.HighResolutionTiming = value;
        SaveUserSettings();
    }

    partial void OnWelcomeTextChanged(string value)
    {
        RefreshWelcomeConsole();
        SaveUserSettings();
    }

    partial void OnSelectedEditorTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsTextEditorTabSelected));
        OnPropertyChanged(nameof(IsTagEditorTabSelected));
        OnPropertyChanged(nameof(IsUserConfigTabSelected));
        OnPropertyChanged(nameof(IsFeatureCenterTabSelected));
        OnPropertyChanged(nameof(IsCardEditorHeaderVisible));
    }

    partial void OnIsCaptureSourceConnectedChanged(bool value)
    {
        // 同步更新标签编辑器的视频源连接状态
        if (TagEditorViewModel != null)
        {
            TagEditorViewModel.IsCaptureConnected = value;
        }
    }

    private void OnTagEditorOpenFileRequested()
    {
        _ = OpenTagEditorImageFileAsync();
    }

    private void OnTagEditorCaptureScreenshot()
    {
        CaptureScreenshotForTagEditor();
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
            using var mat = _captureService.GetMatFrame();
            if (mat == null || mat.Empty())
            {
                _logService.AddLog("标签测试失败：无法获取视频帧");
                return;
            }

            var label = TagEditorViewModel.Label;
            var result = label.Search(mat, out double matchDegree, "");

            // 裁剪匹配位置的 ROI 作为结果图
            Bitmap? resultBitmap = null;
            if (result.Count > 0)
            {
                var pt = result[0];
                int roiX = label.RangeX + pt.X;
                int roiY = label.RangeY + pt.Y;
                int roiW = label.TargetWidth;
                int roiH = label.TargetHeight;

                // 裁剪区域限制在帧范围内
                roiX = Math.Clamp(roiX, 0, mat.Width);
                roiY = Math.Clamp(roiY, 0, mat.Height);
                roiW = Math.Clamp(roiW, 0, mat.Width - roiX);
                roiH = Math.Clamp(roiH, 0, mat.Height - roiY);

                if (roiW > 0 && roiH > 0)
                {
                    using var roi = new Mat(mat, new Rect(roiX, roiY, roiW, roiH));
                    var roiBytes = roi.ToBytes(".png");
                    resultBitmap = new Bitmap(new MemoryStream(roiBytes));
                }
            }

            // 更新匹配度显示和结果图
            TagEditorViewModel.SetTestResult(matchDegree, resultBitmap);

            var status = result.Count > 0 ? "匹配成功" : "未匹配";
            _logService.AddLog($"标签测试 [{label.name}]: {status}, 匹配度 {matchDegree:F1}%");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"标签测试失败: {ex.Message}");
        }
    }

    private async Task OpenTagEditorImageFileAsync()
    {
        try
        {
            var cacheDir = EasyCon.Core.Config.AppPaths.CaptureCacheDir;
            var files = await _dialogService.OpenFilesAsync("选择图片文件",
            [
                new FilePickerFileType("图片文件") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ], cacheDir);

            if (files.Count > 0 && TagEditorViewModel != null)
                TagEditorViewModel.LoadImageFromFile(files[0]);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开文件对话框失败: {ex.Message}");
        }
    }

    private void CaptureScreenshotForTagEditor()
    {
        if (!_captureService.IsConnected)
        {
            _logService.AddLog("请先连接视频源");
            return;
        }

        try
        {
            using var mat = _captureService.GetMatFrame();
            if (mat == null || mat.Empty())
            {
                _logService.AddLog("截图失败：无法获取视频帧");
                return;
            }

            // 将Mat编码为字节数组，然后转换为Bitmap
            var imageBytes = mat.ToBytes(".png");
            var bitmap = new global::Avalonia.Media.Imaging.Bitmap(new MemoryStream(imageBytes));
            TagEditorViewModel?.SetScreenshot(bitmap);
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

    private void ConnectController()
    {
        if (IsControllerConnected)
        {
            DisconnectController();
            return;
        }

        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        IsConnectingController = true;
        ControlSourceStatus = "连接中...";
        _logService.AddLog($"正在连接手柄 ({SelectedControlSource})...");

        var sourceName = SelectedControlSource ?? "";
        Task.Run(() =>
        {
            var ok = _controllerService.TryConnect(sourceName);

            Dispatcher.UIThread.Post(() =>
            {
                IsConnectingController = false;
                if (ok)
                {
                    IsControllerConnected = true;
                    ControlSourceStatus = "已连接";
                    ControllerButtonText = "断开手柄";
                    UpdateEditKeyMappingEnabled();
                    _logService.AddLog($"手柄 ({sourceName}) 连接成功");
                }
                else
                {
                    ControlSourceStatus = "连接失败";
                    _logService.AddLog($"手柄 ({sourceName}) 连接失败");
                }
            });
        });
    }

    private void DisconnectController()
    {
        _controllerService.Disconnect();
        IsControllerConnected = false;
        ControlSourceStatus = "未连接";
        ControllerButtonText = "开启映射";
        UpdateEditKeyMappingEnabled();
        _logService.AddLog("手柄已断开连接");
    }

    private void EditKeyMapping()
    {
        _windowService.ShowKeyMappingWindow();
    }

    private void UpdateEditKeyMappingEnabled()
    {
        IsEditKeyMappingEnabled = SelectedControlSource == "键盘" && !IsControllerConnected;
    }

    partial void OnSelectedControlSourceChanged(string? value)
    {
        UpdateEditKeyMappingEnabled();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStartRecordEnabled));
        OnPropertyChanged(nameof(IsStopRecordEnabled));
    }

    partial void OnShowDebugInfoChanged(bool value)
    {
        _deviceService.ShowDebugInfo = value;
        SaveUserSettings();
    }

    private void RunScript()
    {
        if (_scriptService.IsRunning)
        {
            _scriptService.Stop();
            return;
        }

        if (!HasSelectedScriptPath())
        {
            _scriptService.RunFromContent(EditorText);
            return;
        }

        _scriptService.Run(CurrentScriptPath);
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
    /// 主窗口关闭时调用，关闭所有子窗口和监视器。
    /// </summary>
    public void OnMainWindowClosing()
    {
        _welcomeTimer.Stop();
        _welcomeTimer.Dispose();

        // 关闭嵌入式监视器
        if (_monitorViewModel != null)
        {
            _monitorViewModel.Close();
            _monitorViewModel = null;
        }

        // 释放控制器资源（SDL3 事件循环等）
        _controllerService.Dispose();
    }

    private void RemoteRun()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        if (_deviceService.RemoteStart())
        {
            _logService.AddLog("远程运行成功");
        }
        else
        {
            _logService.AddLog("远程运行失败");
        }
    }

    private void RemoteStop()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        if (_deviceService.RemoteStop())
        {
            _logService.AddLog("远程停止成功");
        }
        else
        {
            _logService.AddLog("远程停止失败");
        }
    }

    private async Task CompileFlashAsync()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        if (string.IsNullOrWhiteSpace(EditorText))
        {
            _logService.AddLog("没有可烧录的脚本");
            return;
        }

        _logService.AddLog("开始编译...");

        // 编译脚本
        if (!await _scriptService.CompileAsync(EditorText, HasSelectedScriptPath() ? CurrentScriptPath : null))
        {
            _logService.AddLog("编译失败，无法烧录");
            return;
        }

        // 组装为字节码
        var bytes = await _scriptService.BuildAsync(true);
        if (bytes == null || bytes.Length == 0)
        {
            _logService.AddLog("编译结果为空，无法烧录");
            return;
        }

        // 检查固件版本
        var version = _deviceService.GetVersion();
        if (version != 0x45)
        {
            _logService.AddLog($"固件版本不匹配 (当前: 0x{version:X2}，需要: 0x45)，请先更新固件");
            return;
        }

        // 烧录
        _logService.AddLog($"正在烧录 ({bytes.Length} 字节)...");
        if (_deviceService.Flash(bytes))
        {
            _logService.AddLog("烧录成功");
        }
        else
        {
            _logService.AddLog("烧录失败");
        }
    }

    private void ClearFlash()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        _logService.AddLog("正在清除烧录...");
        // 使用空字节数组清除烧录
        if (_deviceService.Flash(Array.Empty<byte>()))
        {
            _logService.AddLog("清除烧录成功");
        }
        else
        {
            _logService.AddLog("清除烧录失败");
        }
    }

    private async Task GenerateFirmwareAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorText))
        {
            _logService.AddLog("没有可生成固件的脚本");
            return;
        }

        _logService.AddLog("开始编译...");

        // 编译脚本
        if (!await _scriptService.CompileAsync(EditorText, HasSelectedScriptPath() ? CurrentScriptPath : null))
        {
            _logService.AddLog("编译失败，无法生成固件");
            return;
        }

        // 组装为字节码
        var bytes = await _scriptService.BuildAsync(false);
        if (bytes == null || bytes.Length == 0)
        {
            _logService.AddLog("编译结果为空，无法生成固件");
            return;
        }

        _logService.AddLog($"正在生成固件 ({SelectedFirmware})...");

        try
        {
            // 检查固件目录
            var firmwarePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Firmware");
            if (!Directory.Exists(firmwarePath))
            {
                _logService.AddLog("固件目录不存在，请确认程序Firmware目录下是否有对应固件文件");
                return;
            }

            // 查找对应的固件文件
            var firmwareFile = GetFirmwareFile(firmwarePath, SelectedFirmware);
            if (firmwareFile == null)
            {
                _logService.AddLog($"未找到 {SelectedFirmware} 对应的固件文件");
                return;
            }

            // 读取固件模板并写入脚本
            var hexContent = File.ReadAllText(firmwareFile);
            var outputFileName = Path.GetFileNameWithoutExtension(firmwareFile) + "+Script" + Path.GetExtension(firmwareFile);
            var outputPath = Path.Combine(Environment.CurrentDirectory, outputFileName);

            // 使用HexWriter写入脚本到固件
            var resultHex = EasyCon.Script.Asm.HexWriter.WriteHex(hexContent, bytes, 924, 0x45);
            File.WriteAllText(outputPath, resultHex);

            _logService.AddLog($"固件已生成: {outputPath}");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"生成固件失败: {ex.Message}");
        }
    }

    private static string? GetFirmwareFile(string firmwarePath, string coreName)
    {
        var dir = new DirectoryInfo(firmwarePath);
        if (!dir.Exists) return null;

        var max = 0;
        string? filename = null;
        foreach (var fi in dir.GetFiles("*.hex"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                fi.Name,
                $@"^{coreName} v(\d+)\.hex$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (m.Success)
            {
                var ver = int.Parse(m.Groups[1].Value);
                if (ver > max)
                {
                    max = ver;
                    filename = fi.FullName;
                }
            }
        }
        return filename;
    }

    private void StartRecord()
    {
        if (IsRecording)
        {
            _logService.AddLog("脚本录制已在进行中");
            return;
        }

        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        if (!IsControllerConnected)
        {
            _logService.AddLog("请先连接虚拟手柄");
            return;
        }

        _logService.AddLog("开始录制脚本");
        var device = _deviceService.GetDevice();
        device.StartRecord();
        IsRecording = true;
    }

    private void StopRecord()
    {
        if (!IsNintendoSwitchConnected)
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
}