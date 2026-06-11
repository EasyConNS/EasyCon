using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.TagEditor;
using EasyCon2.Avalonia.Core.Terminal;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Views;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;
using Window = Avalonia.Controls.Window;
using WindowState = Avalonia.Controls.WindowState;

namespace EasyCon2.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string NoScriptPathText = "未选择脚本";
    private const string UntitledScriptText = "未命名脚本";
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly IScriptService _scriptService;
    private readonly IControllerService _controllerService;
    private readonly AnsiParser _ansiParser = new();
    private Window? _espConfigWindow;
    private MonitorViewModel? _monitorViewModel;
    private readonly FileTreeViewModel _fileTreeViewModel;
    private string? _projectDirectoryPath;
    private ConfigState _userConfig = new();
    private bool _isLoadingUserSettings;

    // 窗口标题（含版本号）
    [ObservableProperty]
    private string _windowTitle;

    // 当前脚本路径
    [ObservableProperty]
    private string _currentScriptPath = NoScriptPathText;

    // 日志输出行集合（TerminalControl 绑定）
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

    // 监视器视图
    [ObservableProperty]
    private MonitorView? _monitorView;

    // 文件树视图
    [ObservableProperty]
    private FileTreeView? _fileTreeView;

    // 编辑器标签页索引（0=文本编辑, 1=标签编辑, 2=用户配置, 3=功能中心）
    [ObservableProperty]
    private int _selectedEditorTab = 0;

    // 标签编辑器 ViewModel
    [ObservableProperty]
    private TagEditorViewModel? _tagEditorViewModel;

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

    // 显示代码折叠
    [ObservableProperty]
    private bool _showFolding = true;

    [ObservableProperty]
    private bool _isHighResolutionTimingEnabled = false;

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
    public ICommand ToggleMonitorPauseCommand { get; }

    public ICommand ShowScriptSyntaxCommand { get; }
    public ICommand OpenAiAgentCommand { get; }

    public ICommand ToggleMonitorVisibilityCommand { get; }
    public ICommand SelectColorSchemeCommand { get; }
    public ICommand RestoreDefaultLayoutCommand { get; }

    // 刷新数据源命令
    public ICommand RefreshSerialPortsCommand { get; }
    public ICommand RefreshCaptureSourcesCommand { get; }
    public ICommand RefreshControlSourcesCommand { get; }

    public MainWindowViewModel(ILogService logService, IDeviceService deviceService, ICaptureService captureService, IScriptService scriptService, IControllerService controllerService)
    {
        // 窗口标题
        var ver = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plusIdx = ver.IndexOf('+');
        if (plusIdx > 0) ver = ver[..plusIdx];
        WindowTitle = $"伊机控 EasyCon v{ver}  QQ群:946057081";

        _logService = logService;
        _deviceService = deviceService;
        _captureService = captureService;
        _scriptService = scriptService;
        _controllerService = controllerService;

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
        var fileTreeView = new FileTreeView { DataContext = _fileTreeViewModel };
        FileTreeView = fileTreeView;

        // 初始化监视器视图
        InitializeMonitorView();

        // 初始化标签编辑器（默认空实例，始终可用）
        InitializeTagEditor();

        // 订阅日志事件（LogService 已批量合并，此处每 100ms 最多触发一次）
        _logService.LogAppended += (text, color) =>
        {
            if (text == null)
            {
                LogLines.Clear();
            }
            else
            {
                // 按换行拆分，每行生成一个 TerminalLine
                var rawLines = text.Split('\n');
                foreach (var rawLine in rawLines)
                {
                    if (string.IsNullOrEmpty(rawLine)) continue;
                    LogLines.Add(_ansiParser.ParseLine(rawLine));
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
            IsRunning = running;
            RunButtonText = running ? "停止" : "运行";
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
        OpenScriptCommand = new AsyncRelayCommand<Window>(OpenScriptAsync);
        SaveScriptCommand = new AsyncRelayCommand<Window>(SaveScriptAsync);
        SaveScriptAsCommand = new AsyncRelayCommand<Window>(SaveScriptAsAsync);
        CloseScriptCommand = new RelayCommand(CloseScript);
        FormatScriptCommand = new AsyncRelayCommand(FormatScriptAsync);
        OpenEditorCommand = new RelayCommand(OpenEditor, CanOpenEditor);
        ConnectNintendoSwitchCommand = new RelayCommand(ConnectNintendoSwitch);
        AutoConnectNintendoSwitchCommand = new RelayCommand(AutoConnectNintendoSwitch);
        ConnectCaptureSourceCommand = new RelayCommand(ConnectCaptureSource);
        ConnectControllerCommand = new RelayCommand(ConnectController);
        EditKeyMappingCommand = new RelayCommand<Window>(EditKeyMapping);
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
        OpenAlertConfigCommand = new RelayCommand<Window>(OpenAlertConfig);
        ToggleMonitorPauseCommand = new RelayCommand(ToggleMonitorPause);
        ShowScriptSyntaxCommand = new RelayCommand(ShowScriptSyntax);
        OpenAiAgentCommand = new RelayCommand(OpenAiAgent);
        ToggleMonitorVisibilityCommand = new RelayCommand(ToggleMonitorVisibility);
        SelectColorSchemeCommand = new RelayCommand<string>(SelectColorScheme);
        RestoreDefaultLayoutCommand = new RelayCommand(RestoreDefaultLayout);

        LoadUserSettings();

        // 初始化示例数据
        InitializeSampleData();

        _runTimer.Elapsed += (s, e) =>
        {
            var elapsed = DateTime.Now - _runStartTime;
            Dispatcher.UIThread.Post(() =>
            {
                RunTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");
            });
        };

        // 添加一些初始日志
        LogLines.Add(_ansiParser.ParseLine("欢迎使用 EasyCon2!"));
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
            AutoSwitchLayoutEnabled = _userConfig.AutoSwitchLayoutEnabled;
            ApplySavedLayoutSettings(_userConfig);

            var colorSchemeName = NormalizeColorSchemeName(_userConfig.ColorSchemeName, _userConfig.DarkMode);
            ThemeManager.Instance.ApplyColorScheme(colorSchemeName);
        }
        catch (Exception ex)
        {
            _userConfig = new ConfigState();
            ThemeManager.Instance.ApplyColorScheme(ThemeManager.IndustrialGraySchemeName);
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
        _userConfig.AutoSwitchLayoutEnabled = AutoSwitchLayoutEnabled;
        _userConfig.IsIdleThreeColumnLayoutSelected = IsIdleThreeColumnLayoutSelected;
        _userConfig.IsIdleTwoColumnLayoutSelected = IsIdleTwoColumnLayoutSelected;
        _userConfig.IsRunningThreeColumnLayoutSelected = IsRunningThreeColumnLayoutSelected;
        _userConfig.IsRunningTwoColumnLayoutSelected = IsRunningTwoColumnLayoutSelected;
        _userConfig.IsRunningOneColumnLayoutSelected = IsRunningOneColumnLayoutSelected;
        _userConfig.ColorSchemeName = ThemeManager.Instance.SelectedColorSchemeName;
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
            ThemeManager.IndustrialGraySchemeName => ThemeManager.IndustrialGraySchemeName,
            ThemeManager.WarmToneSchemeName => ThemeManager.WarmToneSchemeName,
            ThemeManager.DarkModeSchemeName => ThemeManager.DarkModeSchemeName,
            _ => legacyDarkMode ? ThemeManager.DarkModeSchemeName : ThemeManager.IndustrialGraySchemeName
        };
    }

    private void OnOpenProjectRequested()
    {
        OpenFolderDialogRequested?.Invoke();
    }

    private void SelectColorScheme(string? colorSchemeName)
    {
        if (string.IsNullOrWhiteSpace(colorSchemeName))
            return;

        ThemeManager.Instance.ApplyColorScheme(colorSchemeName);
        SaveUserSettings();
        _logService.AddLog($"已切换配色: {colorSchemeName}");
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

    private void InitializeMonitorView()
    {
        try
        {
            // 创建监视器视图和视图模型
            _monitorViewModel = new MonitorViewModel(_captureService);

            var monitorView = new MonitorView();
            monitorView.DataContext = _monitorViewModel;
            MonitorView = monitorView;

            // 监视器默认可见
            IsMonitorVisible = true;
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

    private async Task OpenScriptAsync(Window? window)
    {
        if (window == null)
            return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开脚本文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ECS脚本文件") { Patterns = ["*.ecs"] },
                new FilePickerFileType("文本文件") { Patterns = ["*.txt"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            var file = files[0];
            OpenScriptFromPath(file.Path.LocalPath);
        }
    }

    private void OnOpenScriptRequested(Window? window)
    {
        _ = OpenScriptAsync(window);
    }

    private void OnOpenProjectFolderRequested(Window? window)
    {
        _ = OpenProjectFolderAsync(window);
    }

    private void OnSaveScriptRequested(Window? window)
    {
        _ = SaveScriptAsync(window);
    }

    private void OnSaveScriptAsRequested(Window? window)
    {
        _ = SaveScriptAsAsync(window);
    }

    private async Task OpenProjectFolderAsync(Window? window)
    {
        if (window == null)
            return;

        // 使用当前项目目录或用户文档目录作为默认位置
        var startPath = _projectDirectoryPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var startFolder = await window.StorageProvider.TryGetFolderFromPathAsync(startPath);

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "打开项目目录",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        if (folders.Count > 0)
            OpenProjectFromDirectory(folders[0].Path.LocalPath);
    }

    private async Task SaveScriptAsync(Window? window)
    {
        if (!HasSelectedScriptPath())
        {
            await SaveScriptAsAsync(window);
            return;
        }

        SaveEditorText(CurrentScriptPath);
    }

    private async Task SaveScriptAsAsync(Window? window)
    {
        if (window == null)
            return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为",
            SuggestedFileName = HasSelectedScriptPath() ? Path.GetFileName(CurrentScriptPath) : $"{UntitledScriptText}.ecs",
            DefaultExtension = "ecs",
            FileTypeChoices =
            [
                new FilePickerFileType("ECS脚本文件") { Patterns = ["*.ecs"] },
                new FilePickerFileType("文本文件") { Patterns = ["*.txt"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ]
        });

        if (file == null)
            return;

        CurrentScriptPath = file.Path.LocalPath;
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

    private async Task FormatScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorText))
            return;

        if (await _scriptService.CompileAsync(EditorText, null))
        {
            EditorText = _scriptService.GetFormattedCode();
            _logService.AddLog("格式化完成");
        }
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
            // 创建监视器视图和视图模型（如果尚未创建）
            if (_monitorViewModel == null)
            {
                _monitorViewModel = new MonitorViewModel(_captureService);
            }

            if (MonitorView == null)
            {
                var monitorView = new MonitorView();
                monitorView.DataContext = _monitorViewModel;
                MonitorView = monitorView;
            }

            // 显示监视器
            IsMonitorVisible = true;

            // 启动监视
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
        if (_espConfigWindow != null)
        {
            if (_espConfigWindow.WindowState == WindowState.Minimized)
                _espConfigWindow.WindowState = WindowState.Normal;
            _espConfigWindow.Activate();
            return;
        }

        try
        {
            var vm = new ViewModels.ESPConfigViewModel(_deviceService, _logService);
            _espConfigWindow = new ESPConfigWindow { DataContext = vm };
            _espConfigWindow.Closed += (_, _) => _espConfigWindow = null;
            _espConfigWindow.Show();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开手柄设置失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void OpenAlertConfig(Window? window)
    {
        try
        {
            var alertConfigWindow = new AlertConfigWindow();
            if (window != null)
                alertConfigWindow.ShowDialog(window);
            else
                alertConfigWindow.Show();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开推送配置失败: {ex.Message}");
        }
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
        var textBox = new TextBox
        {
            Text = Resources.scriptdoc,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            FontFamily = new global::Avalonia.Media.FontFamily("Microsoft YaHei UI, Consolas"),
            FontSize = 13,
            Padding = new global::Avalonia.Thickness(12)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);

        var window = new Window
        {
            Title = "脚本语法",
            Width = 820,
            Height = 640,
            MinWidth = 520,
            MinHeight = 360,
            Content = textBox
        };
        window.Show();
    }

    private void OpenAiAgent()
    {
        _logService.AddLog("AI Agent 功能待接入");
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
        OpenTagEditorImageFileAsync();
    }

    private void OnTagEditorCaptureScreenshot()
    {
        CaptureScreenshotForTagEditor();
    }

    private async Task OpenTagEditorImageFileAsync()
    {
        // 使用当前主窗口作为父窗口
        var mainWindow = App.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (mainWindow == null) return;

        try
        {
            // 使用 AppPaths.CaptureCacheDir 作为默认目录
            var cacheDir = EasyCon.Core.Config.AppPaths.CaptureCacheDir;
            var startFolder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(cacheDir);

            var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择图片文件",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片文件") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] },
                    new FilePickerFileType("所有文件") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0 && TagEditorViewModel != null)
            {
                var filePath = files[0].Path.LocalPath;
                TagEditorViewModel.LoadImageFromFile(filePath);
            }
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

    private void EditKeyMapping(Window? window)
    {
        if (window == null) return;
        var keyMappingWindow = new KeyMappingWindow();
        keyMappingWindow.ShowDialog(window);
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
        if (_espConfigWindow != null)
        {
            _espConfigWindow.Close();
            _espConfigWindow = null;
        }

        // 关闭嵌入式监视器
        if (_monitorViewModel != null)
        {
            _monitorViewModel.Close();
            _monitorViewModel = null;
        }

        MonitorView = null;

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
