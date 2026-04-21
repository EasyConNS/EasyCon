using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Views;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using Window = Avalonia.Controls.Window;
using WindowState = Avalonia.Controls.WindowState;

namespace EasyCon2.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly IScriptService _scriptService;
    private readonly IControllerService _controllerService;
    private readonly StringBuilder _logBuilder = new();
    private const int MaxLogLength = 100_000;
    private Window? _monitorWindow;

    // 窗口标题（含版本号）
    [ObservableProperty]
    private string _windowTitle;

    // 当前脚本路径
    [ObservableProperty]
    private string _currentScriptPath = "未选择脚本";

    // 日志输出
    [ObservableProperty]
    private string _logOutput = "";

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

    // 运行脚本相关属性
    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _runButtonText = "运行";

    [ObservableProperty]
    private string _runTimeDisplay = "00:00:00";

    private System.Timers.Timer _runTimer = new System.Timers.Timer(500);
    private DateTime _runStartTime;

    // 日志工具条相关属性
    [ObservableProperty]
    private bool _isLogToolbarVisible = false;

    // 打开脚本命令
    public ICommand OpenScriptCommand { get; }

    // 连接模块命令
    public ICommand ConnectNintendoSwitchCommand { get; }
    public ICommand AutoConnectNintendoSwitchCommand { get; }
    public ICommand ConnectCaptureSourceCommand { get; }
    public ICommand ConnectControllerCommand { get; }
    public ICommand EditKeyMappingCommand { get; }
    public ICommand RunScriptCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenMonitorCommand { get; }
    public ICommand DropFileCommand { get; }

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

        // 订阅日志事件（LogService 已批量合并，此处每 100ms 最多触发一次）
        _logService.LogAppended += text =>
        {
            if (text == null)
            {
                _logBuilder.Clear();
                LogOutput = "";
            }
            else
            {
                _logBuilder.Append(text);
                if (_logBuilder.Length > MaxLogLength)
                    _logBuilder.Remove(0, _logBuilder.Length - MaxLogLength / 2);
                LogOutput = _logBuilder.ToString();
            }
        };

        // 订阅设备外部断开事件
        _deviceService.ConnectionLost += () =>
        {
            if (!IsNintendoSwitchConnected) return;
            IsNintendoSwitchConnected = false;
            NintendoSwitchStatus = "已断开";
            NintendoSwitchButtonText = "连接单片机";
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

        // 初始化命令
        OpenScriptCommand = new RelayCommand<Window>(OpenScript);
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
        OpenMonitorCommand = new RelayCommand(OpenMonitor);
        DropFileCommand = new RelayCommand<string>(DropFile);

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
        _logBuilder.Append("欢迎使用 EasyCon2!\n");
        LogOutput = _logBuilder.ToString();
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

    private async void OpenScript(Window? window)
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
            CurrentScriptPath = file.Path.LocalPath;
            _logService.AddLog($"打开了脚本文件: {CurrentScriptPath}");
        }
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

    public void OpenScriptFromPath(string path)
    {
        CurrentScriptPath = path;
        _logService.AddLog($"打开了脚本文件: {CurrentScriptPath}");
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
            _controllerService.Disconnect();
            IsControllerConnected = false;
            ControlSourceStatus = "未连接";
            ControllerButtonText = "开启映射";
            UpdateEditKeyMappingEnabled();
            _logService.AddLog("手柄已断开连接");
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

    private void RunScript()
    {
        if (_scriptService.IsRunning)
        {
            _scriptService.Stop();
            return;
        }

        if (string.IsNullOrEmpty(CurrentScriptPath) || CurrentScriptPath == "未选择脚本")
        {
            _logService.AddLog("请先选择脚本文件");
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
    /// 主窗口关闭时调用，关闭所有子窗口。
    /// </summary>
    public void OnMainWindowClosing()
    {
        if (_monitorWindow != null)
        {
            if (_monitorWindow.DataContext is MonitorWindowViewModel vm)
                vm.Close();
            _monitorWindow.Close();
            _monitorWindow = null;
        }
    }

    private void OpenMonitor()
    {
        if (!IsCaptureSourceConnected)
        {
            _logService.AddLog("请先连接视频源才能打开监视器");
            return;
        }

        try
        {
            // 单例模式：如果监视器窗口已存在且未关闭，则激活到前台
            if (_monitorWindow != null)
            {
                if (_monitorWindow.WindowState == WindowState.Minimized)
                    _monitorWindow.WindowState = WindowState.Normal;
                _monitorWindow.Activate();
                return;
            }

            _monitorWindow = new MonitorWindow(_captureService);

            // 窗口关闭时清空引用
            _monitorWindow.Closed += (s, e) =>
            {
                _monitorWindow = null;
            };

            // 显示窗口（非模态，不影响主界面）
            // StartMonitoring 由 MonitorWindow 在 Opened 事件中自动调用，
            // 确保 DPI 先初始化再渲染首帧
            _monitorWindow.Show();

            _logService.AddLog("监视器窗口已打开");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开监视器失败: {ex.Message}");
        }
    }
}