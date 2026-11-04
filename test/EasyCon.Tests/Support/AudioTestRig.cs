using EasyCon.Capture.Audio;
using System.Buffers.Binary;
using System.Diagnostics;

namespace EasyCon.Tests;

internal sealed class AudioTestRig
{
    public const long Epoch = 10_000_000_000;
    private long _time = Epoch;
    public long Time => Interlocked.Read(ref _time);
    public long ClockTicks => Time / 1_000_000_000L * Stopwatch.Frequency
        + Time % 1_000_000_000L * Stopwatch.Frequency / 1_000_000_000L;
    public TestAudioSource Source { get; }
    public byte[] Sound { get; } = MakeSound();
    public AudioMatchTemplate Template { get; }

    public AudioTestRig(AudioFormat? format = null)
    {
        Source = new TestAudioSource(format ?? new AudioFormat(8000, 1, 16, "pcm"), () => Time);
        Template = AudioMatchTemplate.Create(new AudioClip(new AudioFormat(8000, 1, 16, "pcm"), Sound))!;
    }

    public AudioRecognition Open(CancellationToken token = default)
        => new(Source, Template, token, () => ClockTicks);

    public void Advance(int ms) => Interlocked.Add(ref _time, ms * 1_000_000L);

    public void Feed(byte[] pcm, bool eraseAfterDelivery = false)
    {
        int block = Source.State.Format!.SampleRate / 10 * AudioClip.GetBlockAlign(Source.State.Format);
        for (int offset = 0; offset < pcm.Length; offset += block)
        {
            byte[] packet = pcm.AsSpan(offset, Math.Min(block, pcm.Length - offset)).ToArray();
            Advance(100);
            Source.Emit(packet);
            if (eraseAfterDelivery)
                Array.Clear(packet);
        }
    }

    public double ReferenceScore()
    {
        AudioTemplateMatcher matcher = new(Template, new AudioFormat(8000, 1, 16, "pcm"));
        double peak = 0;
        matcher.Append(Sound, score => peak = Math.Max(peak, score.Score * 100));
        return peak;
    }

    internal static byte[] MakeSound(int rate = 8000, double frequencyShift = 0)
    {
        byte[] pcm = new byte[(int)(rate * 0.6) * 2];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            double time = i / (double)rate;
            double frequency = (time < 0.2 ? 600 : time < 0.4 ? 1100 : 1800) + frequencyShift;
            double value = 0.6 * Math.Min(1, Math.Min(time * 100, (0.6 - time) * 100)) * Math.Sin(2 * Math.PI * frequency * time);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(value * short.MaxValue));
        }
        return pcm;
    }

    public static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}

internal sealed class TestAudioSource(AudioFormat format, Func<long> clock) : IAudioCaptureSource
{
    public bool IsSupported { get; set; } = true;
    private AudioCaptureState _state = new(AudioCaptureStatus.Connected, Format: format);
    public AudioCaptureState State => Volatile.Read(ref _state);
    public bool Disposed { get; private set; }
    public event Action<AudioChunk>? DataAvailable;
    public event Action<AudioCaptureState>? StateChanged;
    public int Subscribers => DataAvailable?.GetInvocationList().Length ?? 0;
    public int StateSubscribers => StateChanged?.GetInvocationList().Length ?? 0;
    public event Action<AudioLevel>? LevelChanged { add { } remove { } }
    public void Emit(byte[] pcm, AudioCaptureState? session = null)
        => Emit(new AudioChunk(clock(), format, pcm, pcm.Length) { Session = session ?? State });
    public void Emit(AudioChunk chunk) => DataAvailable?.Invoke(chunk);
    public Action<AudioChunk>? SnapshotCallbacks() => DataAvailable;
    public void Reconnect() => SetState(new(AudioCaptureStatus.Connected, Format: format));
    public void SetState(AudioCaptureState state)
    {
        Volatile.Write(ref _state, state);
        StateChanged?.Invoke(state);
    }
    public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
    public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => throw new AssertionException("Must borrow the connected source.");
    public Task DisconnectAsync() => throw new AssertionException("Must not disconnect the shared source.");
    public Task<bool> DisconnectIfConnectingAsync() => throw new AssertionException("Must not disconnect the shared source.");
    public void Dispose() => Disposed = true;
}