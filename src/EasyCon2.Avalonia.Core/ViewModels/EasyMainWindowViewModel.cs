using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.Services;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;

namespace EasyCon2.Avalonia.Core.ViewModels;

public partial class EasyMainWindowViewModel : ObservableObject
{
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly IScriptService _scriptService;
    private readonly IFileService _fileService;
    private readonly IFirmwareService _firmwareService;
    private readonly ICaptureService _captureService;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;

    private readonly StringBuilder _logBuilder = new();
    private const int MaxLogLength = 100_000;
    private const int MaxLogEntries = 5000;
    private const string ScriptFilter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*";

    public BoardInfo[] SupportedBoards => _firmwareService.GetSupportedBoards();

    // Expose services for view code-behind wiring
    public IFileService FileService => _fileService;
    public IDeviceService DeviceService => _deviceService;
    public IScriptService ScriptService => _scriptService;
    public IDialogService Dialog => _dialogService;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    // Events for view code-behind to handle editor operations
    public event Action? OpenSearchRequested;
    public event Action? FindNextRequested;
    public event Action? ToggleCommentRequested;
    public event Action<bool>? ShowFoldingChanged;

    #region Observable Properties

    [ObservableProperty]
    private string _windowTitle = "";

    [ObservableProperty]
    private string _scriptTitle = "未命名脚本";

    [ObservableProperty]
    private string _runButtonText = "运行脚本";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _canRun = true;

    [ObservableProperty]
    private string _runButtonColor = "#2ECC71";

    [ObservableProperty]
    private string _timerText = "00:00:00";

    [ObservableProperty]
    private bool _timerActive;

    public ISolidColorBrush TimerForeground => TimerActive ? Brushes.Lime : Brushes.White;
    partial void OnTimerActiveChanged(bool value) => OnPropertyChanged(nameof(TimerForeground));

