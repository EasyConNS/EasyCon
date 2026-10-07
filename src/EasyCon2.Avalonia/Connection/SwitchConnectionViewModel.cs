using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Connection;

/// <summary>
/// 单片机（串口）连接编排：串口列表、连接/自动连接、远程运行与停止。
/// </summary>
public partial class SwitchConnectionViewModel : ObservableObject
{
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logService;
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    private ObservableCollection<string> _serialPortOptions = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty]
    private string _nintendoSwitchStatus = L10nBridge.T("Text.Status.NotConnected");

    [ObservableProperty]
    private bool _isConnectingNintendoSwitch = false;

    [ObservableProperty]
    private bool _isNintendoSwitchConnected = false;

    [ObservableProperty]
    private string _nintendoSwitchButtonText = L10nBridge.T("Text.Btn.ConnectSwitch");

    /// <summary>设备在连接状态下从外部断开（拔线等），供父级联动复位录制等状态。</summary>
    public event Action? DeviceLost;

    public ICommand ConnectNintendoSwitchCommand { get; }
    public ICommand AutoConnectNintendoSwitchCommand { get; }
    public ICommand RefreshSerialPortsCommand { get; }
    public ICommand RemoteRunCommand { get; }
    public ICommand RemoteStopCommand { get; }

    public SwitchConnectionViewModel(IDeviceService deviceService, ILogService logService, IUiDispatcher uiDispatcher)
    {
        _deviceService = deviceService;
        _logService = logService;
        _ui = uiDispatcher;

        ConnectNintendoSwitchCommand = new RelayCommand(ConnectNintendoSwitch);
        AutoConnectNintendoSwitchCommand = new RelayCommand(AutoConnectNintendoSwitch);
        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPorts);
        RemoteRunCommand = new AsyncRelayCommand(RemoteRunAsync);
        RemoteStopCommand = new AsyncRelayCommand(RemoteStopAsync);

        RefreshSerialPorts();

        // 订阅设备外部断开事件
        _deviceService.ConnectionLost += () =>
        {
            if (!IsNintendoSwitchConnected) return;
            IsNintendoSwitchConnected = false;
            NintendoSwitchStatus = L10nBridge.T("Text.Status.Disconnected");
            NintendoSwitchButtonText = L10nBridge.T("Text.Btn.ConnectSwitch");
            DeviceLost?.Invoke();
        };
    }

    private void ConnectNintendoSwitch()
    {
        // 连接在后台 Task.Run 执行，期间按钮仍可点击，必须挡住重入（防并发打开串口）
        if (IsConnectingNintendoSwitch)
            return;

        if (!string.IsNullOrEmpty(SelectedSerialPort))
        {
            if (IsNintendoSwitchConnected)
            {
                _deviceService.Disconnect();
                IsNintendoSwitchConnected = false;
                NintendoSwitchStatus = "未连接";
                NintendoSwitchButtonText = L10nBridge.T("Text.Btn.ConnectSwitch");
                _logService.AddLog(L10nBridge.T("Text.Msg.SwitchLost"));
                return;
            }

            IsConnectingNintendoSwitch = true;
            NintendoSwitchStatus = L10nBridge.T("Text.Status.Connecting");
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.ConnectSwitchPrepare"), SelectedSerialPort));

            var port = SelectedSerialPort;
            Task.Run(() =>
            {
                try
                {
                    var ok = _deviceService.TryConnect(port);

                    _ui.Post(() =>
                    {
                        IsConnectingNintendoSwitch = false;
                        if (ok)
                        {
                            IsNintendoSwitchConnected = true;
                            NintendoSwitchStatus = $"已连接{port}";
                            NintendoSwitchButtonText = L10nBridge.T("Text.Btn.DisconnectSwitch");
                            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.ConnectSwitchOk"), port));
                        }
                        else
                        {
                            NintendoSwitchStatus = "连接失败";
                        }
                    });
                }
                catch (Exception ex)
                {
                    _ui.Post(() =>
                    {
                        IsConnectingNintendoSwitch = false;
                        NintendoSwitchStatus = "连接失败";
                    });
                    _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.ConnectSwitchError"), ex.Message));
                }
            });
        }
    }

    private void AutoConnectNintendoSwitch()
    {
        if (IsNintendoSwitchConnected || IsConnectingNintendoSwitch) return;

        IsConnectingNintendoSwitch = true;
        NintendoSwitchStatus = L10nBridge.T("Text.Status.AutoConnecting");
        _logService.AddLog(L10nBridge.T("Text.Msg.ScanStart"));

        Task.Run(() =>
        {
            try
            {
                var connectedPort = _deviceService.AutoConnect();
                _ui.Post(() =>
                {
                    IsConnectingNintendoSwitch = false;
                    if (connectedPort != null)
                    {
                        IsNintendoSwitchConnected = true;
                        SelectedSerialPort = connectedPort;
                        NintendoSwitchStatus = $"已连接{connectedPort}";
                        NintendoSwitchButtonText = L10nBridge.T("Text.Btn.DisconnectSwitch");
                        _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AutoConnectOk"), connectedPort));
                    }
                    else
                    {
                        NintendoSwitchStatus = L10nBridge.T("Text.Status.AutoConnectFailed");
                        _logService.AddLog(L10nBridge.T("Text.Msg.AutoConnectFail"));
                    }
                });
            }
            catch (Exception ex)
            {
                _ui.Post(() =>
                {
                    IsConnectingNintendoSwitch = false;
                    NintendoSwitchStatus = L10nBridge.T("Text.Status.AutoConnectFailed");
                });
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AutoConnectError"), ex.Message));
            }
        });
    }

    private void RefreshSerialPorts()
    {
        var ports = _deviceService.GetAvailablePorts();
        var oldSelected = SelectedSerialPort;
        SerialPortOptions = new ObservableCollection<string>(ports);
        SelectedSerialPort = oldSelected != null && SerialPortOptions.Contains(oldSelected)
            ? oldSelected
            : SerialPortOptions.FirstOrDefault();
    }

    private async Task RemoteRunAsync()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        try
        {
            // RemoteStart 底层是带超时的同步串口 I/O，放后台执行避免卡 UI
            var ok = await Task.Run(_deviceService.RemoteStart);
            _logService.AddLog(ok ? L10nBridge.T("Text.Msg.RemoteRunOk") : L10nBridge.T("Text.Msg.RemoteRunFail"));
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.RemoteRunError"), ex.Message));
        }
    }

    private async Task RemoteStopAsync()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog("请先连接单片机");
            return;
        }

        try
        {
            var ok = await Task.Run(_deviceService.RemoteStop);
            _logService.AddLog(ok ? L10nBridge.T("Text.Msg.RemoteStopOk") : L10nBridge.T("Text.Msg.RemoteStopFail"));
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.RemoteStopError"), ex.Message));
        }
    }
}