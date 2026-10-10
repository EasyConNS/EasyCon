using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture.Audio;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Connection;

/// <summary>
/// 游戏音频输入连接编排。这里只负责选择设备、连接生命周期和电平展示；
/// 音频块通过 <see cref="IAudioCaptureSource.DataAvailable"/> 保留给后续消费者。
/// </summary>
public partial class AudioConnectionViewModel : ObservableObject, IDisposable
{
    private readonly IAudioCaptureSource _audioSource;
    private readonly ILogService _logService;
    private readonly IUiDispatcher _ui;
    private readonly TimeSpan _connectionTimeout;
    private CancellationTokenSource? _connectTimeout;
    private CancellationTokenSource? _attemptCancellation;
    private long _attemptVersion;
    private long _openingAttempt;
    private long _timedOutAttempt;
    private long _lastLevelUpdateTimestamp;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _audioSourceOptions = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioSource;

    [ObservableProperty]
    private string _audioSourceStatus = L10nBridge.T("Text.Status.NotConnected");

    [ObservableProperty]
    private bool _isConnectingAudioSource;

    [ObservableProperty]
    private bool _isAudioSourceConnected;

    [ObservableProperty]
    private string _audioSourceButtonText = L10nBridge.T("Text.Btn.ConnectAudio");

    [ObservableProperty]
    private string _audioFormatText = "";

    [ObservableProperty]
    private string _audioLevelText = "";

    public ICommand ConnectAudioSourceCommand { get; }

    public ICommand RefreshAudioSourcesCommand { get; }

    /// <summary>
    /// 音频采集源本身，供后续消费者订阅 <see cref="IAudioCaptureSource.DataAvailable"/>；
    /// 本面板只负责连接生命周期和电平展示，不在这里做判闪决策。
    /// </summary>
    public IAudioCaptureSource CaptureSource => _audioSource;

    public AudioConnectionViewModel(IAudioCaptureSource audioSource, ILogService logService, IUiDispatcher uiDispatcher, TimeSpan? connectionTimeout = null)
    {
        _audioSource = audioSource;
        _logService = logService;
        _ui = uiDispatcher;
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(15);
        if (_connectionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout));

        ConnectAudioSourceCommand = new AsyncRelayCommand(ConnectAudioSourceAsync);
        RefreshAudioSourcesCommand = new RelayCommand(RefreshAudioSources);

