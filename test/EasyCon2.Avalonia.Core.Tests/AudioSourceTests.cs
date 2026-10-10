using EasyCon.Capture.Audio;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Services;
using EasyScript;
using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class AudioSourceTests
{
    [SetUp]
    public void SetUpLocalization()
    {
        L10nBridge.Resolver = key => key switch
        {
            "Text.Audio.Format" => "{0:N0} Hz · {1} channels · {2} bit",
            "Text.Status.NotConnected" => "Not Connected",
            "Text.Status.Connecting" => "Connecting",
            "Text.Status.Connected" => "Connected",
            "Text.Status.ConnectFailed" => "Connection Failed",
            "Text.Btn.ConnectAudio" => "Connect Audio",
            "Text.Btn.DisconnectAudio" => "Disconnect Audio",
            "Text.Msg.AudioDisconnectError" => "Audio source disconnect error: {0}",
            "Text.Status.AudioSilent" => "Audio received, currently silent",
            _ => key,
        };
    }

    [TearDown]
    public void ClearLocalization()
    {
        L10nBridge.Resolver = null;
    }

    [Test]
    public void AudioLevelCalculator_Pcm16_ReportsPeakAndRms()
    {
        byte[] pcm =
        [
            0x00, 0x80, // -1.0
            0x00, 0x00, // 0.0
            0x00, 0x40, // 0.5
        ];

        AudioLevel level = AudioLevelCalculator.Calculate(
            pcm,
            new AudioFormat(48_000, 1, 16, "pcm"));

        Assert.Multiple(() =>
        {
            Assert.That(level.Peak, Is.EqualTo(1).Within(0.000001));
            Assert.That(level.Rms, Is.EqualTo(Math.Sqrt(1.25 / 3)).Within(0.000001));
            Assert.That(level.Silent, Is.False);
        });
    }

    [Test]
    public void AudioLevelCalculator_Float32_ClampsInvalidValues()
    {
        byte[] pcm = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(new[] { float.NaN, 2f, -0.25f }, 0, pcm, 0, pcm.Length);

        AudioLevel level = AudioLevelCalculator.Calculate(
            pcm,
            new AudioFormat(48_000, 2, 32, "float"));

        Assert.Multiple(() =>
        {
            Assert.That(level.Peak, Is.EqualTo(1).Within(0.000001));
            Assert.That(level.Rms, Is.EqualTo(Math.Sqrt((1 + 0.0625) / 3)).Within(0.000001));
            Assert.That(level.Silent, Is.False);
        });
    }

    [Test]
    public void AudioLevelCalculator_EmptyOrUnsupportedFormat_IsSilent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                AudioLevelCalculator.Calculate([], new AudioFormat(48_000, 2, 16, "pcm")),
                Is.EqualTo(new AudioLevel(0, 0, true)));
            Assert.That(
                AudioLevelCalculator.Calculate([0, 0, 0], new AudioFormat(48_000, 2, 12, "pcm")),
                Is.EqualTo(new AudioLevel(0, 0, true)));
            Assert.That(
                AudioLevelCalculator.Calculate([0, 0], new AudioFormat(0, 1, 16, "pcm")),
                Is.EqualTo(new AudioLevel(0, 0, true)));
        });
    }

    [Test]
    public async Task WasapiAudioCapture_StateObserverCanDisposeAfterDisconnect()
    {
        WasapiAudioCapture source = new();
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StateChanged += _ =>
        {
            source.Dispose();
            disposed.TrySetResult();
        };

        await source.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task AudioConnectionViewModel_RequiresExplicitSelection_AndWaitsForFirstData()
    {
        var source = new FakeAudioSource();
        var log = new FakeLogService();
        using var viewModel = new AudioConnectionViewModel(source, log, SynchronousUiDispatcher.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AudioSourceOptions, Has.Count.EqualTo(2));
            Assert.That(viewModel.SelectedAudioSource, Is.Null, "音频输入不得自动选择第一个设备");
        });

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);
        Assert.That(log.Messages, Does.Contain("Text.Msg.SelectAudioSource"));

        viewModel.SelectedAudioSource = viewModel.AudioSourceOptions[1];
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(source.LastConnectedId, Is.EqualTo("capture-2"));
            Assert.That(viewModel.IsConnectingAudioSource, Is.True);
            Assert.That(viewModel.IsAudioSourceConnected, Is.False, "仅接受启动不代表已收到采集数据");
        });

        source.EmitData(new AudioLevel(0.5, 0.25, false));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsConnectingAudioSource, Is.False);
            Assert.That(viewModel.IsAudioSourceConnected, Is.True);
            Assert.That(viewModel.AudioLevelText, Does.Contain("dBFS"));
            Assert.That(viewModel.AudioFormatText, Does.Contain("Hz"));
        });
    }

    [Test]
    public async Task AudioConnectionViewModel_ConnectionFailureReturnsToDisconnected()
    {
        var source = new FakeAudioSource { ConnectResult = false };
        var log = new FakeLogService();
        using var viewModel = new AudioConnectionViewModel(source, log, SynchronousUiDispatcher.Instance)
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsConnectingAudioSource, Is.False);
            Assert.That(viewModel.IsAudioSourceConnected, Is.False);
            Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
            Assert.That(log.Messages, Does.Contain("打开音频输入失败"));
        });
    }

    [Test]
    public async Task AudioConnectionViewModel_FailureClearsStaleFormatAndLevel()
    {
        var source = new FakeAudioSource();
        var log = new FakeLogService();
        using var viewModel = new AudioConnectionViewModel(source, log, SynchronousUiDispatcher.Instance)
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);
        source.EmitData(new AudioLevel(0.5, 0.25, false));
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AudioFormatText, Is.Not.Empty);
            Assert.That(viewModel.AudioLevelText, Does.Contain("dBFS"));
        });

        source.EmitFailure("设备已移除");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsAudioSourceConnected, Is.False);
            Assert.That(viewModel.AudioFormatText, Is.Empty);
            Assert.That(viewModel.AudioLevelText, Is.Empty);
        });
    }

    [Test]
    public async Task AudioConnectionViewModel_FailureBeforeConnectReturnsClearsConnectedState()
    {
        var source = new FakeAudioSource();
        source.AfterConnectStarted = () =>
        {
            source.EmitData(new AudioLevel(0.5, 0.25, false));
            source.EmitFailure("设备已移除");
        };
        var log = new FakeLogService();
        using var viewModel = new AudioConnectionViewModel(source, log, SynchronousUiDispatcher.Instance)
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Failed));
            Assert.That(viewModel.IsConnectingAudioSource, Is.False);
            Assert.That(viewModel.IsAudioSourceConnected, Is.False);
            Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
            Assert.That(viewModel.AudioFormatText, Is.Empty);
            Assert.That(viewModel.AudioLevelText, Is.Empty);
            Assert.That(log.Messages, Has.Exactly(1).EqualTo("设备已移除"));
        });
    }

    [Test]
    public async Task AudioConnectionViewModel_SlowOpenTimesOutAndIgnoresLateResult()
    {
        using ManualResetEventSlim releaseOpen = new(false);
        TaskCompletionSource opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeAudioSource
        {
            BeforeConnect = () =>
            {
                opening.TrySetResult();
                if (!releaseOpen.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The test did not release native open.");
            },
        };
        var log = new FakeLogService();
        var ui = new TrackingUiDispatcher();
        using var viewModel = new AudioConnectionViewModel(source, log, ui, TimeSpan.FromMilliseconds(50))
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };

        try
        {
            Task command = ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);
            await opening.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await command.WaitAsync(TimeSpan.FromSeconds(3));
            await ui.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsConnectingAudioSource, Is.False);
                Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
                Assert.That(log.Messages, Does.Contain("Text.Msg.AudioTimeout"));
            });

            releaseOpen.Set();
            await source.ConnectReturned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Disconnected));
                Assert.That(viewModel.IsAudioSourceConnected, Is.False);
                Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
            });

            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);
            source.EmitData(new AudioLevel(0.5, 0.25, false));
            Assert.That(viewModel.IsAudioSourceConnected, Is.True, "Timed-out attempts must not prevent a retry.");
        }
        finally
        {
            releaseOpen.Set();
        }
    }

    [Test]
    public async Task AudioConnectionViewModel_FirstDataWinsAtTimeoutBoundary()
    {
        TaskCompletionSource checkedTimeout = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeAudioSource();
        source.BeforeConditionalDisconnect = () =>
        {
            source.EmitData(new AudioLevel(0.5, 0.25, false));
            checkedTimeout.TrySetResult();
        };
        var log = new FakeLogService();
        var ui = new TrackingUiDispatcher();
        using var viewModel = new AudioConnectionViewModel(source, log, ui, TimeSpan.FromMilliseconds(50))
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);
        await checkedTimeout.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await ui.SecondInvocation.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
            Assert.That(viewModel.IsAudioSourceConnected, Is.True);
            Assert.That(viewModel.IsConnectingAudioSource, Is.False);
            Assert.That(log.Messages, Does.Not.Contain("Text.Msg.AudioTimeout"));
        });
    }

    [Test]
    public async Task AudioConnectionViewModel_DelayedOldDisconnectCannotCancelRetryTimeout()
    {
        using ManualResetEventSlim releaseOpen = new(false);
        TaskCompletionSource retryOpening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeAudioSource { DelayConditionalDisconnectEvent = true };
        int opens = 0;
        source.BeforeConnect = () =>
        {
            if (Interlocked.Increment(ref opens) == 1)
                source.EmitConnecting();
            else
                retryOpening.TrySetResult();
            if (!releaseOpen.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not release native open.");
        };
        var log = new FakeLogService();
        var ui = new TrackingUiDispatcher();
        using var viewModel = new AudioConnectionViewModel(source, log, ui, TimeSpan.FromMilliseconds(150))
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };
        var command = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand;

        try
        {
            await command.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3));
            await ui.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Task retry = command.ExecuteAsync(null);
            await retryOpening.Task.WaitAsync(TimeSpan.FromSeconds(3));
            source.EmitDelayedDisconnection();
            Assert.That(viewModel.IsConnectingAudioSource, Is.True, "The old terminal event must not cancel the new attempt.");
            await retry.WaitAsync(TimeSpan.FromSeconds(3));
            await ui.SecondInvocation.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
        }
        finally
        {
            releaseOpen.Set();
        }
    }

    [Test]
    public async Task AudioConnectionViewModel_TimeoutRecoversBeforeNativeCleanupAndReportsItsFailure()
    {
        TaskCompletionSource<bool> cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeAudioSource { ConditionalDisconnectCompletion = cleanup.Task };
        var log = new FakeLogService();
        TaskCompletionSource timedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cleanupFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        log.LogAppended += (message, _) =>
        {
            if (message == "Text.Msg.AudioTimeout")
                timedOut.TrySetResult();
            else if (message == "Audio source disconnect error: Native cleanup failed.")
                cleanupFailed.TrySetResult();
        };
        using var viewModel = new AudioConnectionViewModel(source, log, SynchronousUiDispatcher.Instance, TimeSpan.FromMilliseconds(50))
        {
            SelectedAudioSource = new AudioDeviceInfo("capture-1", "Capture 1"),
        };

        try
        {
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ConnectAudioSourceCommand).ExecuteAsync(null);
            await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(cleanup.Task.IsCompleted, Is.False);
                Assert.That(viewModel.IsConnectingAudioSource, Is.False);
                Assert.That(viewModel.IsAudioSourceConnected, Is.False);
                Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
            });

            cleanup.SetException(new IOException("Native cleanup failed."));
            await cleanupFailed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(viewModel.AudioSourceStatus, Is.EqualTo("Connection Failed"));
        }
        finally
        {
            cleanup.TrySetResult(true);
        }
    }

    private sealed class TrackingUiDispatcher : IUiDispatcher
    {
        public bool IsOnUiThread => true;
        private int _invocations;
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondInvocation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            CompleteInvocation();
            return Task.CompletedTask;
        }

        public async Task InvokeAsync(Func<Task> action)
        {
            await action();
            CompleteInvocation();
        }

        private void CompleteInvocation()
        {
            if (Interlocked.Increment(ref _invocations) >= 2)
                SecondInvocation.TrySetResult();
            Invoked.TrySetResult();
        }
    }

    private sealed class FakeAudioSource : IAudioCaptureSource
    {
        private readonly IReadOnlyList<AudioDeviceInfo> _devices =
        [
            new AudioDeviceInfo("capture-1", "Capture 1"),
            new AudioDeviceInfo("capture-2", "Capture 2"),
        ];

        public bool ConnectResult { get; init; } = true;
        public Action? BeforeConnect { get; set; }
        public Action? AfterConnectStarted { get; set; }
        public Action? BeforeConditionalDisconnect { get; set; }
        public bool DelayConditionalDisconnectEvent { get; init; }
        public Task<bool>? ConditionalDisconnectCompletion { get; init; }
        private AudioCaptureState? _delayedDisconnected;
        public TaskCompletionSource ConnectReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastConnectedId { get; private set; }
        public AudioCaptureState State { get; private set; } = new(AudioCaptureStatus.Disconnected);

        public bool IsSupported => true;

        public event Action<AudioCaptureState>? StateChanged;
        public event Action<AudioLevel>? LevelChanged;
        public event Action<AudioChunk>? DataAvailable;

        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => _devices;

        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default)
        {
            try
            {
                BeforeConnect?.Invoke();
                if (cancellationToken.IsCancellationRequested)
                    return false;

                LastConnectedId = deviceId;
                State = new AudioCaptureState(AudioCaptureStatus.Connecting, _devices.First(device => device.Id == deviceId),
                    new AudioFormat(48_000, 2, 32, "float"));
                StateChanged?.Invoke(State);
                AfterConnectStarted?.Invoke();
                if (!ConnectResult)
                {
                    State = new AudioCaptureState(AudioCaptureStatus.Failed, State.Device, State.Format, "打开音频输入失败");
                    StateChanged?.Invoke(State);
                    return false;
                }

                return true;
            }
            finally
            {
                ConnectReturned.TrySetResult();
            }
        }

        public Task DisconnectAsync()
        {
            State = new AudioCaptureState(AudioCaptureStatus.Disconnected);
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public Task<bool> DisconnectIfConnectingAsync()
        {
            BeforeConditionalDisconnect?.Invoke();
            if (State.Status != AudioCaptureStatus.Connecting)
                return Task.FromResult(false);

            State = new AudioCaptureState(AudioCaptureStatus.Disconnected);
            if (DelayConditionalDisconnectEvent)
                _delayedDisconnected = State;
            else
                StateChanged?.Invoke(State);
            return ConditionalDisconnectCompletion ?? Task.FromResult(true);
        }

        public void EmitConnecting()
        {
            State = new AudioCaptureState(AudioCaptureStatus.Connecting);
            StateChanged?.Invoke(State);
        }

        public void EmitDelayedDisconnection()
        {
            if (_delayedDisconnected != null)
                StateChanged?.Invoke(_delayedDisconnected);
        }

        public void EmitData(AudioLevel level)
        {
            State = State with { Status = AudioCaptureStatus.Connected };
            StateChanged?.Invoke(State);
            LevelChanged?.Invoke(level);
            DataAvailable?.Invoke(new AudioChunk(1, State.Format!, new byte[] { 0, 0, 0, 0 }, 4));
        }

        public void EmitFailure(string error)
        {
            State = new AudioCaptureState(AudioCaptureStatus.Failed, State.Device, State.Format, error);
            StateChanged?.Invoke(State);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeLogService : ILogService
    {
        public Collection<string> Messages { get; } = [];
        public event Action<string?, string?>? LogAppended;

        public void AddLog(string message, string? color = null)
        {
            Messages.Add(message);
            LogAppended?.Invoke(message, color);
        }

        public void Clear() => Messages.Clear();
        public string? Read() => null;
        public void Print(string message, bool newline = true) => AddLog(message);
        public void Alert(string message) => AddLog(message);
        public string ReadLine() => "";
        public bool TryReadLine(out string line)
        {
            line = "";
            return false;
        }
    }
}