    [ObservableProperty]
    private ObservableCollection<string> _serialPorts = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty]
    private string _serialStatus = "单片机未连接";

    [ObservableProperty]
    private string _serialStatusColor = "#696969";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _connectButtonsEnabled = true;

    [ObservableProperty]
    private string _captureStatus = "采集卡未连接";

    [ObservableProperty]
    private string _captureStatusColor = "#696969";

    [ObservableProperty]
    private ObservableCollection<string> _captureSourceOptions = new();

    [ObservableProperty]
    private string? _selectedCaptureSource;

    [ObservableProperty]
    private string _captureSourceButtonText = "连接视频源";

    [ObservableProperty]
    private string _captureSourceStatusText = "未连接";

    [ObservableProperty]
    private ObservableCollection<CaptureTypeInfo> _captureTypes = new();

    [ObservableProperty]
    private CaptureTypeInfo? _selectedCaptureType;

    [ObservableProperty]
    private int _selectedBoardIndex;

    [ObservableProperty]
    private string _recordButtonText = "录制脚本";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _canPauseRecord;

    [ObservableProperty]
    private string _statusText = "LOG";

    [ObservableProperty]
    private bool _showDebugInfo;

    [ObservableProperty]
    private bool _flashAutoRun = true;

    [ObservableProperty]
    private bool _showFolding = true;

    [ObservableProperty]
    private bool _enableAutoCompletion;

    [ObservableProperty]
    private double _editorFontSize = 14;

    #endregion

    public EasyMainWindowViewModel(
        ILogService logService,
        IDeviceService deviceService,
        IScriptService scriptService,
        IFileService fileService,
        IFirmwareService firmwareService,
        ICaptureService captureService,
        IConfigService configService,
        IDialogService dialogService)
    {
        _logService = logService;
        _deviceService = deviceService;
        _scriptService = scriptService;
        _fileService = fileService;
        _firmwareService = firmwareService;
        _captureService = captureService;
        _configService = configService;
        _dialogService = dialogService;

        var ver = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "";
        var plusIdx = ver.IndexOf('+');
        if (plusIdx > 0) ver = ver[..plusIdx];
        WindowTitle = $"伊机控 EasyCon v{ver}  QQ群:946057081";

        _enableAutoCompletion = _configService.Config.EnableAutoCompletion;
        _editorFontSize = _configService.Config.EditorFontSize;

        SubscribeEvents();
        InitCaptureTypes();
        RefreshCaptureSources();
    }

    private void SubscribeEvents()
    {
        _logService.LogAppended += (text, color) =>
        {
            if (text == null)
            {
                _logBuilder.Clear();
                LogEntries.Clear();
                return;
            }

            _logBuilder.Append(text);
            if (_logBuilder.Length > MaxLogLength)
                _logBuilder.Remove(0, _logBuilder.Length - MaxLogLength / 2);

            var displayText = text.TrimEnd('\n', '\r');
            if (displayText.Length > 0)
            {
                LogEntries.Add(new LogEntry(displayText, ColorToBrush(color)));
                while (LogEntries.Count > MaxLogEntries)
                    LogEntries.RemoveAt(0);
            }
        };

        _scriptService.IsRunningChanged += running =>
        {
            IsRunning = running;
            RunButtonText = running ? "终止脚本" : "运行脚本";
            RunButtonColor = running ? "#E74C3C" : "#2ECC71";
            CanRun = true;
            TimerActive = running;
            if (!running)
                TimerText = "00:00:00";
        };

        _scriptService.LogPrint += (message, color) =>
        {
            _logService.AddLog(message, color);
        };

        _deviceService.StatusChanged += connected =>
        {
            SerialStatus = connected ? "单片机已连接" : "单片机未连接";
            SerialStatusColor = connected ? "#32CD32" : "#FFFFFF";
        };

        _captureService.CaptureStatusChanged += () =>
        {
            CaptureStatus = _captureService.IsOpened ? "采集卡已连接" : "采集卡未连接";
            CaptureStatusColor = _captureService.IsOpened ? "#32CD32" : "#FFFFFF";
        };

        _fileService.FileNameChanged += _ => UpdateScriptTitle();
        _fileService.ScriptModifiedChanged += () => UpdateScriptTitle();
    }

    #region Settings Side Effects

    partial void OnEnableAutoCompletionChanged(bool value)
    {
        _configService.UpdateConfig(c => c.EnableAutoCompletion = value);
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        _configService.UpdateConfig(c => c.EditorFontSize = value);
    }

    partial void OnShowDebugInfoChanged(bool value)
    {
        _deviceService.ShowDebugInfo = value;
    }

    partial void OnFlashAutoRunChanged(bool value)
    {
        _configService.UpdateConfig(c => c.AutoRunAfterFlash = value);
    }

    partial void OnShowFoldingChanged(bool value)
    {
        ShowFoldingChanged?.Invoke(value);
    }

    #endregion

    private static IBrush ColorToBrush(string? colorName) => colorName switch
    {
        "Lime" => Brushes.Lime,
        "Orange" => Brushes.Orange,
        "OrangeRed" => Brushes.OrangeRed,
        "Yellow" => Brushes.Yellow,
        "Gray" => Brushes.Gray,
        _ => Brushes.White,
    };

    public void UpdateScriptTitle()
    {
        var name = _fileService.CurrentFileName == null ? "未命名脚本" : Path.GetFileName(_fileService.CurrentFileName);
        ScriptTitle = _fileService.IsModified ? $"{name}(已编辑)" : name;
    }

    private void InitCaptureTypes()
    {
        var types = _captureService.GetCaptureTypes();
        CaptureTypes = new ObservableCollection<CaptureTypeInfo>(types.Select(t => new CaptureTypeInfo(t.name, t.value)));
        var configType = _configService.Config.CaptureType;
        SelectedCaptureType = CaptureTypes.FirstOrDefault(t => t.Name == configType) ?? CaptureTypes.FirstOrDefault();
    }

    public void RefreshSerialPorts()
    {
        var ports = _deviceService.GetAvailablePorts();
        SerialPorts = new ObservableCollection<string>(ports);
        SelectedSerialPort = SerialPorts.FirstOrDefault();
    }

    #region File Commands

    [RelayCommand]
    private void FileNew()
    {
        if (_fileService.IsModified)
        {
            var r = _dialogService.ShowQuestion("文件已编辑，是否保存？");
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) FileSave();
        }
        _fileService.FileClose();
        StatusText = "新建完毕";
    }

    [RelayCommand]
    private void FileOpen()
    {
        var path = _dialogService.ShowOpenFileDialog("打开", ScriptFilter);
        if (path == null) return;
        if (_fileService.IsModified)
        {
            var r = _dialogService.ShowQuestion("文件已编辑，是否保存？");
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) FileSave();
        }
        _fileService.FileOpen(path);
    }

    [RelayCommand]
    private void FileSave()
    {
        if (_fileService.CurrentFileName == null)
        {
            FileSaveAs();
            return;
        }
        _fileService.FileSave();
    }

    [RelayCommand]
    private void FileSaveAs()
    {
        var path = _dialogService.ShowSaveFileDialog("另存为", ScriptFilter);
        if (path == null) return;
        _fileService.SetFileName(path);
        _fileService.FileSave();
    }

    [RelayCommand]
    private void FileClose()
    {
        if (_fileService.IsModified)
        {
            var r = _dialogService.ShowQuestion("文件已编辑，是否保存？");
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) FileSave();
        }
        _fileService.FileClose();
    }

    #endregion

    #region Script Commands

    [RelayCommand]
    private void RunStopScript()
    {
        CanRun = false;
        if (!IsRunning)
        {
            if (_fileService.CurrentFileName != null && _fileService.IsModified)
            {
                _logService.AddLog("您还没有保存脚本，请先保存后再运行");
                CanRun = true;
                return;
            }
            _scriptService.Run();
        }
        else
        {
            _scriptService.Stop();
        }
        CanRun = true;
    }

    [RelayCommand]
    private async Task Compile()
    {
        if (await _scriptService.Compile(_fileService.GetText(), _fileService.CurrentFileName))
        {
            _fileService.SetText(_scriptService.GetFormattedCode());
        }
        else
        {
            StatusText = "编译失败";
        }
    }

    [RelayCommand]
    private async Task ScriptRun()
    {
        if (!IsRunning)
            _scriptService.Run();
    }

    #endregion

    #region Device Commands

    [RelayCommand]
    private void RemoteStart()
    {
        if (!_deviceService.IsConnected) return;
        StatusText = _deviceService.RemoteStart() ? "远程运行已开始" : "远程运行失败";
    }

    [RelayCommand]
    private void RemoteStop()
    {
        if (!_deviceService.IsConnected) return;
        StatusText = _deviceService.RemoteStop() ? "远程运行已停止" : "远程停止失败";
    }

    [RelayCommand]
    private async Task Flash()
    {
        if (!_deviceService.IsConnected) return;
        if (_deviceService.GetVersion() < 0x45)
        {
            StatusText = "需要更新固件";
            return;
        }
        if (!await _scriptService.Compile(_fileService.GetText(), _fileService.CurrentFileName))
            return;
        await _firmwareService.Flash(SelectedBoardIndex, FlashAutoRun, _fileService.GetText(), _fileService.CurrentFileName);
    }

    [RelayCommand]
    private async Task FlashClear()
    {
        if (!_deviceService.IsConnected)
        {
            StatusText = "还未准备好烧录";
            return;
        }
        await _firmwareService.FlashClear();
    }

    [RelayCommand]
    private async Task GenerateFirmware()
    {
        await _firmwareService.GenerateFirmware(SelectedBoardIndex, _fileService.GetText(), _fileService.CurrentFileName);
    }

    [RelayCommand]
    private void AutoConnect()
    {
        if (IsConnecting) return;
        IsConnecting = true;
        ConnectButtonsEnabled = false;
        StatusText = "尝试连接...";

        Task.Run(() =>
        {
            var port = _deviceService.AutoConnect();
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = port != null ? "连接成功" : "连接失败";
                if (port != null) SelectedSerialPort = port;
                IsConnecting = false;
                ConnectButtonsEnabled = true;
            });
        });
    }

    [RelayCommand]
    private void ManualConnect()
    {
        if (string.IsNullOrEmpty(SelectedSerialPort)) return;
        IsConnecting = true;
        ConnectButtonsEnabled = false;
        StatusText = "尝试连接...";

        Task.Run(() =>
        {
            var ok = _deviceService.TryConnect(SelectedSerialPort);
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = ok ? "连接成功" : "连接失败";
                IsConnecting = false;
                ConnectButtonsEnabled = true;
            });
        });
    }

    #endregion

    #region Capture Source Commands

    [RelayCommand]
    private void RefreshCaptureSources()
    {
        var sources = _captureService.GetCaptureSources();
        var prev = SelectedCaptureSource;
        CaptureSourceOptions = new ObservableCollection<string>(sources);
        SelectedCaptureSource = CaptureSourceOptions.Contains(prev ?? "") ? prev : CaptureSourceOptions.FirstOrDefault();
    }

    [RelayCommand]
    private void ConnectCaptureSource()
    {
        if (_captureService.IsOpened)
        {
            _captureService.Disconnect();
            CaptureSourceButtonText = "连接视频源";
            CaptureSourceStatusText = "未连接";
            CaptureStatusColor = "#696969";
            return;
        }

        if (string.IsNullOrEmpty(SelectedCaptureSource)) return;
        CaptureSourceStatusText = "连接中...";
        Task.Run(() =>
        {
            var ok = _captureService.TryConnect(SelectedCaptureSource);
            Dispatcher.UIThread.Post(() =>
            {
                CaptureSourceStatusText = ok ? "已连接" : "连接失败";
                CaptureSourceButtonText = ok ? "关闭视频源" : "连接视频源";
                CaptureStatusColor = ok ? "#32CD32" : "#696969";
            });
        });
    }

    [RelayCommand]
    private void OpenCaptureConsole()
    {
        _dialogService.ShowCaptureConsole();
    }

    #endregion

    #region Other Commands

    [RelayCommand]
    private void OpenSearch() => OpenSearchRequested?.Invoke();

    [RelayCommand]
    private void FindNext() => FindNextRequested?.Invoke();

    [RelayCommand]
    private void ToggleComment() => ToggleCommentRequested?.Invoke();

    [RelayCommand]
    private void Exit() => _dialogService.RequestClose();

    [RelayCommand]
    private void ClearLog() => _logService.Clear();

    [RelayCommand]
    private async Task CheckUpdate()
    {
        var msg = await _configService.CheckForUpdate();
        if (msg != null) _dialogService.ShowMessage(msg, "检查更新");
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _dialogService.ShowMessage($"伊机控  QQ群:946057081\n\nCopyright © 2020. 铃落(Nukieberry)\nCopyright © 2021. elmagnifico\nCopyright © 2025. 卡尔(ca1e)", "关于");
    }

    [RelayCommand]
    private void ShowAlertConfig()
    {
        _dialogService.ShowAlertConfigDialog();
    }

    #endregion

    public void OnUITick()
    {
        if (_scriptService.StartTime != DateTime.MinValue)
        {
            var time = DateTime.Now - _scriptService.StartTime;
            TimerText = time.ToString(@"hh\:mm\:ss");
        }
    }

    partial void OnSelectedCaptureTypeChanged(CaptureTypeInfo? value)
    {
        if (value != null)
            _configService.UpdateConfig(c => c.CaptureType = value.Name);
    }
}

public class LogEntry
{
    public string Text { get; }
    public IBrush Foreground { get; }

    public LogEntry(string text, IBrush foreground)
    {
        Text = text;
        Foreground = foreground;
    }
}

public class CaptureTypeInfo
{
    public string Name { get; }
    public int Value { get; }
    public CaptureTypeInfo(string name, int value) { Name = name; Value = value; }
    public override string ToString() => Name;
}