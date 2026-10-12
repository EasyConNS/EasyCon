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
        StopPreviewCommand = new AsyncRelayCommand(StopPlaybackAsync, () => IsPlaying);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanUseSelection() && AudioLabelStore.IsValidName(LabelName));
        SelectAllCommand = new RelayCommand(() => { SelectionStart = 0; SelectionEnd = Duration; }, () => CanEdit() && HasClip);
        if (!_history.IsSupported)
            _statusKey = "Text.Status.AudioUnsupported";
    }

    public IAsyncRelayCommand CaptureRecentCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand PlaySelectionCommand { get; }
    public IAsyncRelayCommand StopPreviewCommand { get; }
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

    private bool CanEdit() => !_disposed && !IsBusy && !IsPlaying;
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
        OnPropertyChanged(nameof(SelectionSummary));
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnIsPlayingChanged(bool value) => NotifyCommands();
    partial void OnPlaybackPositionChanged(double? value) => OnPropertyChanged(nameof(PlaybackSummary));

    private void NotifyCommands()
    {
        CaptureRecentCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        PlaySelectionCommand.NotifyCanExecuteChanged();
        StopPreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChangeSelection));
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
        AudioClip selection = _clip.Slice(SelectionStart, SelectionEnd);
        double start = Math.Floor(SelectionStart * _clip.Format.SampleRate) / _clip.Format.SampleRate;
        double end = Math.Min(Duration, start + selection.DurationSeconds);
        double lastPosition = 0;
        using CancellationTokenSource playback = new();
        _playback = playback;
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
            SetStatus("Text.AudioLabel.Stopped");
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
            IsPlaying = false;
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
        CancellationTokenSource? playback = _playback;
        if (playback == null)
            return;
        try
        {
            // Native Stop may wait for the output thread. Never run cancellation callbacks on the UI thread.
            await playback.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Playback finished while the stop request was being dispatched.
        }
        catch (Exception ex)
        {
            SetStatus("Text.AudioLabel.Error", ex.Message);
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
    }

    public void Dispose()
    {
        _disposed = true;
        _ = StopPlaybackAsync();
        // The application owns history and capture; closing the editor releases only its snapshot/playback.
        _clip = null;
        PlaybackPosition = null;
        Waveform = [];
        NotifyCommands();
    }
}