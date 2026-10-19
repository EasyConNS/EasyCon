using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Localization;
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
    private HashSet<string> _availablePorts = [];
    [ObservableProperty]
    private bool _selectionUnavailable;
    public string SelectionNotice => SelectionUnavailable ? "所选通信端口当前不可用，请刷新后重新选择" : "";
    partial void OnSelectedSerialPortChanged(string? value)
    {
        SelectionUnavailable = value != null && !_availablePorts.Contains(value);
        OnPropertyChanged(nameof(SelectionNotice));
    }

    [ObservableProperty]
    private string _nintendoSwitchStatus = L10nBridge.T("Text.Status.NotConnected");

    // 当前状态对应的资源键，供语言切换后重算同状态的译文
    private string _switchStatusKey = "Text.Status.NotConnected";

    [ObservableProperty]
    private bool _isConnectingNintendoSwitch = false;

    [ObservableProperty]
    private bool _isNintendoSwitchConnected = false;

    [ObservableProperty]
    private string _nintendoSwitchButtonText = L10nBridge.T("Text.Btn.ConnectSwitch");

    /// <summary>设备在连接状态下从外部断开（拔线等），供父级联动复位录制等状态。</summary>
    public event Action? DeviceLost;

    /// <summary>记录状态键并刷新状态文本，语言切换时按同状态重算译文。</summary>
    private void SetSwitchStatus(string key)
    {
        _switchStatusKey = key;
        NintendoSwitchStatus = L10nBridge.T(key);
    }

    /// <summary>语言切换后重算快照文本：状态保持当前状态不变，按钮按连接状态取对应文案。</summary>
    public void RefreshLocalizedTexts()
    {
        SetSwitchStatus(_switchStatusKey);
        NintendoSwitchButtonText = L10nBridge.T(IsNintendoSwitchConnected
            ? "Text.Btn.DisconnectSwitch"
            : "Text.Btn.ConnectSwitch");
    }

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

        ConnectNintendoSwitchCommand = new AsyncRelayCommand(ConnectNintendoSwitch);
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
            SetSwitchStatus("Text.Status.Disconnected");
            NintendoSwitchButtonText = L10nBridge.T("Text.Btn.ConnectSwitch");
            DeviceLost?.Invoke();
        };

        // 连接状态可能被外部改变（Flow/agent 经设备桥连接）：以服务真实状态同步面板，
        // 保证「画布/agent 连接成功 = GUI 面板显示已连接」
        _deviceService.ConnectionStateChanged += () => _ui.Post(SyncFromService);
    }

    /// <summary>以 <see cref="IDeviceService.IsConnected"/> 为唯一事实源刷新面板状态。</summary>
    private void SyncFromService()
    {
        var connected = _deviceService.IsConnected;
        IsNintendoSwitchConnected = connected;
        SetSwitchStatus(connected ? "Text.Status.Connected" : "Text.Status.NotConnected");
        NintendoSwitchButtonText = connected
            ? L10nBridge.T("Text.Btn.DisconnectSwitch")
            : L10nBridge.T("Text.Btn.ConnectSwitch");
    }

    private async Task ConnectNintendoSwitch()
    {
        // 连接在后台 Task.Run 执行，期间按钮仍可点击，必须挡住重入（防并发打开串口）
        if (IsConnectingNintendoSwitch)
            return;

        if (!string.IsNullOrEmpty(SelectedSerialPort))
        {
            if (IsNintendoSwitchConnected)
            {
                IsConnectingNintendoSwitch = true;
                try { await Task.Run(_deviceService.Disconnect); }
                finally { IsConnectingNintendoSwitch = false; }
                IsNintendoSwitchConnected = false;
                SetSwitchStatus("Text.Status.NotConnected");
                NintendoSwitchButtonText = L10nBridge.T("Text.Btn.ConnectSwitch");
                _logService.AddLog(L10nBridge.T("Text.Msg.SwitchLost"));
                return;
            }

            IsConnectingNintendoSwitch = true;
            SetSwitchStatus("Text.Status.Connecting");
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
                            SetSwitchStatus("Text.Status.Connected");
                            NintendoSwitchButtonText = L10nBridge.T("Text.Btn.DisconnectSwitch");
                            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.ConnectSwitchOk"), port));
                        }
                        else
                        {
                            SetSwitchStatus("Text.Status.ConnectFailed");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _ui.Post(() =>
                    {
                        IsConnectingNintendoSwitch = false;
                        SetSwitchStatus("Text.Status.ConnectFailed");
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
        SetSwitchStatus("Text.Status.AutoConnecting");
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
                        SetSwitchStatus("Text.Status.Connected");
                        NintendoSwitchButtonText = L10nBridge.T("Text.Btn.DisconnectSwitch");
                        _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AutoConnectOk"), connectedPort));
                    }
                    else
                    {
                        SetSwitchStatus("Text.Status.AutoConnectFailed");
                        _logService.AddLog(L10nBridge.T("Text.Msg.AutoConnectFail"));
                    }
                });
            }
            catch (Exception ex)
            {
                _ui.Post(() =>
                {
                    IsConnectingNintendoSwitch = false;
                    SetSwitchStatus("Text.Status.AutoConnectFailed");
                });
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AutoConnectError"), ex.Message));
            }
        });
    }

    private void RefreshSerialPorts()
    {
        var ports = _deviceService.GetAvailablePorts();
        _availablePorts = [.. ports];
        var oldSelected = SelectedSerialPort;
        SerialPortOptions = new ObservableCollection<string>(ports);
        if (oldSelected != null && !SerialPortOptions.Contains(oldSelected)) SerialPortOptions.Add(oldSelected);
        SelectedSerialPort = oldSelected;
        OnSelectedSerialPortChanged(oldSelected);
    }

    private async Task RemoteRunAsync()
    {
        if (!IsNintendoSwitchConnected)
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.ConnectFirst"));
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
            _logService.AddLog(L10nBridge.T("Text.Msg.ConnectFirst"));
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