        _audioSource.StateChanged += OnStateChanged;
        _audioSource.LevelChanged += OnLevelChanged;
        RefreshAudioSources();
        SyncState(_audioSource.State);
    }

    private async Task ConnectAudioSourceAsync()
    {
        if (_disposed || IsConnectingAudioSource)
            return;

        if (IsAudioSourceConnected || _audioSource.State.Status == AudioCaptureStatus.Connecting)
        {
            CancelConnectTimeout();
            if (_attemptCancellation != null)
                await _attemptCancellation.CancelAsync();
            try
            {
                await _audioSource.DisconnectAsync();
                _logService.AddLog(L10nBridge.T("Text.Msg.AudioDisconnected"));
            }
            catch (Exception ex)
            {
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioDisconnectError"), ex.Message));
                await _ui.InvokeAsync(() => SyncState(_audioSource.State));
            }
            return;
        }

        var selected = SelectedAudioSource;
        if (selected == null)
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.SelectAudioSource"));
            return;
        }

        IsConnectingAudioSource = true;
        AudioSourceStatus = L10nBridge.T("Text.Status.Connecting");
        AudioSourceButtonText = L10nBridge.T("Text.Btn.DisconnectAudio");
        AudioFormatText = "";
        AudioLevelText = "";
        _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.OpenAudioPrepare"), selected.Name));
        long attempt = Interlocked.Increment(ref _attemptVersion);
        Interlocked.Exchange(ref _openingAttempt, attempt);
        if (_attemptCancellation != null)
            await _attemptCancellation.CancelAsync();
        _attemptCancellation?.Dispose();
        _attemptCancellation = new CancellationTokenSource();
        CancellationToken attemptToken = _attemptCancellation.Token;
        StartConnectTimeout(attempt, _attemptCancellation);

        try
        {
            Task<bool> connection = Task.Run(() => _audioSource.TryConnect(selected.Id, attemptToken));
            bool ok = await connection.WaitAsync(attemptToken);
            if (_disposed || attempt != Interlocked.Read(ref _attemptVersion) || attemptToken.IsCancellationRequested)
                return;
            await _ui.InvokeAsync(() =>
            {
                Interlocked.CompareExchange(ref _openingAttempt, 0, attempt);
                if (_disposed || attempt != Interlocked.Read(ref _attemptVersion) || attemptToken.IsCancellationRequested)
                    return;

                AudioCaptureState state = _audioSource.State;
                if (!ok && state.Status != AudioCaptureStatus.Failed)
                    SyncState(new AudioCaptureState(AudioCaptureStatus.Failed, selected, Error: L10nBridge.T("Text.Msg.AudioOpenFail")));
                else if ((IsConnectingAudioSource || IsAudioSourceConnected)
                    && state.Status is AudioCaptureStatus.Failed or AudioCaptureStatus.Disconnected)
                    SyncState(state);
            });
        }
        catch (OperationCanceledException) when (attemptToken.IsCancellationRequested)
        {
            // A timed-out native open can finish later; its canceled token prevents installation.
        }
        catch (Exception ex)
        {
            if (_disposed || attempt != Interlocked.Read(ref _attemptVersion) || attemptToken.IsCancellationRequested)
                return;
            CancelConnectTimeout();
            try
            {
                await _audioSource.DisconnectAsync();
            }
            catch (Exception disconnectException)
            {
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioDisconnectError"), disconnectException.Message));
            }

            await _ui.InvokeAsync(() =>
            {
                IsConnectingAudioSource = false;
                IsAudioSourceConnected = false;
                AudioSourceStatus = L10nBridge.T("Text.Status.ConnectFailed");
                AudioSourceButtonText = L10nBridge.T("Text.Btn.ConnectAudio");
                AudioFormatText = "";
                AudioLevelText = "";
                Interlocked.Exchange(ref _lastLevelUpdateTimestamp, 0);
            });
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioConnectError"), ex.Message));
        }
        finally
        {
            Interlocked.CompareExchange(ref _openingAttempt, 0, attempt);
        }
    }

    private void RefreshAudioSources()
    {
        if (_disposed)
            return;

        try
        {
            var oldId = SelectedAudioSource?.Id;
            AudioSourceOptions = new ObservableCollection<AudioDeviceInfo>(_audioSource.GetAvailableDevices());
            SelectedAudioSource = oldId == null
                ? null
                : AudioSourceOptions.FirstOrDefault(device => device.Id == oldId);
        }
        catch (Exception ex)
        {
            AudioSourceOptions = new ObservableCollection<AudioDeviceInfo>();
            SelectedAudioSource = null;
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioListError"), ex.Message));
        }
    }

    private void OnStateChanged(AudioCaptureState state)
    {
        _ui.Post(() =>
        {
            if (_timedOutAttempt == Interlocked.Read(ref _attemptVersion) && _timedOutAttempt != 0)
                return;
            long opening = Interlocked.Read(ref _openingAttempt);
            if (opening != 0 && opening == Interlocked.Read(ref _attemptVersion)
                && state.Status is AudioCaptureStatus.Disconnected or AudioCaptureStatus.Failed)
                return; // An earlier attempt can still own the backend state while this call waits to open.
            if (!ReferenceEquals(state, _audioSource.State))
                return;

            SyncState(state);
        });
    }

    private void SyncState(AudioCaptureState state)
    {
        if (_disposed)
            return;

        switch (state.Status)
        {
            case AudioCaptureStatus.Connecting:
                IsConnectingAudioSource = true;
                IsAudioSourceConnected = false;
                AudioSourceStatus = L10nBridge.T("Text.Status.Connecting");
                AudioSourceButtonText = L10nBridge.T("Text.Btn.DisconnectAudio");
                break;
            case AudioCaptureStatus.Connected:
                CancelConnectTimeout();
                IsConnectingAudioSource = false;
                IsAudioSourceConnected = true;
                AudioSourceStatus = L10nBridge.T("Text.Status.Connected");
                AudioSourceButtonText = L10nBridge.T("Text.Btn.DisconnectAudio");
                AudioFormatText = state.Format == null
                    ? ""
                    : string.Format(
                        L10nBridge.T("Text.Audio.Format"),
                        state.Format.SampleRate,
                        state.Format.Channels,
                        state.Format.BitsPerSample);
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioOk"), state.Device?.Name ?? ""));
                break;
            case AudioCaptureStatus.Failed:
                CancelConnectTimeout();
                IsConnectingAudioSource = false;
                IsAudioSourceConnected = false;
                AudioSourceStatus = L10nBridge.T("Text.Status.ConnectFailed");
                AudioSourceButtonText = L10nBridge.T("Text.Btn.ConnectAudio");
                AudioFormatText = "";
                AudioLevelText = "";
                Interlocked.Exchange(ref _lastLevelUpdateTimestamp, 0);
                if (!string.IsNullOrWhiteSpace(state.Error))
                    _logService.AddLog(state.Error);
                break;
            default:
                CancelConnectTimeout();
                IsConnectingAudioSource = false;
                IsAudioSourceConnected = false;
                AudioSourceStatus = L10nBridge.T("Text.Status.NotConnected");
                AudioSourceButtonText = L10nBridge.T("Text.Btn.ConnectAudio");
                AudioFormatText = "";
                AudioLevelText = "";
                Interlocked.Exchange(ref _lastLevelUpdateTimestamp, 0);
                break;
        }
    }

    private void OnLevelChanged(AudioLevel level)
    {
        long now = Stopwatch.GetTimestamp();
        long last = Interlocked.Read(ref _lastLevelUpdateTimestamp);
        if (last != 0 && now - last < Stopwatch.Frequency / 10)
            return;
        Interlocked.Exchange(ref _lastLevelUpdateTimestamp, now);

        _ui.Post(() =>
        {
            if (_disposed || !IsAudioSourceConnected)
                return;

            if (level.Silent)
            {
                AudioLevelText = L10nBridge.T("Text.Status.AudioSilent");
                return;
            }

            var peakDb = level.Peak <= 0 ? -60 : Math.Max(-60, 20 * Math.Log10(level.Peak));
            AudioLevelText = $"{peakDb:F1} dBFS";
        });
    }

    private void StartConnectTimeout(long attempt, CancellationTokenSource attemptCancellation)
    {
        CancelConnectTimeout();
        _connectTimeout = new CancellationTokenSource();
        var token = _connectTimeout.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_connectionTimeout, token);
                await _ui.InvokeAsync(async () =>
                {
                    if (token.IsCancellationRequested || _disposed || !IsConnectingAudioSource
                        || attempt != Interlocked.Read(ref _attemptVersion))
                        return;

                    Task<bool> disconnect = _audioSource.DisconnectIfConnectingAsync();
                    if (_audioSource.State.Status == AudioCaptureStatus.Connected)
                        return;

                    _timedOutAttempt = attempt;
                    await attemptCancellation.CancelAsync();
                    SyncState(new AudioCaptureState(AudioCaptureStatus.Failed));
                    _logService.AddLog(L10nBridge.T("Text.Msg.AudioTimeout"));

                    // The UI and command have already recovered. Observe native cleanup
                    // asynchronously, in the same context that started it.
                    try
                    {
                        await disconnect.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await _ui.InvokeAsync(() =>
                        {
                            if (!_disposed && attempt == Interlocked.Read(ref _attemptVersion))
                                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioDisconnectError"), ex.Message));
                        });
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || _disposed || attempt != Interlocked.Read(ref _attemptVersion))
                    return;

                await _ui.InvokeAsync(async () =>
                {
                    _timedOutAttempt = attempt;
                    await attemptCancellation.CancelAsync();
                    IsConnectingAudioSource = false;
                    IsAudioSourceConnected = false;
                    AudioSourceStatus = L10nBridge.T("Text.Status.ConnectFailed");
                    AudioSourceButtonText = L10nBridge.T("Text.Btn.ConnectAudio");
                    AudioFormatText = "";
                    AudioLevelText = "";
                    Interlocked.Exchange(ref _lastLevelUpdateTimestamp, 0);
                });
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.AudioDisconnectError"), ex.Message));
            }
        });
    }

    private void CancelConnectTimeout()
    {
        _connectTimeout?.Cancel();
        _connectTimeout?.Dispose();
        _connectTimeout = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelConnectTimeout();
        _attemptCancellation?.Cancel();
        _attemptCancellation?.Dispose();
        _attemptCancellation = null;
        _audioSource.StateChanged -= OnStateChanged;
        _audioSource.LevelChanged -= OnLevelChanged;
        _audioSource.Dispose();
    }
}