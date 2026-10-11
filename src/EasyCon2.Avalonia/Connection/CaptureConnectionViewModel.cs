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
/// 视频源连接编排：源列表、采集 API 类型、连接/断开。
/// </summary>
public partial class CaptureConnectionViewModel : ObservableObject
{
    private readonly ICaptureService _captureService;
    private readonly ILogService _logService;
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    private ObservableCollection<string> _captureSourceOptions = new();

    [ObservableProperty]
    private string? _selectedCaptureSource;

    [ObservableProperty]
    private string _captureSourceStatus = L10nBridge.T("Text.Status.NotConnected");

    // 当前状态对应的资源键（语言切换后 RefreshLocalizedTexts 据此重算同状态的译文）
    private string _captureStatusKey = "Text.Status.NotConnected";

    [ObservableProperty]
    private bool _isConnectingCaptureSource = false;

    [ObservableProperty]
    private bool _isCaptureSourceConnected = false;

    [ObservableProperty]
    private string _captureSourceButtonText = L10nBridge.T("Text.Btn.ConnectCapture");

    [ObservableProperty]
    private ObservableCollection<string> _captureTypeOptions = new() { "ANY", "DSHOW", "MSMF", "DC1394" };

    [ObservableProperty]
    private string _selectedCaptureType = "ANY";

    partial void OnSelectedCaptureTypeChanged(string value)
    {
        _captureService.CaptureType = NormalizeCaptureType(value);
        CaptureTypeChanged?.Invoke();
    }

    /// <summary>连接成功（父级借此自动打开监视器）。</summary>
    public event Action? CaptureConnected;

    /// <summary>采集 API 类型变化（父级借此持久化用户配置）。</summary>
    public event Action? CaptureTypeChanged;

    /// <summary>记录状态键并刷新状态文本（语言切换时按同状态重算译文）。</summary>
    private void SetCaptureStatus(string key)
    {
        _captureStatusKey = key;
        CaptureSourceStatus = L10nBridge.T(key);
    }

    /// <summary>语言切换后重算快照文本：状态保持当前状态不变，按钮按连接状态取对应文案。</summary>
    public void RefreshLocalizedTexts()
    {
        CaptureSourceStatus = L10nBridge.T(_captureStatusKey);
        CaptureSourceButtonText = L10nBridge.T(IsCaptureSourceConnected
            ? "Text.Btn.DisconnectCapture"
            : "Text.Btn.ConnectCapture");
    }

    public ICommand ConnectCaptureSourceCommand { get; }
    public ICommand RefreshCaptureSourcesCommand { get; }

    public CaptureConnectionViewModel(ICaptureService captureService, ILogService logService, IUiDispatcher uiDispatcher)
    {
        _captureService = captureService;
        _logService = logService;
        _ui = uiDispatcher;

        ConnectCaptureSourceCommand = new AsyncRelayCommand(ConnectCaptureSource);
        RefreshCaptureSourcesCommand = new RelayCommand(RefreshCaptureSources);

        RefreshCaptureSources();

        // 订阅视频源外部断开事件
        _captureService.ConnectionLost += () =>
        {
            if (!IsCaptureSourceConnected) return;
            IsCaptureSourceConnected = false;
            SetCaptureStatus("Text.Status.Disconnected");
            CaptureSourceButtonText = L10nBridge.T("Text.Btn.ConnectCapture");
        };

        // 连接状态可能被外部改变（Flow/agent 经设备桥连接）：以服务真实状态同步面板，
        // 保证「画布/agent 连接成功 = GUI 面板显示已连接」
        _captureService.ConnectionStateChanged += () => _ui.Post(SyncFromService);
    }

    /// <summary>以 <see cref="ICaptureService.IsConnected"/> 为唯一事实源刷新面板状态。</summary>
    private void SyncFromService()
    {
        var connected = _captureService.IsConnected;
        if (IsCaptureSourceConnected == connected)
            return;
        IsCaptureSourceConnected = connected;
        SetCaptureStatus(connected ? "Text.Status.Connected" : "Text.Status.NotConnected");
        CaptureSourceButtonText = connected
            ? L10nBridge.T("Text.Btn.DisconnectCapture")
            : L10nBridge.T("Text.Btn.ConnectCapture");
        if (connected)
            CaptureConnected?.Invoke();   // 与手动连接一致：父级借此自动打开监视器
    }

    private async Task ConnectCaptureSource()
    {
        // 采集卡 native open 可达数秒，期间必须挡住重入：二次 TryConnect 会
        // Dispose 第一个 producer 后重开设备，部分采集卡会卡死在「连接中」
        if (IsConnectingCaptureSource)
            return;

        if (!string.IsNullOrEmpty(SelectedCaptureSource))
        {
            if (IsCaptureSourceConnected)
            {
                try
                {
                    // 采集循环可能阻塞在 Read 上，Dispose 最长等 5 秒，放后台避免冻结 UI
                    await _captureService.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.DisconnectCaptureError"), ex.Message));
                }
                IsCaptureSourceConnected = false;
                SetCaptureStatus("Text.Status.NotConnected");
                CaptureSourceButtonText = L10nBridge.T("Text.Btn.ConnectCapture");
                _logService.AddLog(L10nBridge.T("Text.Msg.CaptureDisconnected"));
                return;
            }

            IsConnectingCaptureSource = true;
            SetCaptureStatus("Text.Status.Connecting");
            var sourceName = SelectedCaptureSource ?? "";
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.OpenCapturePrepare"), sourceName));

            try
            {
                // 后台执行 + await：消灭 fire-and-forget（原 CS4014），命令在连接完成前保持执行中
                var ok = await Task.Run(() => _captureService.TryConnect(sourceName));
                await _ui.InvokeAsync(() =>
                {
                    IsConnectingCaptureSource = false;
                    if (ok)
                    {
                        IsCaptureSourceConnected = true;
                        SetCaptureStatus("Text.Status.Connected");
                        CaptureSourceButtonText = L10nBridge.T("Text.Btn.DisconnectCapture");
                        _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.CaptureOk"), sourceName));

                        CaptureConnected?.Invoke();
                    }
                    else
                    {
                        SetCaptureStatus("Text.Status.ConnectFailed");
                        _logService.AddLog(L10nBridge.T("Text.Msg.CaptureOpenFail"));
                    }
                });
            }
            catch (Exception ex)
            {
                await _ui.InvokeAsync(() =>
                {
                    IsConnectingCaptureSource = false;
                    SetCaptureStatus("Text.Status.ConnectFailed");
                });
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.CaptureError"), ex.Message));
            }
        }
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

    private static string NormalizeCaptureType(string? captureType)
    {
        return captureType switch
        {
            "DSHOW" or "MSMF" or "DC1394" => captureType,
            _ => "ANY"
        };
    }
}