using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.AudioLabels;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class AudioLabelEditorTests
{
    [TestCase(null)]
    [TestCase("untitled.ecs")]
    public async Task PreviewAndSaveUseTheSameFrozenFrameAlignedSelection(string? scriptPath)
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
            using AudioLabelEditorViewModel vm = new(history, preview, dialogs, SynchronousUiDispatcher.Instance, () => scriptPath);
            await vm.CaptureRecentCommand.ExecuteAsync(null);
            vm.LabelName = "测试音效";
            vm.SelectionStart = 0.25;
            vm.SelectionEnd = 0.75;
            source.Emit(new byte[32000]);
            await vm.PlaySelectionCommand.ExecuteAsync(null);
            await vm.SaveCommand.ExecuteAsync(null);
            AudioLabel saved = AudioLabelStore.Load(Path.Combine(directory, "audiolabel", "测试音效.audiolabel", "label.json"));
            Assert.Multiple(() =>
            {
                Assert.That(dialogs.FolderRequests, Is.EqualTo(1), "Without a saved script, select the parent of audiolabel.");
                Assert.That(preview.Played!.Pcm.ToArray(), Is.EqualTo(pcm[8000..24000]));
                Assert.That(saved.Clip.Pcm.ToArray(), Is.EqualTo(preview.Played.Pcm.ToArray()));
                Assert.That(vm.IsPlaying, Is.False);
                Assert.That(vm.IsBusy, Is.False);
                Assert.That(vm.PlaybackPosition, Is.EqualTo(0.75));
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

    [TestCase(false, "脚本 文件夹")]
    [TestCase(true, "脚本 文件夹")]
    [TestCase(false, "audiolabel")]
    public async Task SavedScriptCreatesOrReusesItsLabelDirectoryWithoutOpeningAPicker(bool exists, string folderName)
    {
        string root = Path.Combine(Path.GetTempPath(), "easycon-audio-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            string scriptDirectory = Path.Combine(root, folderName);
            Directory.CreateDirectory(scriptDirectory);
            string? scriptPath = null;
            FakeSource source = new();
            using AudioHistoryBuffer history = new(source);
            byte[] pcm = Enumerable.Repeat((byte)37, 32000).ToArray();
            source.Emit(pcm);
            FakeDialogs dialogs = new();
            using AudioLabelEditorViewModel vm = new(history, new FakePreview(), dialogs, SynchronousUiDispatcher.Instance, () => scriptPath);
            await vm.CaptureRecentCommand.ExecuteAsync(null);
            vm.LabelName = "闪光音效";
            // The editor may have been opened before the script was saved or switched.
            scriptPath = Path.Combine(scriptDirectory, "test.ecs");
            File.WriteAllText(scriptPath, "# audio label test");
            string labels = Path.Combine(scriptDirectory, "audiolabel");
            if (exists)
                AudioLabelStore.Save(labels, "已有音效", new AudioClip(source.State.Format!, pcm));

            await vm.SaveCommand.ExecuteAsync(null);

            string savedPath = Path.Combine(labels, "闪光音效.audiolabel", "label.json");
            AudioLabel saved = AudioLabelStore.Load(savedPath);
            Assert.Multiple(() =>
            {
                Assert.That(saved.Metadata.Name, Is.EqualTo("闪光音效"));
                Assert.That(saved.Clip.Pcm.ToArray(), Is.EqualTo(pcm));
                Assert.That(dialogs.FolderRequests, Is.Zero, "A saved script supplies the destination without a folder picker.");
                Assert.That(vm.IsBusy, Is.False);
            });
            if (exists)
                Assert.That(AudioLabelStore.Load(Path.Combine(labels, "已有音效.audiolabel", "label.json")).Clip.Pcm.ToArray(), Is.EqualTo(pcm));

            byte[] metadata = File.ReadAllBytes(savedPath);
            vm.SelectionEnd = 0.5;
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllBytes(savedPath), Is.EqualTo(metadata), "Saving the same name must preserve the existing label.");
                Assert.That(AudioLabelStore.Load(savedPath).Clip.Pcm.ToArray(), Is.EqualTo(pcm));
                Assert.That(vm.Status, Is.EqualTo("Text.AudioLabel.AlreadyExists"));
                Assert.That(dialogs.FolderRequests, Is.Zero);
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChoosingAnExistingAudioLabelFolderDoesNotNestIt(bool trailingSeparator)
    {
        string root = Path.Combine(Path.GetTempPath(), "easycon-audio-folder-" + Guid.NewGuid().ToString("N"));
        try
        {
            string labels = Path.Combine(root, "audiolabel");
            Directory.CreateDirectory(labels);
            FakeDialogs dialogs = new() { Folder = Task.FromResult<string?>(trailingSeparator ? labels + Path.DirectorySeparatorChar : labels) };
            FakeSource source = new();
            using AudioHistoryBuffer history = new(source);
            source.Emit(new byte[32000]);
            using AudioLabelEditorViewModel vm = new(history, new FakePreview(), dialogs, SynchronousUiDispatcher.Instance);
            await vm.CaptureRecentCommand.ExecuteAsync(null);
            vm.LabelName = "test";

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(AudioLabelStore.Load(Path.Combine(labels, "test.audiolabel", "label.json")).Clip.FrameCount, Is.EqualTo(8000));
                Assert.That(Directory.Exists(Path.Combine(labels, "audiolabel")), Is.False);
                Assert.That(dialogs.FolderRequests, Is.EqualTo(1));
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task CancellingTheFolderPickerKeepsTheSelectionReadyToSave()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakeDialogs dialogs = new();
        using AudioLabelEditorViewModel vm = new(history, new FakePreview(), dialogs, SynchronousUiDispatcher.Instance);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.LabelName = "cancelled";
        vm.SelectionStart = 0.25;
        vm.SelectionEnd = 0.75;
        string status = vm.Status;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(dialogs.FolderRequests, Is.EqualTo(1));
            Assert.That(vm.Status, Is.EqualTo(status));
            Assert.That(vm.SelectionStart, Is.EqualTo(0.25));
            Assert.That(vm.SelectionEnd, Is.EqualTo(0.75));
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(vm.SaveCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task AFileBlockingTheAudioLabelDirectoryIsReportedWithoutReplacingIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "easycon-audio-blocked-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string blocked = Path.Combine(root, "audiolabel");
            File.WriteAllText(blocked, "keep this file");
            FakeSource source = new();
            using AudioHistoryBuffer history = new(source);
            source.Emit(new byte[32000]);
            FakeDialogs dialogs = new();
            using AudioLabelEditorViewModel vm = new(history, new FakePreview(), dialogs, SynchronousUiDispatcher.Instance,
                () => Path.Combine(root, "test.ecs"));
            await vm.CaptureRecentCommand.ExecuteAsync(null);
            vm.LabelName = "blocked";

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(blocked), Is.EqualTo("keep this file"));
                Assert.That(Directory.GetDirectories(root), Is.Empty);
                Assert.That(vm.Status, Is.EqualTo("Text.AudioLabel.Error"));
                Assert.That(vm.IsBusy, Is.False);
                Assert.That(vm.SaveCommand.CanExecute(null), Is.True);
                Assert.That(dialogs.FolderRequests, Is.Zero);
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task ClosingEditorCancelsPlaybackWithoutDisconnectingCapture()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakePreview preview = new() { WaitForCancellation = true };
        QueuedDispatcher ui = new();
        AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), ui);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(vm.CaptureRecentCommand.CanExecute(null), Is.False);
        preview.PositionChanged!(0.4);
        vm.Dispose();
        await playing.WaitAsync(TimeSpan.FromSeconds(3));
        ui.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(preview.Cancelled, Is.True);
            Assert.That(source.Disposed, Is.False);
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
            Assert.That(vm.PlaybackPosition, Is.Null);
        });
    }

    [Test]
    public async Task ClosingDuringFolderPickerDoesNotSaveOrLoseSource()
    {
        TaskCompletionSource<string?> folder = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        AudioLabelEditorViewModel vm = new(history, new FakePreview(), new FakeDialogs { Folder = folder.Task }, SynchronousUiDispatcher.Instance);
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
        using AudioLabelEditorViewModel vm = new(history, new FakePreview { IsSupported = false }, new FakeDialogs(), SynchronousUiDispatcher.Instance);
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
        using AudioLabelEditorViewModel vm = new(history, new FakePreview { IsSupported = false }, new FakeDialogs(), SynchronousUiDispatcher.Instance);
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
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), SynchronousUiDispatcher.Instance);
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

    [Test]
    public async Task PreviewTracksTheOriginalTimelineAndRetainsTheLastPositionWhenStopped()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[320000]);
        FakePreview preview = new() { WaitForCancellation = true };
        QueuedDispatcher ui = new();
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), ui);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.SelectionStart = 3;
        vm.SelectionEnd = 5;
        Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(3));
        preview.PositionChanged!(1.35);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(3), "Worker callbacks must be dispatched to the UI.");
        ui.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(vm.PlaybackPosition, Is.EqualTo(4.35).Within(0.00001));
            Assert.That(vm.SelectionStart, Is.EqualTo(3));
            Assert.That(vm.SelectionEnd, Is.EqualTo(5));
        });
        preview.PositionChanged(1.5);
        preview.PositionChanged(0);
        await vm.StopPreviewCommand.ExecuteAsync(null);
        await playing.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(vm.PlaybackPosition, Is.EqualTo(4.5), "Stop must retain the final report even before queued UI updates run.");
        vm.SelectionEnd = 6;
        ui.Drain();
        Assert.That(vm.PlaybackPosition, Is.EqualTo(4.5), "Editing a selection must preserve the stopped cursor.");

        source.Emit(new byte[32000]);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.Null, "A new snapshot must discard the old cursor.");
    }

    [Test]
    public async Task RestartIgnoresPendingUpdatesFromThePreviousPreview()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakePreview preview = new() { WaitForCancellation = true };
        QueuedDispatcher ui = new();
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), ui);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Task first = vm.PlaySelectionCommand.ExecuteAsync(null);
        Action<double> previousCallback = preview.PositionChanged!;
        previousCallback(0.8);
        await vm.StopPreviewCommand.ExecuteAsync(null);
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Task second = vm.PlaySelectionCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.Zero);
        previousCallback(0.9);
        ui.Drain();
        Assert.That(vm.PlaybackPosition, Is.Zero);
        preview.PositionChanged!(0.2);
        ui.Drain();
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.2));
        await vm.StopPreviewCommand.ExecuteAsync(null);
        await second.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestCase(8000)]
    [TestCase(44100)]
    [TestCase(48000)]
    [TestCase(96000)]
    public async Task PreviewPositionUsesTheSameSampleAlignmentAsThePlayedSelection(int sampleRate)
    {
        FakeSource source = new() { State = new(AudioCaptureStatus.Connected, Format: new AudioFormat(sampleRate, 2, 16, "pcm")) };
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[sampleRate * 4]);
        FakePreview preview = new() { WaitForCancellation = true };
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), SynchronousUiDispatcher.Instance);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.SelectionStart = 123.5 / sampleRate;
        vm.SelectionEnd = 456.5 / sampleRate;
        Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(123d / sampleRate).Within(1e-10));
        preview.PositionChanged!(double.NaN);
        preview.PositionChanged(double.PositiveInfinity);
        preview.PositionChanged(-1);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(123d / sampleRate).Within(1e-10));
        preview.PositionChanged(100);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(457d / sampleRate).Within(1e-10));
        preview.PositionChanged(0);
        await vm.StopPreviewCommand.ExecuteAsync(null);
        await playing.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(vm.PlaybackPosition, Is.EqualTo(457d / sampleRate).Within(1e-10), "A device clock reset must not rewind the cursor.");
    }

    [Test]
    public async Task PlaybackFailureRetainsPositionAndReenablesEditing()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        TaskCompletionSource failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePreview preview = new() { Completion = failure.Task };
        QueuedDispatcher ui = new();
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), ui);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Task playing = vm.PlaySelectionCommand.ExecuteAsync(null);
        preview.PositionChanged!(0.3);
        failure.SetException(new InvalidOperationException("Output disconnected"));
        await playing.WaitAsync(TimeSpan.FromSeconds(3));
        ui.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(vm.PlaybackPosition, Is.EqualTo(0.3));
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(vm.CanChangeSelection, Is.True);
            Assert.That(vm.Status, Is.EqualTo("Text.AudioLabel.Error"));
        });
    }

    [TestCase(null)]
    [TestCase(0.3)]
    [TestCase(0.8)]
    public async Task PauseAndResumeUseTheCursorWithoutChangingTheSavedSelection(double? seek)
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        byte[] pcm = Enumerable.Range(0, 32000).Select(i => (byte)(i % 251)).ToArray();
        source.Emit(pcm);
        FakePreview preview = new() { WaitForCancellation = true };
        QueuedDispatcher ui = new();
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), ui);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.SelectionStart = 0.25;
        vm.SelectionEnd = 0.9;
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Action<double> previousCallback = preview.PositionChanged!;
        previousCallback(0.25);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsPaused, Is.True);
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(vm.CanChangeSelection, Is.True);
            Assert.That(vm.PlaybackPosition, Is.EqualTo(0.5));
            Assert.That(vm.PreviewButtonText, Is.EqualTo("Text.AudioLabel.Resume"));
        });
        if (seek is double position)
            vm.SeekPreviewCommand.Execute(position);
        double resume = seek ?? 0.5;
        previousCallback(0.6);
        ui.Drain();
        Assert.That(vm.PlaybackPosition, Is.EqualTo(resume), "A paused cursor must ignore stale output callbacks.");
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(preview.Played!.Pcm.ToArray(), Is.EqualTo(pcm[((int)(resume * 8000) * 4)..28800]));
            Assert.That(vm.PlaybackPosition, Is.EqualTo(resume).Within(1e-10));
            Assert.That(vm.SelectionStart, Is.EqualTo(0.25));
            Assert.That(vm.SelectionEnd, Is.EqualTo(0.9));
        });
        await vm.StopPreviewCommand.ExecuteAsync(null);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.25), "Stop must reset the next start, unlike pause.");
        Assert.That(preview.Played!.Pcm.ToArray(), Is.EqualTo(pcm[8000..28800]));
        await vm.StopPreviewCommand.ExecuteAsync(null);
    }

    [Test]
    public async Task ChangingSelectionAfterPauseStartsAtTheNewSelectionAndSeekIsClamped()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakePreview preview = new() { WaitForCancellation = true };
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), SynchronousUiDispatcher.Instance);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        preview.PositionChanged!(0.6);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        vm.SelectionStart = 0.2;
        vm.SelectionEnd = 0.8;
        Assert.That(vm.IsPaused, Is.False);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.2));
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        vm.SeekPreviewCommand.Execute(-5);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.2));
        vm.SeekPreviewCommand.Execute(5);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.8));
        vm.SeekPreviewCommand.Execute(double.NaN);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.8));
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.False, "Resuming at the end must not unexpectedly rewind.");
        Assert.That(vm.IsPaused, Is.False);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.EqualTo(0.2), "A fresh preview after completion starts at selection start.");
        await vm.StopPreviewCommand.ExecuteAsync(null);
    }

    [Test]
    public async Task ReplacingPausedAudioWithTheSameLengthClearsItsResumePosition()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakePreview preview = new() { WaitForCancellation = true };
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), SynchronousUiDispatcher.Instance);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        preview.PositionChanged!(0.4);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        Assert.That(vm.IsPaused, Is.False);
        Assert.That(vm.PlaybackPosition, Is.Null);
        Assert.That(vm.PreviewButtonText, Is.EqualTo("Text.AudioLabel.Play"));
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPosition, Is.Zero);
        await vm.StopPreviewCommand.ExecuteAsync(null);
    }

    [Test]
    public async Task RecordingDisablesConflictingActionsAndReplacesTheSnapshotWithNewAudio()
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        FakePreview preview = new();
        using AudioLabelEditorViewModel vm = new(history, preview, new FakeDialogs(), SynchronousUiDispatcher.Instance);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.LabelName = "new";
        vm.ToggleRecordingCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsRecording, Is.True);
            Assert.That(vm.RecordingLimit, Is.EqualTo(15));
            Assert.That(vm.CaptureRecentCommand.CanExecute(null), Is.False);
            Assert.That(vm.OpenCommand.CanExecute(null), Is.False);
            Assert.That(vm.SaveCommand.CanExecute(null), Is.False);
            Assert.That(vm.TogglePreviewCommand.CanExecute(null), Is.False);
            Assert.That(vm.CanChangeSelection, Is.False);
            Assert.That(vm.ToggleRecordingCommand.CanExecute(null), Is.True);
        });
        byte[] recording = Enumerable.Repeat((byte)42, 16000).ToArray();
        source.Emit(recording);
        Task stopped = RecordingFinished(vm);
        vm.ToggleRecordingCommand.Execute(null);
        await stopped;
        await vm.PlaySelectionCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Duration, Is.EqualTo(0.5));
            Assert.That(vm.RecordingDuration, Is.EqualTo(0.5));
            Assert.That(vm.CanChangeSelection, Is.True);
            Assert.That(preview.Played!.Pcm.ToArray(), Is.EqualTo(recording));
            Assert.That(source.Disposed, Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedRecordingRetainsTheEditedSnapshotAndClosingNeverDisconnectsCapture(bool disconnect)
    {
        FakeSource source = new();
        using AudioHistoryBuffer history = new(source);
        source.Emit(new byte[32000]);
        using AudioLabelEditorViewModel vm = new(history, new FakePreview(), new FakeDialogs(), SynchronousUiDispatcher.Instance);
        await vm.CaptureRecentCommand.ExecuteAsync(null);
        vm.SelectionStart = 0.25;
        vm.ToggleRecordingCommand.Execute(null);
        source.Emit(new byte[8000]);
        Task stopped = RecordingFinished(vm);
        if (disconnect)
            await source.DisconnectAsync();
        else
            source.Emit([1]);
        await stopped;
        Assert.Multiple(() =>
        {
            Assert.That(vm.Duration, Is.EqualTo(1));
            Assert.That(vm.SelectionStart, Is.EqualTo(0.25));
            Assert.That(vm.Status, Is.EqualTo("Text.AudioLabel.Error"));
            Assert.That(vm.IsBusy, Is.False);
        });
        if (!disconnect)
        {
            vm.ToggleRecordingCommand.Execute(null);
            stopped = RecordingFinished(vm);
            vm.Dispose();
            await stopped;
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
        }
        Assert.That(source.Disposed, Is.False);
    }

    private static Task RecordingFinished(AudioLabelEditorViewModel vm)
    {
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.IsRecording) && !vm.IsRecording)
            {
                vm.PropertyChanged -= Changed;
                finished.TrySetResult();
            }
        }
        vm.PropertyChanged += Changed;
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pending = new();
        public bool IsOnUiThread => false;
        public void Post(Action action) => _pending.Enqueue(action);
        public void Drain() { while (_pending.TryDequeue(out Action? action)) action(); }
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class FakePreview : IAudioPreview
    {
        public bool IsSupported { get; init; } = true;
        public bool WaitForCancellation { get; init; }
        public Action? OnCancel { get; init; }
        public Task? Completion { get; init; }
        public Action<double>? PositionChanged { get; private set; }
        public AudioClip? Played { get; private set; }
        public bool Cancelled { get; private set; }
        public async Task PlayAsync(AudioClip clip, CancellationToken cancellationToken, Action<double>? positionChanged = null)
        {
            Played = clip;
            PositionChanged = positionChanged;
            if (Completion != null)
                await Completion;
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
        public AudioCaptureState State { get; set; } = new(AudioCaptureStatus.Connected, Format: new AudioFormat(8000, 2, 16, "pcm"));
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
        public int FolderRequests { get; private set; }
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null)
        {
            FolderRequests++;
            return Folder;
        }
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    }
}