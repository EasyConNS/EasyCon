using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyScript;
using EasyDevice;
using OpenCvSharp;
using Window = Avalonia.Controls.Window;

namespace EC.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IOutputAdapter
{
    void IOutputAdapter.Print(string message, bool newline)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogOutput += (newline ? $"[{DateTime.Now:HH:mm:ss}] {message}\n" : message);
        });
    }

    void IOutputAdapter.Alert(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] [ALERT] {message}\n";
        });
    }

    private class NullGamePad : ICGamePad
    {
        public DelayType DelayMethod => DelayType.HighResolution;
        public void ClickButtons(GamePadKey key, int duration, CancellationToken token) { }
        public void PressButtons(GamePadKey key) { }
        public void ReleaseButtons(GamePadKey key) { }
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }
        public void SetStick(GamePadKey key, byte x, byte y) { }
        public void ChangeAmiibo(uint index) { }
    }
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

    // NintendoSwitch 单片机实例
    private NintendoSwitch _nintendoSwitch = new();

    // 视频源连接相关属性
    [ObservableProperty]
    private ObservableCollection<string> _captureSourceOptions = new();

    [ObservableProperty]
    private string? _selectedCaptureSource;

    [ObservableProperty]
    private string _captureSourceStatus = "未连接";

    [ObservableProperty]
    private bool _isConnectingCaptureSource = false;

    // OpenCVCapture 实例，初始化为null，只有实际连接时才new
    private OpenCVCapture? _capture = null;

    // 视频源下标映射（名字 -> 下标）
    private Dictionary<string, int> _captureSourceIndexMap = new();

    // 脚本运行相关
    private EasyRunner _runner = new();
    private CancellationTokenSource? _scriptCts;

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

    private System.Timers.Timer _runTimer = new System.Timers.Timer(1000);
    private DateTime _runStartTime;

    // 日志工具条相关属性
    [ObservableProperty]
    private bool _isLogToolbarVisible = false;

    // 打开脚本命令
    public ICommand OpenScriptCommand { get; }

    // 连接模块命令
    public ICommand ConnectNintendoSwitchCommand { get; }
    public ICommand ConnectCaptureSourceCommand { get; }
    public ICommand OpenKeyMappingCommand { get; }
    public ICommand RunScriptCommand { get; }
    public ICommand ClearLogCommand { get; }

    // 刷新数据源命令
    public ICommand RefreshSerialPortsCommand { get; }
    public ICommand RefreshCaptureSourcesCommand { get; }
    public ICommand RefreshControlSourcesCommand { get; }

    public MainWindowViewModel()
    {
        // 初始化命令
        OpenScriptCommand = new RelayCommand<Window>(OpenScript);
        ConnectNintendoSwitchCommand = new RelayCommand(ConnectNintendoSwitch);
        ConnectCaptureSourceCommand = new RelayCommand(ConnectCaptureSource);
        OpenKeyMappingCommand = new RelayCommand(OpenKeyMapping);
        RunScriptCommand = new RelayCommand(RunScript);
        ClearLogCommand = new RelayCommand(ClearLog);
        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPorts);
        RefreshCaptureSourcesCommand = new RelayCommand(RefreshCaptureSources);
        RefreshControlSourcesCommand = new RelayCommand(RefreshControlSources);

        // 初始化示例数据
        InitializeSampleData();

        //_nintendoSwitch.Log += (message) =>
        //{
        //    Dispatcher.UIThread.Post(() =>
        //    {
        //        LogOutput += $"[NS] {message}\n";
        //    });
        //};
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
        var ports = ECCore.GetDeviceNames();
        SerialPortOptions = new ObservableCollection<string>(ports);

        // 获取可用视频源列表，只显示名字，内部保存下标映射
        var captureSources = ECCore.GetCaptureSources().ToList();
        CaptureSourceOptions = new ObservableCollection<string>(captureSources);
        _captureSourceIndexMap = captureSources.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index);

        ControlSourceOptions = new ObservableCollection<string> { "键盘", "Pro手柄" };

        SelectedSerialPort = SerialPortOptions.FirstOrDefault();
        SelectedCaptureSource = CaptureSourceOptions.FirstOrDefault();
        SelectedControlSource = ControlSourceOptions.FirstOrDefault();

        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 已加载 {ports.Count} 个可用串口, {captureSources.Count} 个视频源\n";
    }

    private void RefreshSerialPorts()
    {
        var ports = ECCore.GetDeviceNames();
        var oldSelected = SelectedSerialPort;
        SerialPortOptions = new ObservableCollection<string>(ports);
        SelectedSerialPort = SerialPortOptions.FirstOrDefault();
        if (oldSelected != null && SerialPortOptions.Contains(oldSelected))
            SelectedSerialPort = oldSelected;
    }

    private void RefreshCaptureSources()
    {
        var captureSources = ECCore.GetCaptureSources().ToList();
        var oldSelected = SelectedCaptureSource;
        CaptureSourceOptions = new ObservableCollection<string>(captureSources);
        _captureSourceIndexMap = captureSources.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index);
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
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 打开了脚本文件: {CurrentScriptPath}\n";
        }
    }

    private void ConnectNintendoSwitch()
    {
        if (!string.IsNullOrEmpty(SelectedSerialPort))
        {
            if (IsNintendoSwitchConnected)
            {
                _nintendoSwitch?.Disconnect();
                IsNintendoSwitchConnected = false;
                NintendoSwitchStatus = "未连接";
                NintendoSwitchButtonText = "连接单片机";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] 单片机已断开连接\n";
                return;
            }

            IsConnectingNintendoSwitch = true;
            NintendoSwitchStatus = "连接中...";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 准备连接单片机({SelectedSerialPort})...\n";

            Task.Run(() =>
            {
                var result = _nintendoSwitch.TryConnect(SelectedSerialPort);

                Dispatcher.UIThread.Post(() =>
                {
                    IsConnectingNintendoSwitch = false;
                    if (result == NintendoSwitch.ConnectResult.Success)
                    {
                        IsNintendoSwitchConnected = true;
                        NintendoSwitchStatus = $"已连接{SelectedSerialPort}";
                        NintendoSwitchButtonText = "断开连接";
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 单片机 ({SelectedSerialPort}) 连接成功\n";
                    }
                    else
                    {
                        NintendoSwitchStatus = "连接失败";
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 单片机连接失败: {result}\n";
                    }
                });
            });
        }
    }

    private void ConnectCaptureSource()
    {
        if (!string.IsNullOrEmpty(SelectedCaptureSource))
        {
            if (_capture?.IsOpened??false)
            {
                _capture.Release();
                IsConnectingCaptureSource = false;
                CaptureSourceStatus = "未连接";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] 视频源已断开连接\n";
                return;
            }

            IsConnectingCaptureSource = true;
            CaptureSourceStatus = "连接中...";
            var sourceName = SelectedCaptureSource ?? "";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 准备打开视频源({sourceName})...\n";

            Task.Run(() =>
            {
                try
                {
                    int deviceId = _captureSourceIndexMap.TryGetValue(sourceName, out var idx) ? idx : 0;
                    int deviceType = (int)VideoCaptureAPIs.ANY;

                    _capture?.Dispose();
                    _capture = new OpenCVCapture();
                    if (!_capture.Open(deviceId, deviceType))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            IsConnectingCaptureSource = false;
                            CaptureSourceStatus = "连接失败";
                            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 视频源打开失败\n";
                        });
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnectingCaptureSource = false;
                        CaptureSourceStatus = "已连接";
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 视频源 ({sourceName}) 已连接\n";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnectingCaptureSource = false;
                        CaptureSourceStatus = "连接失败";
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 视频源连接异常: {ex.Message}\n";
                    });
                }
            });
        }
    }

    private void OpenKeyMapping()
    {
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 打开按键映射配置: {SelectedControlSource}\n";
    }

    private void RunScript()
    {
        if (IsRunning)
        {
            _runTimer.Stop();
            RunTimeDisplay = "00:00:00";
            _scriptCts?.Cancel();
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 正在停止脚本...\n";
            return;
        }

        if (string.IsNullOrEmpty(CurrentScriptPath) || CurrentScriptPath == "未选择脚本")
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 请先选择脚本文件\n";
            return;
        }

        IsRunning = true;
        RunButtonText = "停止";
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 开始运行脚本: {CurrentScriptPath}\n";

        _runStartTime = DateTime.Now;
        _runTimer.Start();

        _scriptCts = new CancellationTokenSource();
        var token = _scriptCts.Token;

        Task.Run(() =>
        {
            try
            {
                var scriptBasePath = Path.GetDirectoryName(CurrentScriptPath) ?? "";
                scriptBasePath = Path.GetFullPath(scriptBasePath);
                var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);

                var diag = _runner.Load(CurrentScriptPath, [.. label.Select(il => il.name)]);

                if (diag.HasErrors())
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsRunning = false;
                        RunButtonText = "运行";
                        foreach (var d in diag)
                        {
                            LogOutput += $"[{DateTime.Now:HH:mm:ss}] 编译失败: {d.Message} (行{d.Location.StartLine + 1})\n";
                        }
                    });
                    return;
                }

                if (_runner.HasKeyAction && (_nintendoSwitch == null || !_nintendoSwitch.IsConnected()))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsRunning = false;
                        RunButtonText = "运行";
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 错误: 脚本需要连接单片机\n";
                    });
                    return;
                }

                if (_runner.NeedILLoad && (_capture == null || !_capture.IsOpened))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsRunning = false;
                        RunButtonText = "运行";
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] 错误: 脚本需要连接视频源\n";
                    });
                    return;
                }

                ICGamePad pad = new NullGamePad();
                if (_runner.HasKeyAction)
                {
                    pad = new GamePadAdapter(_nintendoSwitch);
                }

                var externalGetters = label.ToDictionary(il => il.name, il => (Func<int>)(() =>
                {
                    if (_capture == null) throw new Exception("采集卡未连接");
                    il.Search(_capture.GetMatFrame(), out var md);
                    return (int)md;
                }));

                _runner.Run((IOutputAdapter)this, pad, externalGetters, token);
                Dispatcher.UIThread.Post(() =>
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] 脚本运行完成\n";
                });
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] 脚本已终止\n";
                });
            }
            catch (ScriptException ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] 运行出错: {ex.Message} (行{ex.Address})\n";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] 意外错误: {ex.Message}\n";
                });
            }
            finally
            {
                _runTimer?.Stop();
                Dispatcher.UIThread.Post(() =>
                {
                    IsRunning = false;
                    RunButtonText = "运行";
                    RunTimeDisplay = "00:00:00";
                });
            }
        }, token);
    }

    private void ClearLog()
    {
        LogOutput = "";
    }

    // 公共方法用于添加日志
    public void AddLog(string message)
    {
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }
}