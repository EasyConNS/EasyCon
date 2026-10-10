using EasyCon.Capture.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public sealed class SafeWasapiCaptureTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task ReadAndStopFailures_StillNotifyOnceAfterReleasingResources()
    {
        COMException readError = new("Endpoint disconnected while reading.");
        COMException stopError = new("Windows audio service stopped.");
        FakeWasapiCaptureClient client = new()
        {
            ReadException = readError,
            StopException = stopError,
        };
        using SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopEvents = 0;
        bool resourcesReleasedBeforeNotification = false;
        capture.RecordingStopped += (_, _) => throw new InvalidOperationException("Faulty observer.");
        capture.RecordingStopped += (_, args) =>
        {
            Interlocked.Increment(ref stopEvents);
            resourcesReleasedBeforeNotification = client.Disposed.Task.IsCompleted;
            stopped.TrySetResult(args);
        };

        capture.StartRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.StopRecording();
        capture.Dispose();
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.TypeOf<AggregateException>());
            Assert.That(((AggregateException)result.Exception!).Flatten().InnerExceptions,
                Is.EqualTo(new[] { readError, stopError }));
            Assert.That(resourcesReleasedBeforeNotification, Is.True);
            Assert.That(stopEvents, Is.EqualTo(1));
            Assert.That(client.StopCount, Is.EqualTo(1));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task NormalStop_HasNoErrorAndReleasesExactlyOnce()
    {
        FakeWasapiCaptureClient client = new();
        using SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopEvents = 0;
        capture.RecordingStopped += (_, args) =>
        {
            Interlocked.Increment(ref stopEvents);
            stopped.TrySetResult(args);
        };

        capture.StartRecording();
        await client.Started.Task.WaitAsync(_timeout);
        capture.StopRecording();
        capture.StopRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.Null);
            Assert.That(stopEvents, Is.EqualTo(1));
            Assert.That(client.InitializeCount, Is.EqualTo(1));
            Assert.That(client.StartCount, Is.EqualTo(1));
            Assert.That(client.StopCount, Is.EqualTo(1));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RequestedStopFailure_ReportsErrorAndStillReleasesResources()
    {
        COMException stopError = new("Windows audio service stopped.");
        FakeWasapiCaptureClient client = new() { StopException = stopError };
        using SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args);

        capture.StartRecording();
        await client.Started.Task.WaitAsync(_timeout);
        capture.StopRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.SameAs(stopError));
            Assert.That(client.StopCount, Is.EqualTo(1));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InitializationFailure_NotifiesAndReleasesWithoutStarting()
    {
        COMException initializationError = new("Endpoint is unavailable during initialization.");
        FakeWasapiCaptureClient client = new() { InitializeException = initializationError };
        using SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopEvents = 0;
        capture.RecordingStopped += (_, args) =>
        {
            Interlocked.Increment(ref stopEvents);
            stopped.TrySetResult(args);
        };

        capture.StartRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.SameAs(initializationError));
            Assert.That(stopEvents, Is.EqualTo(1));
            Assert.That(client.StartCount, Is.Zero);
            Assert.That(client.StopCount, Is.Zero);
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartFailure_StopsInitializedClientAndReleasesResources()
    {
        COMException startError = new("Endpoint is unavailable during start.");
        FakeWasapiCaptureClient client = new() { StartException = startError };
        using SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args);

        capture.StartRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.SameAs(startError));
            Assert.That(client.StopCount, Is.EqualTo(1));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Packets_PreservePcmFramesOrderAndSilentNullBuffer()
    {
        // Deliberately larger than BufferFrameCount; every little-endian PCM frame
        // must survive, and the following silent packet must contain fresh zeros.
        byte[] first = [0x34, 0x12, 0x00, 0x80, 0xff, 0x7f, 0x00, 0x00];
        byte[] last = [0xfe, 0xff, 0x01, 0x00];
        FakeWasapiCaptureClient client = new() { BufferFrameCount = 1 };
        client.Enqueue(first);
        client.EnqueueSilent(3);
        client.Enqueue(last);
        using SafeWasapiCapture capture = new(client);
        ConcurrentQueue<byte[]> packets = new();
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, _) => throw new InvalidOperationException("Faulty observer.");
        capture.DataAvailable += (_, args) =>
        {
            packets.Enqueue(args.Buffer.AsSpan(0, args.BytesRecorded).ToArray());
            if (packets.Count == 3)
                capture.StopRecording();
        };
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args);

        capture.StartRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.Null);
            Assert.That(packets.ToArray(), Is.EqualTo(new[] { first, new byte[6], last }));
            Assert.That(client.ReleaseCount, Is.EqualTo(3));
            Assert.That(client.Disposed.Task.IsCompleted, Is.True);
        });
    }

    [TestCase(8, false)]
    [TestCase(16, false)]
    [TestCase(24, false)]
    [TestCase(32, false)]
    [TestCase(32, true)]
    public async Task SilentPacketsUseTheCorrectZeroAmplitudeSample(int bits, bool floatingPoint)
    {
        FakeWasapiCaptureClient client = new()
        {
            MixFormat = floatingPoint ? WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1) : new WaveFormat(48_000, bits, 1),
        };
        client.EnqueueSilent(4);
        using SafeWasapiCapture capture = new(client);
        byte[]? packet = null;
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, args) =>
        {
            packet = args.Buffer.AsSpan(0, args.BytesRecorded).ToArray();
            capture.StopRecording();
        };
        capture.RecordingStopped += (_, _) => stopped.TrySetResult();

        capture.StartRecording();
        await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();
        byte[] expected = Enumerable.Repeat((byte)(bits == 8 ? 0x80 : 0), 4 * (bits / 8)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(packet, Is.EqualTo(expected));
            Assert.That(AudioLevelCalculator.Calculate(packet!, new AudioFormat(48_000, 1, bits, floatingPoint ? "float" : "pcm")),
                Is.EqualTo(new AudioLevel(0, 0, true)));
        });
    }

    [Test]
    public async Task DisposeFromDataCallback_ReturnsAndThreadThenFinishesCleanup()
    {
        FakeWasapiCaptureClient client = new();
        client.Enqueue([0x34, 0x12]);
        SafeWasapiCapture capture = new(client);
        TaskCompletionSource callbackReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool releasedInsideDataCallback = true;
        capture.DataAvailable += (_, _) =>
        {
            capture.Dispose();
            releasedInsideDataCallback = client.Disposed.Task.IsCompleted;
            callbackReturned.TrySetResult();
        };
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args);

        capture.StartRecording();
        await callbackReturned.Task.WaitAsync(_timeout);
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        // Keep the test bounded even if a regression leaves the callback joining
        // its own thread. Failed waits must not synchronously dispose that thread.
        await Task.Run(capture.Dispose).WaitAsync(_timeout);

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.Null);
            Assert.That(releasedInsideDataCallback, Is.False);
            Assert.That(client.Disposed.Task.IsCompleted, Is.True);
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DisposeFromStoppedCallback_DoesNotJoinItselfOrSkipLaterObservers()
    {
        COMException readError = new("Endpoint disconnected while reading.");
        FakeWasapiCaptureClient client = new() { ReadException = readError };
        SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool resourcesReleasedInCallback = false;
        capture.RecordingStopped += (_, _) =>
        {
            capture.Dispose();
            resourcesReleasedInCallback = client.Disposed.Task.IsCompleted;
            throw new InvalidOperationException("Faulty observer after dispose.");
        };
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args);

        capture.StartRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        await Task.Run(capture.Dispose).WaitAsync(_timeout);

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.SameAs(readError));
            Assert.That(resourcesReleasedInCallback, Is.True);
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ReleaseAndDisposeFailures_StillNotifyAfterAttemptingBothCleanupStages()
    {
        COMException releaseError = new("Packet release failed.");
        COMException disposeError = new("Native disposal failed.");
        FakeWasapiCaptureClient client = new()
        {
            ReleaseException = releaseError,
            DisposeException = disposeError,
        };
        client.Enqueue([0x34, 0x12]);
        using SafeWasapiCapture capture = new(client);
        TaskCompletionSource<StoppedEventArgs> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args);

        capture.StartRecording();
        StoppedEventArgs result = await stopped.Task.WaitAsync(_timeout);
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(result.Exception, Is.TypeOf<AggregateException>());
            Assert.That(((AggregateException)result.Exception!).Flatten().InnerExceptions,
                Is.EqualTo(new[] { releaseError, disposeError }));
            Assert.That(client.ReleaseCount, Is.EqualTo(1));
            Assert.That(client.StopCount, Is.EqualTo(1));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
        });
    }

    [TestCase(16, WaveFormatEncoding.Pcm)]
    [TestCase(32, WaveFormatEncoding.IeeeFloat)]
    public void ExtensibleMixFormat_ExposesStandardFormat(int bitsPerSample, WaveFormatEncoding encoding)
    {
        FakeWasapiCaptureClient client = new() { MixFormat = new WaveFormatExtensible(48_000, bitsPerSample, 2) };
        using SafeWasapiCapture capture = new(client);

        Assert.Multiple(() =>
        {
            Assert.That(capture.WaveFormat.Encoding, Is.EqualTo(encoding));
            Assert.That(capture.WaveFormat.SampleRate, Is.EqualTo(48_000));
            Assert.That(capture.WaveFormat.Channels, Is.EqualTo(2));
            Assert.That(capture.WaveFormat.BitsPerSample, Is.EqualTo(bitsPerSample));
            Assert.That(capture.WaveFormat.BlockAlign, Is.EqualTo(bitsPerSample / 8 * 2));
        });
    }

    [Test]
    public void DisposeBeforeStarting_ReleasesClientWithoutStopOrNotifications()
    {
        FakeWasapiCaptureClient client = new();
        SafeWasapiCapture capture = new(client);
        int stopEvents = 0;
        capture.RecordingStopped += (_, _) => stopEvents++;

        capture.Dispose();
        capture.StopRecording();
        capture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(client.InitializeCount, Is.Zero);
            Assert.That(client.StopCount, Is.Zero);
            Assert.That(client.DisposeCount, Is.EqualTo(1));
            Assert.That(stopEvents, Is.Zero);
            Assert.Throws<ObjectDisposedException>(capture.StartRecording);
        });
    }

    [Test]
    public void UnsupportedMixFormat_ReleasesClientEvenWhenConstructionFails()
    {
        FakeWasapiCaptureClient client = new() { MixFormat = WaveFormat.CreateALawFormat(8_000, 1) };

        Assert.Throws<NotSupportedException>(() => _ = new SafeWasapiCapture(client));

        Assert.That(client.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    [Explicit("Diagnostic for the pinned NAudio 2.2.1 implementation; use the safe adapter tests for regression coverage.")]
    [Category("Manual")]
    public void LegacyNAudio221_StopFailureSkipsRecordingStopped()
    {
        // Invoke the actual old thread body synchronously. Running it on a real
        // background Thread would allow its unhandled COM exception to kill the host.
        WasapiCapture capture = (WasapiCapture)RuntimeHelpers.GetUninitializedObject(typeof(WasapiCapture));
        bool stoppedRaised = false;
        capture.RecordingStopped += (_, _) => stoppedRaised = true;
        AudioClient client = new(new LegacyServiceStoppedClient());
        MethodInfo threadBody = typeof(WasapiCapture).GetMethod("CaptureThread", BindingFlags.Instance | BindingFlags.NonPublic)!;

        TargetInvocationException? escaped = Assert.Throws<TargetInvocationException>(() => threadBody.Invoke(capture, [client]));

        Assert.Multiple(() =>
        {
            Assert.That(escaped!.InnerException, Is.TypeOf<COMException>());
            Assert.That(stoppedRaised, Is.False);
        });
    }

    private sealed class LegacyServiceStoppedClient : IAudioClient
    {
        public int Initialize(AudioClientShareMode mode, AudioClientStreamFlags flags, long duration, long period,
            WaveFormat format, ref Guid guid) => 0;

        public int GetBufferSize(out uint size)
        {
            size = 0;
            throw ServiceStopped();
        }

        public long GetStreamLatency() => 0;

        public int GetCurrentPadding(out int padding)
        {
            padding = 0;
            return 0;
        }

        public int IsFormatSupported(AudioClientShareMode mode, WaveFormat format, IntPtr closest) => 0;

        public int GetMixFormat(out IntPtr format)
        {
            format = IntPtr.Zero;
            return 0;
        }

        public int GetDevicePeriod(out long duration, out long minimum)
        {
            duration = 0;
            minimum = 0;
            return 0;
        }

        public int Start() => 0;
        public int Stop() => throw ServiceStopped();
        public int Reset() => 0;
        public int SetEventHandle(IntPtr handle) => 0;

        public int GetService(Guid guid, out object service)
        {
            service = null!;
            return 0;
        }

        private static COMException ServiceStopped() => new("Windows audio service stopped.", unchecked((int)0x88890010));
    }

    private sealed class FakeWasapiCaptureClient : IWasapiCaptureClient
    {
        private readonly Queue<FakePacket> _packets = new();
        private IntPtr _packetBuffer;
        private int _initializeCount;
        private int _startCount;
        private int _stopCount;
        private int _releaseCount;
        private int _disposeCount;

        public WaveFormat MixFormat { get; init; } = new(48_000, 16, 1);
        public int BufferFrameCount { get; init; } = 4_800;
        public Exception? InitializeException { get; init; }
        public Exception? StartException { get; init; }
        public Exception? ReadException { get; init; }
        public Exception? ReleaseException { get; init; }
        public Exception? StopException { get; init; }
        public Exception? DisposeException { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InitializeCount => Volatile.Read(ref _initializeCount);
        public int StartCount => Volatile.Read(ref _startCount);
        public int StopCount => Volatile.Read(ref _stopCount);
        public int ReleaseCount => Volatile.Read(ref _releaseCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Enqueue(byte[] bytes)
        {
            _packets.Enqueue(new FakePacket(bytes.Length / MixFormat.BlockAlign, bytes, 0));
        }

        public void EnqueueSilent(int frames)
        {
            _packets.Enqueue(new FakePacket(frames, null, AudioClientBufferFlags.Silent));
        }

        public void Initialize(WaveFormat format)
        {
            Interlocked.Increment(ref _initializeCount);
            if (InitializeException != null)
                throw InitializeException;
        }

        public void Start()
        {
            Interlocked.Increment(ref _startCount);
            if (StartException != null)
                throw StartException;

            Started.TrySetResult();
        }

        public void Stop()
        {
            Interlocked.Increment(ref _stopCount);
            if (StopException != null)
                throw StopException;
        }

        public int GetNextPacketSize()
        {
            if (ReadException != null)
                throw ReadException;

            return _packets.TryPeek(out FakePacket? packet) ? packet.Frames : 0;
        }

        public IntPtr GetBuffer(out int framesAvailable, out AudioClientBufferFlags flags)
        {
            FakePacket packet = _packets.Peek();
            framesAvailable = packet.Frames;
            flags = packet.Flags;
            if (packet.Bytes != null)
            {
                _packetBuffer = Marshal.AllocHGlobal(packet.Bytes.Length);
                Marshal.Copy(packet.Bytes, 0, _packetBuffer, packet.Bytes.Length);
            }

            return _packetBuffer;
        }

        public void ReleaseBuffer(int framesAvailable)
        {
            Interlocked.Increment(ref _releaseCount);
            FreePacketBuffer();
            _packets.Dequeue();
            if (ReleaseException != null)
                throw ReleaseException;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            FreePacketBuffer();
            Disposed.TrySetResult();
            if (DisposeException != null)
                throw DisposeException;
        }

        private void FreePacketBuffer()
        {
            if (_packetBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_packetBuffer);
                _packetBuffer = IntPtr.Zero;
            }
        }

        private sealed record FakePacket(int Frames, byte[]? Bytes, AudioClientBufferFlags Flags);
    }
}