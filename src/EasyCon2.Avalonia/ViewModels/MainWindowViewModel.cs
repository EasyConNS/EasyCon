using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyDevice;
using EC.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Window = Avalonia.Controls.Window;

namespace EC.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly ICaptureService _captureService;
    private readonly IScriptService _scriptService;

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
    public ICommand OpenKeyMappingCommand { get; }
    public ICommand RunScriptCommand { get; }
    public ICommand ClearLogCommand { get; }

    // 刷新数据源命令
    public ICommand RefreshSerialPortsCommand { get; }
    public ICommand RefreshCaptureSourcesCommand { get; }
    public ICommand RefreshControlSourcesCommand { get; }

    public MainWindowViewModel(ILogService logService, IDeviceService deviceService, ICaptureService captureService, IScriptService scriptService)
    {
        _logService = logService;
        _deviceService = deviceService;
        _captureService = captureService;
        _scriptService = scriptService;

        // 订阅日志事件
        _logService.LogAppended += text =>
        {
            if (text == null)
                LogOutput = "";
            else
                LogOutput += text;
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
        OpenKeyMappingCommand = new RelayCommand(OpenKeyMapping);
        RunScriptCommand = new RelayCommand(RunScript);
        ClearLogCommand = new RelayCommand(ClearLog);
        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPorts);
        RefreshCaptureSourcesCommand = new RelayCommand(RefreshCaptureSources);
        RefreshControlSourcesCommand = new RelayCommand(RefreshControlSources);

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
        LogOutput = "欢迎使用 EasyCon2!\n";
    }

    private void InitializeSampleData()
    {
        // 获取可用串口列表
        var ports = _deviceService.GetAvailablePorts();
        SerialPortOptions = new ObservableCollection<string>(ports);

        // 获取可用视频源列表
        var captureSources = _captureService.GetAvailableSources();
        CaptureSourceOptions = new ObservableCollection<string>(captureSources);

        ControlSourceOptions = new ObservableCollection<string> { "键盘", "Pro手柄" };

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
        ControlSourceOptions = new ObservableCollection<string> { "键盘", "Pro手柄" };
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
                var result = _deviceService.TryConnect(port);

                Dispatcher.UIThread.Post(() =>
                {
                    IsConnectingNintendoSwitch = false;
                    if (result == NintendoSwitch.ConnectResult.Success)
                    {
                        IsNintendoSwitchConnected = true;
                        NintendoSwitchStatus = $"已连接{port}";
                        NintendoSwitchButtonText = "断开连接";
                        _logService.AddLog($"单片机 ({port}) 连接成功");
                    }
                    else
                    {
                        NintendoSwitchStatus = "连接失败";
                        _logService.AddLog($"单片机连接失败: {result}");
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
            var ports = _deviceService.GetAvailablePorts();
            foreach (var port in ports)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    NintendoSwitchStatus = $"尝试 {port}...";
                });

                var result = _deviceService.TryConnect(port);
                if (result == NintendoSwitch.ConnectResult.Success)
                {
                    var connectedPort = port;
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnectingNintendoSwitch = false;
                        IsNintendoSwitchConnected = true;
                        SelectedSerialPort = connectedPort;
                        NintendoSwitchStatus = $"已连接{connectedPort}";
                        NintendoSwitchButtonText = "断开连接";
                        _logService.AddLog($"自动连接成功: {connectedPort}");
                    });
                    return;
                }

                Thread.Sleep(1000);
            }

            Dispatcher.UIThread.Post(() =>
            {
                IsConnectingNintendoSwitch = false;
                NintendoSwitchStatus = "自动连接失败";
                _logService.AddLog("自动连接失败，未找到可用设备");
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

    private void OpenKeyMapping()
    {
        _logService.AddLog($"打开按键映射配置: {SelectedControlSource}");
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
}