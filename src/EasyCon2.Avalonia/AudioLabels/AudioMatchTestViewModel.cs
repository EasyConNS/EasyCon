using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.AudioLabels;

public sealed record AudioMatchHitRow(string Time, int Score, int Threshold);

public partial class AudioMatchTestViewModel : ObservableObject, IDisposable
{
    private readonly IAudioCaptureSource _source;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly Func<string?> _scriptPath;
    private AudioMatchTemplate? _template;
    private AudioMatchMonitor? _monitor;
    private CancellationTokenSource? _listening;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private string _statusKey = "Text.AudioMatch.Ready";
    private object[] _statusArguments = [];

    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private bool _isListening;
    [ObservableProperty]
    private string _labelName = "";
    [ObservableProperty]
    private int _threshold = 85;
    [ObservableProperty]
    private double _cooldownSeconds = 1;
    [ObservableProperty]
    private int? _score;
    [ObservableProperty]
    private int _peakScore;
    [ObservableProperty]
    private int _hitCount;

    public AudioMatchTestViewModel(IAudioCaptureSource source, IDialogService dialogs, IUiDispatcher ui,
        Func<string?>? scriptPath = null)
    {
        _source = source;
        _dialogs = dialogs;
        _ui = ui;
        _scriptPath = scriptPath ?? (() => null);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => !_disposed && !IsBusy && !IsListening);
        StartCommand = new AsyncRelayCommand(StartAsync, () => !_disposed && !IsBusy && !IsListening && _template != null && IsSupported);
        StopCommand = new RelayCommand(() => _listening?.Cancel(), () => !_disposed && IsListening);
        ClearCommand = new RelayCommand(ClearResults, () => !_disposed);
        if (!IsSupported)
            _statusKey = "Text.Status.AudioUnsupported";
    }

    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public ObservableCollection<AudioMatchHitRow> Hits { get; } = [];
    public bool IsSupported => _source.IsSupported;
    public bool HasTemplate => _template != null;
    public string TemplateSummary => _template == null ? L10nBridge.T("Text.AudioMatch.NoTemplate")
        : string.Format(L10nBridge.T("Text.AudioMatch.TemplateSummary"), LabelName, _template.DurationSeconds);
    public string ScoreText => Score?.ToString() ?? "—";
    public int ScoreValue => Score ?? 0;
    public string PeakScoreText => PeakScore.ToString();
    public string HitSummary => string.Format(L10nBridge.T("Text.AudioMatch.HitSummary"), HitCount);
    public string Status => string.Format(L10nBridge.T(_statusKey), _statusArguments);

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnIsListeningChanged(bool value) => NotifyCommands();
    partial void OnScoreChanged(int? value)
    {
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(ScoreValue));
    }
    partial void OnPeakScoreChanged(int value) => OnPropertyChanged(nameof(PeakScoreText));
    partial void OnHitCountChanged(int value) => OnPropertyChanged(nameof(HitSummary));
    partial void OnThresholdChanged(int value)
    {
        int bounded = Math.Clamp(value, 10, 100);
        if (bounded != value)
            Threshold = bounded;
        UpdateSettings();
    }
    partial void OnCooldownSecondsChanged(double value)
    {
        double bounded = double.IsFinite(value) ? Math.Clamp(value, 0, 30) : 1;
        if (bounded != value)
            CooldownSeconds = bounded;
        UpdateSettings();
    }

    private void UpdateSettings()
    {
        if (_monitor != null)
            _monitor.Settings = new AudioMatchSettings(Threshold, CooldownSeconds);
    }

    private void NotifyCommands()
    {
        OpenCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private async Task OpenAsync()
    {
        if (_disposed || IsBusy || IsListening)
            return;
        IsBusy = true;
        CancellationToken token = _lifetime.Token;
        try
        {
            string? script = _scriptPath();
            string? directory = !string.IsNullOrEmpty(script) && Path.IsPathFullyQualified(script) ? Path.GetDirectoryName(script) : null;
            IReadOnlyList<string> files = await _dialogs.OpenFilesAsync(L10nBridge.T("Text.AudioLabel.Open"),
                [new FileDialogFilter(L10nBridge.T("Text.AudioLabel.FileFilter"), ["label.json", "*.wav"])], directory);
            if (_disposed || files.Count == 0)
                return;
            // A failed replacement must not leave the previous label armed under the new selection.
            _template = null;
            LabelName = "";
            ClearResults();
            RefreshTemplate();
            (AudioMatchTemplate? template, string name, bool validDuration) = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string path = files[0];
                AudioClip clip;
                string name;
                if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    clip = AudioLabelStore.ReadWave(path);
                    name = Path.GetFileNameWithoutExtension(path);
                }
                else
                {
                    AudioLabel label = AudioLabelStore.Load(path);
                    clip = label.Clip;
                    name = label.Metadata.Name;
                }
                bool duration = clip.DurationSeconds is >= AudioMatchTemplate.MinimumSeconds and <= AudioMatchTemplate.MaximumSeconds;
                return (duration ? AudioMatchTemplate.Create(clip, token) : null, name, duration);
            }, token);
            if (_disposed)
                return;
            _template = template;
            LabelName = template == null ? "" : name;
            RefreshTemplate();
            SetStatus(!validDuration ? "Text.AudioMatch.InvalidDuration"
                : template == null ? "Text.AudioMatch.NoSignal"
                : !IsSupported ? "Text.Status.AudioUnsupported" : "Text.AudioMatch.Loaded");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus("Text.AudioLabel.Error", ex.Message); }
        finally { IsBusy = false; }
    }

    private void RefreshTemplate()
    {
        OnPropertyChanged(nameof(HasTemplate));
        OnPropertyChanged(nameof(TemplateSummary));
        NotifyCommands();
    }

    private async Task StartAsync()
    {
        if (_disposed || IsListening || IsBusy || _template == null || !IsSupported)
            return;
        using CancellationTokenSource listening = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        using AudioMatchMonitor monitor = new(_source, _template) { Settings = new AudioMatchSettings(Threshold, CooldownSeconds) };
        _monitor = monitor;
        _listening = listening;
        ClearResults();
        IsListening = true;
        SetStatus("Text.AudioMatch.WaitingForAudio");
        try
        {
            await Task.Run(async () =>
            {
                using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
                while (await timer.WaitForNextTickAsync(listening.Token))
                {
                    AudioMatchUpdate update = monitor.Poll(listening.Token);
                    // Await dispatch to keep at most one UI update queued, even during a slow render.
                    await _ui.InvokeAsync(() =>
                    {
                        if (!_disposed && !listening.IsCancellationRequested)
                            ApplyUpdate(monitor.IsCurrent(update) ? update : new AudioMatchUpdate(AudioMatchStatus.Interrupted, null, []));
                    });
                }
            }, listening.Token);
        }
        catch (OperationCanceledException) when (listening.IsCancellationRequested)
        {
            SetStatus("Text.AudioMatch.Stopped");
        }
        catch (Exception ex) { SetStatus("Text.AudioLabel.Error", ex.Message); }
        finally
        {
            _monitor = null;
            _listening = null;
            Score = null;
            IsListening = false;
        }
    }

    private void ApplyUpdate(AudioMatchUpdate update)
    {
        Score = update.Score;
        PeakScore = Math.Max(PeakScore, update.Score ?? 0);
        foreach (AudioMatchHit hit in update.Hits)
        {
            HitCount++;
            Hits.Insert(0, new AudioMatchHitRow(hit.DetectedAt.ToLocalTime().ToString("HH:mm:ss.fff"),
                hit.Score, hit.Threshold));
            if (Hits.Count > 100)
                Hits.RemoveAt(Hits.Count - 1);
        }
        SetStatus("Text.AudioMatch." + update.Status);
    }

    private void ClearResults()
    {
        Score = null;
        PeakScore = 0;
        HitCount = 0;
        Hits.Clear();
    }

    private void SetStatus(string key, params object[] arguments)
    {
        if (_disposed)
            return;
        _statusKey = key;
        _statusArguments = arguments;
        OnPropertyChanged(nameof(Status));
    }

    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(TemplateSummary));
        OnPropertyChanged(nameof(HitSummary));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _monitor?.Dispose();
        _template = null;
        NotifyCommands();
    }
}