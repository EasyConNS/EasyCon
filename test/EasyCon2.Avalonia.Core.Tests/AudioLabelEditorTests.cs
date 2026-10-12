using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.AudioLabels;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class AudioLabelEditorTests
{
    [Test]
    public async Task PreviewAndSaveUseTheSameFrozenFrameAlignedSelection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "easycon-audio-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            FakeSource source = new();
            using AudioHistoryBuffer history = new(source);
            byte[] pcm = Enumerable.Range(0, 32000).Select(i => (byte)(i % 251)).ToArray();
            source.Emit(pcm);
            FakePreview preview = new();
            FakeDialogs dialogs = new() { Folder = Task.FromResult<string?>(directory) };
            using AudioLabelEditorViewModel vm = new(history, preview, dialogs);
            await vm.CaptureRecentCommand.ExecuteAsync(null);
            vm.LabelName = "测试音效";
            vm.SelectionStart = 0.25;
            vm.SelectionEnd = 0.75;
            source.Emit(new byte[32000]);
            await vm.PlaySelectionCommand.ExecuteAsync(null);
            await vm.SaveCommand.ExecuteAsync(null);
            AudioLabel saved = AudioLabelStore.Load(Path.Combine(directory, "测试音效.audiolabel", "label.json"));
            Assert.Multiple(() =>
            {
                Assert.That(preview.Played!.Pcm.ToArray(), Is.EqualTo(pcm[8000..24000]));
                Assert.That(saved.Clip.Pcm.ToArray(), Is.EqualTo(preview.Played.Pcm.ToArray()));
                Assert.That(vm.IsPlaying, Is.False);
                Assert.That(vm.IsBusy, Is.False);
            });
            await source.DisconnectAsync();
            await vm.CaptureRecentCommand.ExecuteAsync(null);
            Assert.That(vm.SelectionStart, Is.EqualTo(0.25), "No fresh audio must not erase an edited snapshot.");
            Assert.That(vm.HasClip, Is.True);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task ClosingEditorCancelsPlaybackWithoutDisconnectingCapture()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakePreview preview = new() { WaitForCancellation = true };
        AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs());
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(vm.CaptureRecentCommand.CanExecute(null), Is.False);
        vm.Dispose();
        await playing.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(preview.Cancelled, Is.True);
            Assert.That(source.Disposed, Is.False);
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
        });
    }

    [Test]
    public async Task ClosingDuringFolderPickerDoesNotSaveOrLoseSource()
    {
        TaskCompletionSource<string?> folder = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        AudioLabelEditorViewModel vm = new(history, new FakePreview(), new FakeDialogs { Folder = folder.Task });
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.LabelName = "cancelled";
        string destination = Path.Combine(Path.GetTempPath(), "easycon-not-created-" + Guid.NewGuid().ToString("N"));
        Task save = vm.SaveCommand.ExecuteAsync(null);
        vm.Dispose();
        folder.SetResult(destination);
        await save;
        Assert.That(Directory.Exists(destination), Is.False);
        Assert.That(source.Disposed, Is.False);
    }

    [Test]
    public async Task InvalidSelectionsAndNamesDisableSavingAndMissingOutputDisablesPreview()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        using AudioLabelEditorViewModel vm = new(history, new FakePreview { IsSupported = false }, new FakeDialogs());
        Assert.That(vm.SaveCommand.CanExecute(null), Is.False);
        source.Emit(new byte[32000]);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.LabelName = "../bad";
        Assert.That(vm.SaveCommand.CanExecute(null), Is.False);
        vm.LabelName = "valid";
        Assert.That(vm.SaveCommand.CanExecute(null), Is.True);
        Assert.That(vm.PlaySelectionCommand.CanExecute(null), Is.False);
        vm.SelectionStart = 5;
        Assert.That(vm.SelectionStart, Is.EqualTo(vm.SelectionEnd));
        Assert.That(vm.SaveCommand.CanExecute(null), Is.False);
        vm.SelectionStart = double.NaN;
        vm.SelectionEnd = double.PositiveInfinity;
        Assert.That(vm.SelectionStart, Is.Zero);
        Assert.That(vm.SelectionEnd, Is.EqualTo(vm.Duration));
    }

    [Test]
    public void UnsupportedCaptureStillAllowsOpeningExistingAudio()
    {
        FakeSource source = new() { IsSupported = false };
        using AudioHistoryBuffer history = new(source);
        using AudioLabelEditorViewModel vm = new(history, new FakePreview { IsSupported = false }, new FakeDialogs());
        Assert.That(vm.CaptureRecentCommand.CanExecute(null), Is.False);
        Assert.That(vm.OpenCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task StopDoesNotRunSlowNativeCancellationOnTheCallerThread()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePreview preview = new()
        {
            WaitForCancellation = true,
            OnCancel = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); }
        };
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs());
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
        try
        {
            Task stop = vm.StopPreviewCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(stop.IsCompleted, Is.False, "Stopping must return control before a blocked native callback finishes.");
            release.Set();
            await Task.WhenAll(stop, playing).WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            release.Set();
        }
    }

    private sealed class FakePreview : IAudioPreview
    {
        public bool IsSupported { get; init; } = true;
        public bool WaitForCancellation { get; init; }
        public Action? OnCancel { get; init; }
        public AudioClip? Played { get; private set; }
        public bool Cancelled { get; private set; }
        public async Task PlayAsync(AudioClip clip, CancellationToken cancellationToken)
        {
            Played = clip;
            if (!WaitForCancellation)
                return;
            using CancellationTokenRegistration registration = cancellationToken.Register(() => OnCancel?.Invoke());
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            finally { Cancelled = cancellationToken.IsCancellationRequested; }
        }
    }

    private sealed class FakeSource : IAudioCaptureSource
    {
        public bool IsSupported { get; init; } = true;
        public AudioCaptureState State { get; private set; } = new(AudioCaptureStatus.Connected, Format: new AudioFormat(8000, 2, 16, "pcm"));
        public bool Disposed { get; private set; }
        public event Action<AudioCaptureState>? StateChanged;
        public event Action<AudioChunk>? DataAvailable;
        public event Action<AudioLevel>? LevelChanged { add { } remove { } }
        public void Emit(byte[] pcm) => DataAvailable?.Invoke(new AudioChunk(0, State.Format!, pcm, pcm.Length));
        public void Disconnect() { State = new(AudioCaptureStatus.Disconnected); StateChanged?.Invoke(State); }
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => false;
        public Task DisconnectAsync() { Disconnect(); return Task.CompletedTask; }
        public Task<bool> DisconnectIfConnectingAsync() => Task.FromResult(false);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Task<string?> Folder { get; init; } = Task.FromResult<string?>(null);
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Folder;
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    }
}