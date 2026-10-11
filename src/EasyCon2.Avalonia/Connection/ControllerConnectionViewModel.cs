using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Connection;

/// <summary>
/// 虚拟手柄（控制源）连接编排：控制源列表、开启/断开映射、按键映射入口。
/// </summary>
public partial class ControllerConnectionViewModel : ObservableObject
{
    private readonly IControllerService _controllerService;
    private readonly IWindowService _windowService;
    private readonly ILogService _logService;
    private readonly IUiDispatcher _ui;
    private readonly Func<bool> _isSwitchConnected;

    [ObservableProperty]
    private ObservableCollection<ControlSourceInfo> _controlSourceOptions = new();

    [ObservableProperty]
    private ControlSourceInfo? _selectedControlSource;

    [ObservableProperty]
    private string _controlSourceStatus = L10n.T("Text.Status.NotConnected");

    // 当前状态对应的资源键（语言切换后 RefreshLocalizedTexts 据此重算同状态的译文）
    private string _controlStatusKey = "Text.Status.NotConnected";

    [ObservableProperty]
    private bool _isConnectingController = false;

    [ObservableProperty]
    private bool _isControllerConnected = false;

    [ObservableProperty]
    private string _controllerButtonText = L10n.T("Text.Btn.StartMapping");

    [ObservableProperty]
    private bool _isEditKeyMappingEnabled = true;

    public ICommand ConnectControllerCommand { get; }
    public ICommand RefreshControlSourcesCommand { get; }
    public ICommand EditKeyMappingCommand { get; }

    /// <summary>记录状态键并刷新状态文本（语言切换时按同状态重算译文）。</summary>
    private void SetControlStatus(string key)
    {
        _controlStatusKey = key;
        ControlSourceStatus = L10n.T(key);
    }

    /// <summary>语言切换后重算快照文本：状态保持当前状态不变，按钮按连接状态取对应文案。</summary>
    public void RefreshLocalizedTexts()
    {
        ControlSourceStatus = L10n.T(_controlStatusKey);
        ControllerButtonText = L10n.T(IsControllerConnected
            ? "Text.Btn.DisconnectController"
            : "Text.Btn.StartMapping");
    }

    public ControllerConnectionViewModel(IControllerService controllerService, IWindowService windowService, ILogService logService, Func<bool> isSwitchConnected, IUiDispatcher? uiDispatcher = null)
    {
        _controllerService = controllerService;
        _windowService = windowService;
        _logService = logService;
        _ui = uiDispatcher ?? SynchronousUiDispatcher.Instance;
        _isSwitchConnected = isSwitchConnected;

        ConnectControllerCommand = new RelayCommand(ConnectController);
        RefreshControlSourcesCommand = new RelayCommand(RefreshControlSources);
        EditKeyMappingCommand = new RelayCommand(EditKeyMapping);

        RefreshControlSources();

        // 订阅手柄热插拔事件
        _controllerService.AvailableSourcesChanged += () => _ui.Post(RefreshControlSources);

        // 订阅控制器外部断开事件（VPad 中键/ESC 退出）
        _controllerService.Disconnected += () => _ui.Post(() =>
        {
            IsControllerConnected = false;
            SetControlStatus("Text.Status.NotConnected");
            ControllerButtonText = L10n.T("Text.Btn.StartMapping");
            UpdateEditKeyMappingEnabled();
            _logService.AddLog(L10n.T("Text.Msg.ControllerDisconnected"));
        });
    }

    private void ConnectController()
    {
        // 连接在后台 Task.Run 执行，期间按钮仍可点击，必须挡住重入
        if (IsConnectingController)
            return;

        if (IsControllerConnected)
        {
            DisconnectController();
            return;
        }

        if (!_isSwitchConnected())
        {
            _logService.AddLog(L10n.T("Text.Msg.ConnectFirst"));
            return;
        }

        IsConnectingController = true;
        SetControlStatus("Text.Status.Connecting");
        var selectedSource = SelectedControlSource;
        _logService.AddLog(string.Format(L10n.T("Text.Msg.ConnectControllerPrepare"), selectedSource?.DisplayName ?? ""));

        var sourceId = selectedSource?.SourceId ?? "";
        Task.Run(() =>
        {
            try
            {
                var ok = _controllerService.TryConnect(sourceId);

                Dispatcher.UIThread.Post(() =>
                {
                    IsConnectingController = false;
                    if (ok)
                    {
                        IsControllerConnected = true;
                        SetControlStatus("Text.Status.Connected");
                        ControllerButtonText = L10n.T("Text.Btn.DisconnectController");
                        UpdateEditKeyMappingEnabled();
                        _logService.AddLog(string.Format(L10n.T("Text.Msg.ConnectControllerOk"), selectedSource?.DisplayName));
                    }
                    else
                    {
                        SetControlStatus("Text.Status.ConnectFailed");
                        _logService.AddLog(string.Format(L10n.T("Text.Msg.ConnectControllerFail"), selectedSource?.DisplayName));
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsConnectingController = false;
                    SetControlStatus("Text.Status.ConnectFailed");
                });
                _logService.AddLog(string.Format(L10n.T("Text.Msg.ConnectControllerError"), ex.Message));
            }
        });
    }

    private void DisconnectController()
    {
        _controllerService.Disconnect();
        IsControllerConnected = false;
        SetControlStatus("Text.Status.NotConnected");
        ControllerButtonText = L10n.T("Text.Btn.StartMapping");
        UpdateEditKeyMappingEnabled();
        _logService.AddLog(L10n.T("Text.Msg.ControllerDisconnected"));
    }

    private void RefreshControlSources()
    {
        var oldSelected = SelectedControlSource;
        ControlSourceOptions = new ObservableCollection<ControlSourceInfo>(_controllerService.GetAvailableSources());
        SelectedControlSource = ControlSourceOptions.FirstOrDefault();
        if (oldSelected != null && ControlSourceOptions.Contains(oldSelected))
            SelectedControlSource = oldSelected;
    }

    private void EditKeyMapping()
    {
        _windowService.Show(WindowKeys.KeyMapping);
    }

    private void UpdateEditKeyMappingEnabled()
    {
        IsEditKeyMappingEnabled = SelectedControlSource?.SourceId == ControllerService.KeyboardSourceId && !IsControllerConnected;
    }

    partial void OnSelectedControlSourceChanged(ControlSourceInfo? value)
    {
        UpdateEditKeyMappingEnabled();
    }
}