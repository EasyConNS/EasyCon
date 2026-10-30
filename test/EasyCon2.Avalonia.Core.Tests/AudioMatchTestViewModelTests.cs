using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.AudioLabels;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class AudioMatchTestViewModelTests
{
    private string _directory = null!;
    private string _labelPath = null!;
    private AudioClip _sound = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "easycon-audio-match-tests-" + Guid.NewGuid().ToString("N"));
        byte[] pcm = new byte[8000];
        for (int frame = 0; frame < pcm.Length / 2; frame++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(frame * 2), (short)(20000 * Math.Sin(frame * 2 * Math.PI * 700 / 8000)));
        _sound = new AudioClip(new AudioFormat(8000, 1, 16, "pcm"), pcm);
        _labelPath = AudioLabelStore.Save(_directory, "测试音效", _sound);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task SavedLabelCanListenStopAndRestartWithoutClosingTheDevice()
    {
        FakeSource source = new(_sound.Format);
        FakeDialogs dialogs = new() { Files = [_labelPath] };
        using AudioMatchTestViewModel vm = new(source, dialogs, SynchronousUiDispatcher.Instance);
        Assert.That(vm.StartCommand.CanExecute(null), Is.False);
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.That(vm.HasTemplate, Is.True);
        Assert.That(vm.LabelName, Is.EqualTo("测试音效"));
        Task listening = vm.StartCommand.ExecuteAsync(null);
        try
        {
            Assert.That(vm.OpenCommand.CanExecute(null), Is.False);
            source.Emit(_sound.Pcm.ToArray());
            await UntilAsync(() => vm.Hits.Count == 1);
            Assert.That(vm.PeakScore, Is.GreaterThan(98).And.LessThanOrEqualTo(100));
            Assert.That(vm.ScoreText, Is.EqualTo(vm.Score!.Value.ToString()));
            Assert.That(vm.PeakScoreText, Is.EqualTo(vm.PeakScore.ToString()));
            Assert.That(vm.Hits[0].Score, Is.InRange(85, vm.PeakScore));
            Assert.That(vm.Hits[0].Threshold, Is.EqualTo(85));
            Assert.That(vm.Hits[0].Time, Does.Match(@"^\d{2}:\d{2}:\d{2}\.\d{3}$"));
            vm.Threshold = 91;
            vm.CooldownSeconds = 2;
            vm.StopCommand.Execute(null);
            await listening.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.Score, Is.Null);
            Assert.That(vm.Hits, Has.Count.EqualTo(1));
            Assert.That(source.SubscriberCount, Is.Zero);
            Assert.That(source.Disposed, Is.False);
            listening = vm.StartCommand.ExecuteAsync(null);
            Assert.That(vm.HitCount, Is.Zero);
            Assert.That(vm.Hits, Is.Empty);
        }
        finally
        {
            vm.Dispose();
            await listening.WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.That(source.SubscriberCount, Is.Zero);
        Assert.That(source.Disposed, Is.False);
    }

    [Test]
    public async Task InvalidReplacementDisarmsTheOldTemplateAndCancelPreservesIt()
    {
        FakeDialogs dialogs = new() { Files = [_labelPath] };
        using AudioMatchTestViewModel vm = new(new FakeSource(_sound.Format), dialogs, SynchronousUiDispatcher.Instance);
        await vm.OpenCommand.ExecuteAsync(null);
        dialogs.Files = [];
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.That(vm.HasTemplate, Is.True);
        dialogs.Files = [Path.Combine(_directory, "missing", "label.json")];
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.That(vm.HasTemplate, Is.False);
        Assert.That(vm.StartCommand.CanExecute(null), Is.False);
        string silent = AudioLabelStore.Save(_directory, "silent", new AudioClip(_sound.Format, new byte[_sound.Pcm.Length]));
        dialogs.Files = [silent];
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.That(vm.HasTemplate, Is.False);
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task ClosingWithAnUpdateQueuedCannotAddAHitOrRetainSubscriptions()
    {
        FakeSource source = new(_sound.Format);
        QueuedDispatcher dispatcher = new();
        using AudioMatchTestViewModel vm = new(source, new FakeDialogs { Files = [_labelPath] }, dispatcher);
        await vm.OpenCommand.ExecuteAsync(null);
        Task listening = vm.StartCommand.ExecuteAsync(null);
        source.Emit(_sound.Pcm.ToArray());
        try
        {
            await UntilAsync(() => !dispatcher.Actions.IsEmpty);
            vm.Dispose();
            dispatcher.Drain();
            await listening.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.HitCount, Is.Zero);
            Assert.That(source.SubscriberCount, Is.Zero);
            Assert.That(source.Disposed, Is.False);
        }
        finally
        {
            vm.Dispose();
            dispatcher.Drain();
            await listening.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Test]
    public async Task UnsupportedPlatformsCanLoadLabelsButCannotListen()
    {
        using AudioMatchTestViewModel vm = new(new FakeSource(_sound.Format) { IsSupported = false },
            new FakeDialogs { Files = [_labelPath] }, SynchronousUiDispatcher.Instance);
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.That(vm.HasTemplate, Is.True);
        Assert.That(vm.StartCommand.CanExecute(null), Is.False);
        Assert.That(vm.Threshold, Is.EqualTo(85));
        vm.Threshold = int.MinValue;
        Assert.That(vm.Threshold, Is.EqualTo(10));
        vm.Threshold = int.MaxValue;
        Assert.That(vm.Threshold, Is.EqualTo(100));
        vm.CooldownSeconds = double.PositiveInfinity;
        Assert.That(vm.CooldownSeconds, Is.EqualTo(1));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeSource(AudioFormat format) : IAudioCaptureSource
    {
        public bool IsSupported { get; init; } = true;
        public AudioCaptureState State { get; } = new(AudioCaptureStatus.Connected, Format: format);
        public bool Disposed { get; private set; }
        private Action<AudioChunk>? _data;
        public int SubscriberCount => _data?.GetInvocationList().Length ?? 0;
        public event Action<AudioChunk>? DataAvailable { add => _data += value; remove => _data -= value; }
        public event Action<AudioCaptureState>? StateChanged { add { } remove { } }
        public event Action<AudioLevel>? LevelChanged { add { } remove { } }
        public void Emit(byte[] pcm) => _data?.Invoke(new AudioChunk(0, State.Format!, pcm, pcm.Length));
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => false;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> DisconnectIfConnectingAsync() => Task.FromResult(false);
        public void Dispose() => Disposed = true;
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        public ConcurrentQueue<Action> Actions { get; } = new();
        public bool IsOnUiThread => false;
        public void Post(Action action) => Actions.Enqueue(action);
        public Task InvokeAsync(Action action)
        {
            TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Actions.Enqueue(() => { action(); completed.TrySetResult(); });
            return completed.Task;
        }
        public Task InvokeAsync(Func<Task> action) => throw new NotSupportedException();
        public void Drain() { while (Actions.TryDequeue(out Action? action)) action(); }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public IReadOnlyList<string> Files { get; set; } = [];
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null) => Task.FromResult(Files);
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}