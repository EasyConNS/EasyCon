using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.AudioLabels;

public partial class AudioLabelEditorViewModel : ObservableObject, IDisposable
{
    private readonly AudioHistoryBuffer _history;
    private readonly IAudioPreview _preview;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly Func<string?> _scriptPath;
    private AudioClip? _clip;
    private CancellationTokenSource? _playback;
    private TaskCompletionSource? _playbackEnded;
    private AudioClipRecording? _recording;
    private bool _pauseRequested;
    private bool _startFromCursor;
    private bool _disposed;
    private string _statusKey = "Text.AudioLabel.Ready";
    private object[] _statusArguments = [];

    [ObservableProperty]
    private string _labelName = "";

    [ObservableProperty]
    private double _selectionStart;

    [ObservableProperty]
    private double _selectionEnd;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isPreviewTransitioning;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private double _recordingDuration;

    [ObservableProperty]
    private double _recordingLimit = 15;

    [ObservableProperty]
    private double? _playbackPosition;

    [ObservableProperty]
    private AudioWaveformPeak[] _waveform = [];

    public AudioLabelEditorViewModel(AudioHistoryBuffer history, IAudioPreview preview, IDialogService dialogs, IUiDispatcher ui,
        Func<string?>? scriptPath = null)
    {
        _history = history;
        _preview = preview;
        _dialogs = dialogs;
        _ui = ui;
        _scriptPath = scriptPath ?? (() => null);
        CaptureRecentCommand = new AsyncRelayCommand(CaptureRecentAsync, () => CanEdit() && _history.IsSupported);
        OpenCommand = new AsyncRelayCommand(OpenAsync, CanEdit);
        PlaySelectionCommand = new AsyncRelayCommand(PlaySelectionAsync, () => CanUseSelection() && _preview.IsSupported);
        TogglePreviewCommand = new AsyncRelayCommand(TogglePreviewAsync,
            () => !_disposed && !IsBusy && !IsRecording && !IsPreviewTransitioning && _preview.IsSupported && HasClip && SelectionEnd > SelectionStart);
        StopPreviewCommand = new AsyncRelayCommand(StopPlaybackAsync, () => !_disposed && !IsPreviewTransitioning && (IsPlaying || IsPaused));
        ToggleRecordingCommand = new RelayCommand(ToggleRecording, () => !IsBusy && (IsRecording || CanEdit()) && _history.IsSupported && !_disposed);
        SeekPreviewCommand = new RelayCommand<double>(SeekPreview, _ => CanUseSelection());
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanUseSelection() && AudioLabelStore.IsValidName(LabelName));
        SelectAllCommand = new RelayCommand(() => { SelectionStart = 0; SelectionEnd = Duration; }, () => CanEdit() && HasClip);
        if (!_history.IsSupported)
            _statusKey = "Text.Status.AudioUnsupported";
    }

    public IAsyncRelayCommand CaptureRecentCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand PlaySelectionCommand { get; }
    public IAsyncRelayCommand TogglePreviewCommand { get; }
    public IAsyncRelayCommand StopPreviewCommand { get; }
    public IRelayCommand ToggleRecordingCommand { get; }
    public IRelayCommand<double> SeekPreviewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public bool HasClip => _clip != null;
    public bool CanChangeSelection => CanEdit() && HasClip;
    public bool IsPreviewSupported => _preview.IsSupported;
    public double Duration => _clip?.DurationSeconds ?? 0;
    public string Status => string.Format(L10nBridge.T(_statusKey), _statusArguments);
    public bool IsNameInvalid => LabelName.Length != 0 && !AudioLabelStore.IsValidName(LabelName);
    public string ClipSummary => _clip == null ? "" : string.Format(L10nBridge.T("Text.AudioLabel.ClipSummary"),
        Duration, _clip.Format.SampleRate, _clip.Format.Channels, _clip.Format.BitsPerSample);
    public string SelectionSummary => string.Format(L10nBridge.T("Text.AudioLabel.SelectionSummary"),
        SelectionStart, SelectionEnd, Math.Max(0, SelectionEnd - SelectionStart));
    public string PlaybackSummary => string.Format(L10nBridge.T("Text.AudioLabel.PlaybackSummary"), PlaybackPosition ?? 0, Duration);
    public string PreviewButtonText => L10nBridge.T(IsPlaying ? "Text.AudioLabel.Pause" : IsPaused ? "Text.AudioLabel.Resume" : "Text.AudioLabel.Play");
    public string RecordingButtonText => L10nBridge.T(IsRecording ? "Text.AudioLabel.StopRecording" : "Text.AudioLabel.Record");
    public string RecordingSummary => string.Format(L10nBridge.T("Text.AudioLabel.RecordingSummary"), RecordingDuration, RecordingLimit);

    private bool CanEdit() => !_disposed && !IsBusy && !IsPlaying && !IsRecording && !IsPreviewTransitioning;
    private bool CanUseSelection() => CanEdit() && HasClip && SelectionEnd > SelectionStart;

    partial void OnLabelNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsNameInvalid));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectionStartChanged(double value)
    {
        double bounded = double.IsFinite(value) ? Math.Clamp(value, 0, SelectionEnd) : 0;
        if (value != bounded)
            SelectionStart = bounded;
        SelectionChanged();
    }

    partial void OnSelectionEndChanged(double value)
    {
        double bounded = double.IsFinite(value) ? Math.Clamp(value, SelectionStart, Duration) : Duration;
        if (value != bounded)
            SelectionEnd = bounded;
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        if (IsPaused)
            SetStatus("Text.AudioLabel.Stopped");
        IsPaused = false;
        _startFromCursor = false;
        if (PlaybackPosition is double position)
            PlaybackPosition = Math.Clamp(position, SelectionStart, SelectionEnd);
        OnPropertyChanged(nameof(SelectionSummary));
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnIsPlayingChanged(bool value) => NotifyCommands();
    partial void OnIsPausedChanged(bool value) => NotifyCommands();
    partial void OnIsPreviewTransitioningChanged(bool value) => NotifyCommands();
    partial void OnIsRecordingChanged(bool value) => NotifyCommands();
    partial void OnRecordingDurationChanged(double value) => OnPropertyChanged(nameof(RecordingSummary));
    partial void OnRecordingLimitChanged(double value) => OnPropertyChanged(nameof(RecordingSummary));
    partial void OnPlaybackPositionChanged(double? value) => OnPropertyChanged(nameof(PlaybackSummary));

    private void NotifyCommands()
    {
        CaptureRecentCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        PlaySelectionCommand.NotifyCanExecuteChanged();
        TogglePreviewCommand.NotifyCanExecuteChanged();
        StopPreviewCommand.NotifyCanExecuteChanged();
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        SeekPreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChangeSelection));
        OnPropertyChanged(nameof(PreviewButtonText));
        OnPropertyChanged(nameof(RecordingButtonText));
    }

    private void ToggleRecording()
    {
        if (_disposed || IsBusy)
            return;
        if (_recording != null)
        {
            _recording.Stop();
            return;
        }
        if (!CanEdit())
            return;
        try
        {
            AudioClipRecording recording = _history.BeginRecording();
            _recording = recording;
            IsPaused = false;
            _startFromCursor = false;
            RecordingDuration = 0;
            RecordingLimit = recording.MaximumSeconds;
            IsRecording = true;
            SetStatus("Text.AudioLabel.Recording");
            _ = ObserveRecordingAsync(recording);
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
    }

    private async Task ObserveRecordingAsync(AudioClipRecording recording)
    {
        try
        {
            while (!recording.Completion.IsCompleted)
            {
                await Task.WhenAny(recording.Completion, Task.Delay(50));
                if (_disposed)
                    return;
                RecordingDuration = recording.Duration;
            }
            AudioClip clip = await recording.Completion;
            if (_disposed)
                return;
            IsBusy = true;
            AudioWaveformPeak[] waveform = await Task.Run(() => clip.CreateWaveform());
            if (!_disposed)
            {
                SetClip(clip, waveform);
                SetStatus(recording.ReachedLimit ? "Text.AudioLabel.RecordedLimit" : "Text.AudioLabel.Recorded", clip.DurationSeconds);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
        finally
        {
            recording.Dispose();
            _recording = null;
            IsRecording = false;
            IsBusy = false;
        }
    }

    private void SeekPreview(double position)
    {
        if (!CanUseSelection() || !double.IsFinite(position))
            return;
        PlaybackPosition = Math.Clamp(position, SelectionStart, SelectionEnd);
        _startFromCursor = true;
    }

    private async Task TogglePreviewAsync()
    {
        if (IsPlaying)
        {
            _pauseRequested = true;
            await CancelPlaybackAsync();
        }
        else if (PlaySelectionCommand.CanExecute(null))
            _ = PlaySelectionCommand.ExecuteAsync(null);
    }

    private async Task CaptureRecentAsync()
    {
        if (!CanEdit() || !_history.IsSupported)
            return;
        IsBusy = true;
        try
        {
            AudioClip? clip = await Task.Run(() => _history.Snapshot());
            if (_disposed)
                return;
            if (clip == null)
            {
                SetStatus("Text.AudioLabel.NoHistory");
                return;
            }
            AudioWaveformPeak[] waveform = await Task.Run(() => clip.CreateWaveform());
            if (!_disposed)
            {
                SetClip(clip, waveform);
                SetStatus("Text.AudioLabel.Captured", clip.DurationSeconds);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenAsync()
    {
        if (!CanEdit())
            return;
        IsBusy = true;
        try
        {
            IReadOnlyList<string> files = await _dialogs.OpenFilesAsync(L10nBridge.T("Text.AudioLabel.Open"),
                [new FileDialogFilter(L10nBridge.T("Text.AudioLabel.FileFilter"), ["*.wav", "label.json"])], SuggestedDirectory());
            if (_disposed || files.Count == 0)
                return;
            string path = files[0];
            (AudioClip clip, string name) = await Task.Run(() =>
            {
                if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                    return (AudioLabelStore.ReadWave(path), Path.GetFileNameWithoutExtension(path));
                AudioLabel label = AudioLabelStore.Load(path);
                return (label.Clip, label.Metadata.Name);
            });
            AudioWaveformPeak[] waveform = await Task.Run(() => clip.CreateWaveform());
            if (!_disposed)
            {
                SetClip(clip, waveform);
                LabelName = name;
                SetStatus("Text.AudioLabel.Loaded");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetClip(AudioClip clip, AudioWaveformPeak[] waveform)
    {
        IsPaused = false;
        _startFromCursor = false;
        // Reset the start before installing a shorter clip so all clamps stay ordered.
        SelectionStart = 0;
        _clip = clip;
        PlaybackPosition = null;
        Waveform = waveform;
        OnPropertyChanged(nameof(HasClip));
        OnPropertyChanged(nameof(Duration));
        SelectionEnd = Duration;
        OnPropertyChanged(nameof(ClipSummary));
        OnPropertyChanged(nameof(PlaybackSummary));
        NotifyCommands();
    }

    private async Task PlaySelectionAsync()
    {
        if (!CanUseSelection() || _clip == null)
            return;
        double requestedStart = _startFromCursor ? Math.Clamp(PlaybackPosition ?? SelectionStart, SelectionStart, SelectionEnd) : SelectionStart;
        _startFromCursor = false;
        IsPaused = false;
        if (requestedStart >= SelectionEnd)
        {
            SetStatus("Text.AudioLabel.Played");
            return;
        }
        AudioClip selection = _clip.Slice(requestedStart, SelectionEnd);
        double start = Math.Floor(requestedStart * _clip.Format.SampleRate) / _clip.Format.SampleRate;
        double end = Math.Min(Duration, start + selection.DurationSeconds);
        double lastPosition = 0;
        using CancellationTokenSource playback = new();
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _playbackEnded = ended;
        _playback = playback;
        _pauseRequested = false;
        PlaybackPosition = start;
        IsPlaying = true;
        SetStatus("Text.AudioLabel.Playing");
        try
        {
            await _preview.PlayAsync(selection, playback.Token, position =>
            {
                if (!double.IsFinite(position))
                    return;
                double bounded = Math.Max(Volatile.Read(ref lastPosition), Math.Clamp(position, 0, selection.DurationSeconds));
                Volatile.Write(ref lastPosition, bounded);
                _ui.Post(() =>
                {
                    // A queued update from a stopped/closed preview must never affect the next one.
                    if (!_disposed && ReferenceEquals(_playback, playback))
                        PlaybackPosition = Math.Clamp(Math.Max(PlaybackPosition ?? start, start + bounded), start, end);
                });
            });
            playback.Token.ThrowIfCancellationRequested();
            Volatile.Write(ref lastPosition, selection.DurationSeconds);
            SetStatus("Text.AudioLabel.Played");
        }
        catch (OperationCanceledException) when (playback.IsCancellationRequested)
        {
            IsPaused = _pauseRequested && !_disposed;
            _startFromCursor = IsPaused;
            SetStatus(IsPaused ? "Text.AudioLabel.Paused" : "Text.AudioLabel.Stopped");
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
        finally
        {
            if (!_disposed)
                PlaybackPosition = Math.Clamp(Math.Max(PlaybackPosition ?? start, start + Volatile.Read(ref lastPosition)), start, end);
            _playback = null;
            _playbackEnded = null;
            IsPlaying = false;
            ended.TrySetResult();
        }
    }

    private async Task SaveAsync()
    {
        if (!CanUseSelection() || !AudioLabelStore.IsValidName(LabelName) || _clip == null)
            return;
        AudioClip selection = _clip.Slice(SelectionStart, SelectionEnd);
        string name = LabelName;
        IsBusy = true;
        try
        {
            string? directory = await _dialogs.OpenFolderAsync(L10nBridge.T("Text.AudioLabel.SaveFolder"), SuggestedDirectory());
            if (_disposed || directory == null)
                return;
            if (Directory.Exists(Path.Combine(directory, name + ".audiolabel")))
            {
                SetStatus("Text.AudioLabel.AlreadyExists");
                return;
            }
            string saved = await Task.Run(() => AudioLabelStore.Save(directory, name, selection));
            SetStatus("Text.AudioLabel.Saved", saved);
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopPlaybackAsync()
    {
        _pauseRequested = false;
        IsPaused = false;
        _startFromCursor = false;
        await CancelPlaybackAsync();
        SetStatus("Text.AudioLabel.Stopped");
    }

    private async Task CancelPlaybackAsync()
    {
        CancellationTokenSource? playback = _playback;
        Task? ended = _playbackEnded?.Task;
        if (playback == null)
            return;
        IsPreviewTransitioning = true;
        try
        {
            // Native Stop may wait for the output thread. Never run cancellation callbacks on the UI thread.
            await playback.CancelAsync();
            if (ended != null)
                await ended;
        }
        catch (ObjectDisposedException)
        {
            // Playback finished while the stop request was being dispatched.
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
        }
        finally
        {
            IsPreviewTransitioning = false;
        }
    }

    private string? SuggestedDirectory()
    {
        string? path = _scriptPath();
        return !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) ? Path.GetDirectoryName(path) : null;
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
        OnPropertyChanged(nameof(ClipSummary));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(PlaybackSummary));
        OnPropertyChanged(nameof(PreviewButtonText));
        OnPropertyChanged(nameof(RecordingButtonText));
        OnPropertyChanged(nameof(RecordingSummary));
    }

    public void Dispose()
    {
        _disposed = true;
        _recording?.Dispose();
        _ = StopPlaybackAsync();
        // The application owns history and capture; closing the editor releases only its snapshot/playback.
        _clip = null;
        PlaybackPosition = null;
        Waveform = [];
        NotifyCommands();
    }
}