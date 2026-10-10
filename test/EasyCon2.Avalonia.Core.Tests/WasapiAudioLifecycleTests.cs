using EasyCon.Capture.Audio;
using NAudio.Wave;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public sealed class WasapiAudioLifecycleTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

    [TestCase(AudioCaptureStatus.Connecting)]
    [TestCase(AudioCaptureStatus.Failed)]
    public async Task StateObserversCanDisposeDuringConnect(AudioCaptureStatus observedStatus)
    {
        ControlledCapture capture = new();
        using WasapiAudioCapture source = new(_ => observedStatus == AudioCaptureStatus.Failed
            ? throw new InvalidOperationException("The endpoint failed to open.")
            : new AudioCaptureSession(new AudioDeviceInfo("device", "Capture"), capture));
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StateChanged += state =>
        {
            if (state.Status != observedStatus)
                return;

            source.Dispose();
            disposed.TrySetResult();
        };

        await Task.Run(() => source.TryConnect("device")).WaitAsync(_timeout);
        await disposed.Task.WaitAsync(_timeout);
        Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Disconnected));
    }

    [Test]
    public async Task DataObserverDisposeDoesNotWaitForConcurrentDisconnectJoiningItsCallback()
    {
        using ManualResetEventSlim allowDispose = new(false);
        ControlledCapture capture = new();
        using WasapiAudioCapture source = new(_ => new AudioCaptureSession(new AudioDeviceInfo("device", "Capture"), capture));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.DataAvailable += _ =>
        {
            entered.TrySetResult();
            if (!allowDispose.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not release the data observer.");
            source.Dispose();
            disposed.TrySetResult();
        };

        Assert.That(source.TryConnect("device"), Is.True);
        capture.EmitInBackground();
        try
        {
            await entered.Task.WaitAsync(_timeout);
            Task disconnect = source.DisconnectAsync();
            await capture.Disposing.Task.WaitAsync(_timeout);
            allowDispose.Set();
            await disposed.Task.WaitAsync(_timeout);
            await disconnect.WaitAsync(_timeout);
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Disconnected));
        }
        finally
        {
            allowDispose.Set();
        }
    }

    [Test]
    public async Task DataObserverCannotSynchronouslyReenterConnectDuringConcurrentDisconnect()
    {
        using ManualResetEventSlim allowConnect = new(false);
        ControlledCapture capture = new();
        using WasapiAudioCapture source = new(_ => new AudioCaptureSession(new AudioDeviceInfo("device", "Capture"), capture));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource rejected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.DataAvailable += _ =>
        {
            entered.TrySetResult();
            if (!allowConnect.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not release the data observer.");

            try
            {
                source.TryConnect("device");
            }
            catch (InvalidOperationException)
            {
                rejected.TrySetResult();
            }
        };

        Assert.That(source.TryConnect("device"), Is.True);
        capture.EmitInBackground();
        try
        {
            await entered.Task.WaitAsync(_timeout);
            Task disconnect = source.DisconnectAsync();
            await capture.Disposing.Task.WaitAsync(_timeout);
            allowConnect.Set();
            await rejected.Task.WaitAsync(_timeout);
            await disconnect.WaitAsync(_timeout);
        }
        finally
        {
            allowConnect.Set();
        }
    }

    [Test]
    public async Task CancelWhileOpeningPreventsLateStartAndReleasesTheUninstalledSession()
    {
        using ManualResetEventSlim releaseOpen = new(false);
        TaskCompletionSource opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlledCapture capture = new();
        using WasapiAudioCapture source = new(_ =>
        {
            opening.TrySetResult();
            if (!releaseOpen.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not release native open.");
            return new AudioCaptureSession(new AudioDeviceInfo("device", "Capture"), capture);
        });
        using CancellationTokenSource cancellation = new();
        Task<bool> connection = Task.Run(() => source.TryConnect("device", cancellation.Token));
        try
        {
            await opening.Task.WaitAsync(_timeout);
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connecting));
            Task<bool> disconnect = source.DisconnectIfConnectingAsync();
            await cancellation.CancelAsync();
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Disconnected));
            releaseOpen.Set();

            Assert.That(await connection.WaitAsync(_timeout), Is.False);
            Assert.That(await disconnect.WaitAsync(_timeout), Is.True);
            await capture.Disposed.Task.WaitAsync(_timeout);
            Assert.Multiple(() =>
            {
                Assert.That(capture.StartCount, Is.Zero);
                Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Disconnected));
            });
        }
        finally
        {
            releaseOpen.Set();
        }
    }

    [Test]
    public async Task FirstDataPreventsConditionalDisconnect()
    {
        ControlledCapture capture = new();
        using WasapiAudioCapture source = new(_ => new AudioCaptureSession(new AudioDeviceInfo("device", "Capture"), capture));
        Assert.That(source.TryConnect("device"), Is.True);
        capture.Emit();

        Assert.That(await source.DisconnectIfConnectingAsync().WaitAsync(_timeout), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
            Assert.That(capture.Disposed.Task.IsCompleted, Is.False);
        });
    }

    [Test]
    public async Task FailureClearsCaptureBeforeReconnectAndDoesNotStopNewSession()
    {
        ControlledCapture first = new();
        ControlledCapture second = new();
        int opens = 0;
        using WasapiAudioCapture source = new(_ => new AudioCaptureSession(new AudioDeviceInfo("device", "Capture"),
            Interlocked.Increment(ref opens) == 1 ? first : second));
        Assert.That(source.TryConnect("device"), Is.True);
        first.Fail();
        await first.Disposed.Task.WaitAsync(_timeout);
        Assert.That(source.TryConnect("device"), Is.True);
        second.Emit();

        Assert.Multiple(() =>
        {
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
            Assert.That(second.Disposed.Task.IsCompleted, Is.False);
        });
    }

    private sealed class ControlledCapture : IWaveIn
    {
        private readonly ManualResetEventSlim _callbackExited = new(true);

        public WaveFormat WaveFormat { get; set; } = new(48_000, 16, 1);
        public int StartCount { get; private set; }
        public TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording() => StartCount++;
        public void StopRecording() { }

        public void Emit() => DataAvailable?.Invoke(this, new WaveInEventArgs([0, 0x40], 2));
        public void Fail() => RecordingStopped?.Invoke(this, new StoppedEventArgs(new IOException("Endpoint disappeared.")));
        public void EmitInBackground()
        {
            _callbackExited.Reset();
            _ = Task.Run(() =>
            {
                try
                {
                    Emit();
                }
                finally
                {
                    _callbackExited.Set();
                }
            });
        }

        public void Dispose()
        {
            Disposing.TrySetResult();
            if (!_callbackExited.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The capture callback did not return during native disposal.");
            Disposed.TrySetResult();
        }
    }
}