using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.Localization;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Core.Threading;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Core.ViewModels;

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
            CaptureSourceStatus = L10nBridge.T("Text.Status.Disconnected");
            CaptureSourceButtonText = L10nBridge.T("Text.Btn.ConnectCapture");
        };
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
                CaptureSourceStatus = L10nBridge.T("Text.Status.NotConnected");
                CaptureSourceButtonText = L10nBridge.T("Text.Btn.ConnectCapture");
                _logService.AddLog(L10nBridge.T("Text.Msg.CaptureDisconnected"));
                return;
            }

            IsConnectingCaptureSource = true;
            CaptureSourceStatus = L10nBridge.T("Text.Status.Connecting");
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
                        CaptureSourceStatus = L10nBridge.T("Text.Status.Connected");
                        CaptureSourceButtonText = L10nBridge.T("Text.Btn.DisconnectCapture");
                        _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.CaptureOk"), sourceName));

                        CaptureConnected?.Invoke();
                    }
                    else
                    {
                        CaptureSourceStatus = "连接失败";
                        _logService.AddLog(L10nBridge.T("Text.Msg.CaptureOpenFail"));
                    }
                });
            }
            catch (Exception ex)
            {
                await _ui.InvokeAsync(() =>
                {
                    IsConnectingCaptureSource = false;
                    CaptureSourceStatus = "连接失败";
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