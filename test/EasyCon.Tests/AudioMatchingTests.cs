using EasyCon.Capture.Audio;
using System.Buffers.Binary;
using System.Diagnostics;

namespace EasyCon.Tests;

[TestFixture]
public class AudioMatchingTests
{
    [TestCase(8000, 16, 1)]
    [TestCase(44100, 24, 2)]
    [TestCase(48000, 32, 2)]
    [TestCase(96000, 16, 1)]
    public void FindsAQuieterTargetAtAnUnalignedOffsetAcrossFormats(int rate, int bits, int channels)
    {
        AudioClip reference = Sound(48000, 16, 2);
        AudioMatchTemplate template = AudioMatchTemplate.Create(reference)!;
        AudioClip actual = Sound(rate, bits, channels, gain: 0.12, lead: 0.237, tail: 0.4);
        List<AudioMatchScore> scores = Match(template, actual, 137);
        AudioMatchScore best = scores.MaxBy(score => score.Score);
        TestContext.Out.WriteLine($"{rate} Hz/{bits} bit/{channels} ch: {best}");
        Assert.That(best.Score, Is.GreaterThan(0.9));
        Assert.That(best.EndSeconds, Is.EqualTo(0.237 + reference.DurationSeconds).Within(0.035));
    }

    [Test]
    public void DifferentSoundsAndSilenceDoNotMatchAndReverseStereoDoesNotCancel()
    {
        AudioMatchTemplate template = AudioMatchTemplate.Create(Sound())!;
        AudioClip other = Sound(frequencyShift: 1400);
        AudioClip silent = new(other.Format, new byte[other.Pcm.Length]);
        Assert.That(Match(template, other, 1024).Max(score => score.Score), Is.LessThan(0.7));
        Assert.That(Match(template, silent, 1024).Max(score => score.Score), Is.Zero);
        // Sound() stores the right channel with opposite phase.
        Assert.That(Match(template, Sound(channels: 2), 257).Max(score => score.Score), Is.GreaterThan(0.98));
    }

    [Test]
    public void ArbitraryPacketBoundariesPreserveTheSameScores()
    {
        AudioMatchTemplate template = AudioMatchTemplate.Create(Sound())!;
        AudioClip actual = Sound(44100, 24, 2, lead: 0.113, tail: 0.2);
        double[] whole = Match(template, actual, actual.FrameCount).Select(score => score.Score).ToArray();
        double[] tiny = Match(template, actual, 37).Select(score => score.Score).ToArray();
        Assert.That(tiny, Is.EqualTo(whole).Within(1e-6));
    }

    [Test]
    public void SilenceInvalidSamplesAndTooShortReferencesAreRejected()
    {
        AudioFormat format = new(8000, 1, 32, "float");
        Assert.That(AudioMatchTemplate.Create(new AudioClip(format, new byte[8000 * 4])), Is.Null);
        byte[] dc = new byte[8000 * 4];
        for (int offset = 0; offset < dc.Length; offset += 4)
            BinaryPrimitives.WriteSingleLittleEndian(dc.AsSpan(offset), 0.5f);
        Assert.That(AudioMatchTemplate.Create(new AudioClip(format, dc)), Is.Null, "A DC offset is not a sound fingerprint.");
        byte[] invalid = new byte[8000 * 4];
        BinaryPrimitives.WriteSingleLittleEndian(invalid, float.NaN);
        Assert.Throws<InvalidDataException>(() => AudioMatchTemplate.Create(new AudioClip(format, invalid)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioMatchTemplate.Create(new AudioClip(format, new byte[80 * 4])));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioMatchTemplate.Create(new AudioClip(format, new byte[80001 * 4])));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => AudioMatchTemplate.Create(Sound(), cancelled.Token));
    }

    [Test]
    public void ALongSoundIsOneHitAndANewSoundCanRearmAfterCooldown()
    {
        AudioMatchTrigger trigger = new();
        AudioMatchSettings settings = new(85, 1);
        Assert.That(trigger.Observe(90, 1, settings), Is.True);
        Assert.That(trigger.Observe(99, 3, settings), Is.False);
        Assert.That(trigger.Observe(82, 3.1, settings), Is.False);
        Assert.That(trigger.Observe(90, 3.2, settings), Is.False);
        Assert.That(trigger.Observe(20, 3.3, settings), Is.False);
        Assert.That(trigger.Observe(90, 3.4, settings), Is.True);
        Assert.That(trigger.Observe(20, 3.5, settings), Is.False);
        Assert.That(trigger.Observe(90, 3.6, settings), Is.False);
        Assert.That(trigger.Observe(90, 4.5, settings), Is.True);
    }

    [TestCase(0, 0, false)]
    [TestCase(0.84, 84, false)]
    [TestCase(0.849, 84, false)]
    [TestCase(0.84999999, 84, false)]
    [TestCase(0.85, 85, true)]
    [TestCase(0.859, 85, true)]
    [TestCase(0.99999999, 99, true)]
    [TestCase(1, 100, true)]
    public void PercentageScoresRoundDownBeforeHitDecisions(double normalized, int expected, bool hit)
    {
        int score = AudioMatchMonitor.ToPercentage(normalized);
        Assert.That(score, Is.EqualTo(expected));
        AudioMatchTrigger trigger = new();
        Assert.That(trigger.Observe(score, 1.25, new AudioMatchSettings(85)), Is.EqualTo(hit));
    }

    [Test]
    public void MonitorReportsHitsOnlyFromNewAudioAndClearsStaleScores()
    {
        AudioClip sound = Sound();
        FakeSource source = new(sound.Format);
        long clock = Stopwatch.Frequency;
        source.Emit(sound.Pcm.ToArray()); // Captured before Start: must never be replayed into the monitor.
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!, () => clock);
        Assert.That(monitor.Poll().Score, Is.Null);
        source.Emit(sound.Pcm.ToArray());
        AudioMatchUpdate found = monitor.Poll();
        Assert.That(found.Hits, Has.Count.EqualTo(1));
        Assert.That(found.Score, Is.GreaterThan(98));
        Assert.That(monitor.Poll().Hits, Is.Empty);
        clock += 2 * Stopwatch.Frequency;
        AudioMatchUpdate stale = monitor.Poll();
        Assert.That(stale.Status, Is.EqualTo(AudioMatchStatus.Interrupted));
        Assert.That(stale.Score, Is.Null);
        Assert.That(stale.Hits, Is.Empty);
        source.Emit(sound.Pcm.ToArray()[..(sound.Pcm.Length / 2)]);
        Assert.That(monitor.Poll().Score, Is.Null, "Stale audio cannot complete the new half-window.");
        monitor.Dispose();
        Assert.That(source.SubscriberCount, Is.Zero);
        Assert.That(source.Disposed, Is.False);
    }

    [Test]
    public void CaptureMemoryIsCopiedAndDisconnectInvalidatesAnAlreadyComputedUpdate()
    {
        AudioClip sound = Sound();
        FakeSource source = new(sound.Format);
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!);
        byte[] borrowed = sound.Pcm.ToArray();
        source.Emit(borrowed);
        Array.Clear(borrowed);
        AudioMatchUpdate update = monitor.Poll();
        Assert.That(update.Hits, Has.Count.EqualTo(1));
        Assert.That(monitor.IsCurrent(update), Is.True);
        source.SetState(new(AudioCaptureStatus.Disconnected));
        Assert.That(monitor.IsCurrent(update), Is.False);
    }

    [Test]
    public void ReconnectAndDelayedOldStateNotificationsCannotMixSessions()
    {
        AudioClip sound = Sound();
        FakeSource source = new(sound.Format);
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!);
        byte[] pcm = sound.Pcm.ToArray();
        source.Emit(pcm[..(pcm.Length / 2)]);
        Assert.That(monitor.Poll().Score, Is.Null);
        AudioCaptureState disconnected = source.SetState(new(AudioCaptureStatus.Disconnected));
        source.SetState(new(AudioCaptureStatus.Connected, Format: sound.Format));
        source.Emit(pcm[(pcm.Length / 2)..]);
        Assert.That(monitor.Poll().Score, Is.Null);
        source.Notify(disconnected);
        source.Emit(pcm);
        Assert.That(monitor.Poll().Hits, Has.Count.EqualTo(1));
        source.SetState(new(AudioCaptureStatus.Disconnected));
        Assert.That(monitor.Poll().Score, Is.Null);
    }

    [Test]
    public void DelayedOldAudioCannotCompleteAReconnectedMatch()
    {
        AudioClip sound = Sound();
        FakeSource source = new(sound.Format);
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!);
        AudioCaptureState old = source.State;
        byte[] pcm = sound.Pcm.ToArray();
        source.Emit(pcm[..(pcm.Length / 2)]);
        monitor.Poll();
        source.SetState(new(AudioCaptureStatus.Connected, Format: sound.Format));
        source.Emit(pcm, old);
        AudioMatchUpdate delayed = monitor.Poll();
        Assert.Multiple(() =>
        {
            Assert.That(delayed.Score, Is.Null, "Old-session audio must not be recognized as a new sound after reconnecting.");
            Assert.That(delayed.Hits, Is.Empty);
        });

        source.Emit(pcm[..(pcm.Length / 2)]);
        Assert.That(monitor.Poll().Score, Is.Null);
        source.Emit([1], old);
        source.Emit(pcm[(pcm.Length / 2)..]);
        AudioMatchUpdate current = monitor.Poll();
        Assert.That(current.Score, Is.GreaterThan(98), "An invalid old packet must not discard the new session's partial template.");
        Assert.That(current.Hits, Has.Count.EqualTo(1));
    }

    [Test]
    public void OverflowAndMalformedPacketsDiscardPendingHits()
    {
        AudioClip sound = Sound();
        FakeSource source = new(sound.Format);
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!);
        source.Emit(sound.Pcm.ToArray());
        byte[] silence = new byte[2 * sound.Format.SampleRate * AudioClip.GetBlockAlign(sound.Format)];
        source.Emit(silence); // More than two seconds queued: the pending target must be discarded.
        AudioMatchUpdate overflow = monitor.Poll();
        Assert.That(overflow.Status, Is.EqualTo(AudioMatchStatus.Interrupted));
        Assert.That(overflow.Hits, Is.Empty);
        source.Emit(sound.Pcm.ToArray());
        source.Emit([0]); // Not a complete PCM frame.
        AudioMatchUpdate invalid = monitor.Poll();
        Assert.That(invalid.Status, Is.EqualTo(AudioMatchStatus.InvalidAudio));
        Assert.That(invalid.Score, Is.Null);
        Assert.That(invalid.Hits, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReconnectingWhileStateIsBeingReadPreservesNewAudio(bool stateNotification)
    {
        AudioClip sound = Sound();
        FakeSource source = new(sound.Format);
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!);
        monitor.Poll();
        AudioCaptureState previous = source.State;
        using ManualResetEventSlim snapshotTaken = new();
        using ManualResetEventSlim releaseSnapshot = new();
        using ManualResetEventSlim reconnectStarted = new();
        source.AfterStateSnapshot = () =>
        {
            snapshotTaken.Set();
            if (!releaseSnapshot.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The paused state read was not released.");
        };
        Task reader = Task.Factory.StartNew(() =>
        {
            if (stateNotification)
                source.Notify(previous);
            else
                monitor.Poll();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task? reconnect = null;
        try
        {
            Assert.That(snapshotTaken.Wait(TimeSpan.FromSeconds(3)), Is.True);
            reconnect = Task.Factory.StartNew(() =>
            {
                reconnectStarted.Set();
                source.SetState(new(AudioCaptureStatus.Connected, Format: sound.Format));
                source.Emit(sound.Pcm.ToArray());
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.That(reconnectStarted.Wait(TimeSpan.FromSeconds(3)), Is.True);
            // Delivery may finish, or wait behind the paused reader's lock; both orderings must preserve it.
            await Task.WhenAny(reconnect, Task.Delay(200));
            releaseSnapshot.Set();
            await Task.WhenAll(reader, reconnect).WaitAsync(TimeSpan.FromSeconds(3));
            AudioMatchUpdate current = monitor.Poll();
            Assert.That(current.Hits, Has.Count.EqualTo(1), "A stale state read must not erase the new connection's complete sound.");
            Assert.That(current.Score, Is.GreaterThan(98));
        }
        finally
        {
            releaseSnapshot.Set();
            await reader.WaitAsync(TimeSpan.FromSeconds(3));
            if (reconnect != null)
                await reconnect.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Test]
    public void LiveThresholdChangesApplyWithoutReplayingOldResults()
    {
        AudioClip sound = Sound(gain: 0.8);
        FakeSource source = new(sound.Format);
        using AudioMatchMonitor monitor = new(source, AudioMatchTemplate.Create(sound)!);
        monitor.Settings = new AudioMatchSettings(100, 0);
        source.Emit(Sound(frequencyShift: 20).Pcm.ToArray());
        AudioMatchUpdate first = monitor.Poll();
        Assert.That(first.Hits, Is.Empty);
        Assert.That(first.Score, Is.GreaterThan(60).And.LessThan(100));
        monitor.Settings = new AudioMatchSettings(60, 0);
        Assert.That(monitor.Poll().Hits, Is.Empty);
        source.Emit(new byte[sound.Pcm.Length]);
        monitor.Poll();
        source.Emit(Sound(frequencyShift: 20).Pcm.ToArray());
        Assert.That(monitor.Poll().Hits, Has.Count.EqualTo(1));
    }

    private static List<AudioMatchScore> Match(AudioMatchTemplate template, AudioClip clip, int packetFrames)
    {
        AudioTemplateMatcher matcher = new(template, clip.Format);
        List<AudioMatchScore> result = [];
        int alignment = AudioClip.GetBlockAlign(clip.Format);
        for (int offset = 0; offset < clip.Pcm.Length; offset += packetFrames * alignment)
            matcher.Append(clip.Pcm.Span.Slice(offset, Math.Min(packetFrames * alignment, clip.Pcm.Length - offset)), result.Add);
        return result;
    }

    private static AudioClip Sound(int rate = 8000, int bits = 16, int channels = 1,
        double gain = 0.6, double lead = 0, double tail = 0, double frequencyShift = 0)
    {
        AudioFormat format = new(rate, channels, bits, bits == 32 ? "float" : "pcm");
        int alignment = AudioClip.GetBlockAlign(format);
        byte[] pcm = new byte[(int)Math.Round((lead + 0.6 + tail) * rate) * alignment];
        for (int frame = 0; frame < pcm.Length / alignment; frame++)
        {
            double time = (frame / (double)rate) - lead;
            double frequency = (time < 0.2 ? 600 : time < 0.4 ? 1100 : 1800) + frequencyShift;
            double value = time is >= 0 and < 0.6
                ? gain * Math.Min(1, Math.Min(time * 100, (0.6 - time) * 100)) * Math.Sin(2 * Math.PI * frequency * time) : 0;
            for (int channel = 0; channel < channels; channel++)
            {
                double sample = channel % 2 == 0 ? value : -value;
                Span<byte> output = pcm.AsSpan((frame * alignment) + (channel * (bits / 8)));
                switch (bits)
                {
                    case 16: BinaryPrimitives.WriteInt16LittleEndian(output, (short)(sample * short.MaxValue)); break;
                    case 24:
                        int integer = (int)(sample * 8388607);
                        output[0] = (byte)integer;
                        output[1] = (byte)(integer >> 8);
                        output[2] = (byte)(integer >> 16);
                        break;
                    case 32: BinaryPrimitives.WriteSingleLittleEndian(output, (float)sample); break;
                }
            }
        }
        return new AudioClip(format, pcm);
    }

    private sealed class FakeSource(AudioFormat format) : IAudioCaptureSource
    {
        public bool IsSupported => true;
        private AudioCaptureState _state = new(AudioCaptureStatus.Connected, Format: format);
        public Action? AfterStateSnapshot;
        public AudioCaptureState State
        {
            get
            {
                AudioCaptureState state = Volatile.Read(ref _state);
                Interlocked.Exchange(ref AfterStateSnapshot, null)?.Invoke();
                return state;
            }
        }
        public bool Disposed { get; private set; }
        private Action<AudioChunk>? _dataAvailable;
        public int SubscriberCount => _dataAvailable?.GetInvocationList().Length ?? 0;
        public event Action<AudioChunk>? DataAvailable { add => _dataAvailable += value; remove => _dataAvailable -= value; }
        public event Action<AudioCaptureState>? StateChanged;
        public event Action<AudioLevel>? LevelChanged { add { } remove { } }
        public void Emit(byte[] pcm, AudioCaptureState? session = null)
            => _dataAvailable?.Invoke(new AudioChunk(0, (session ?? State).Format!, pcm, pcm.Length) { Session = session ?? State });
        public AudioCaptureState SetState(AudioCaptureState state) { Volatile.Write(ref _state, state); Notify(state); return state; }
        public void Notify(AudioCaptureState state) => StateChanged?.Invoke(state);
        public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
        public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => false;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> DisconnectIfConnectingAsync() => Task.FromResult(false);
        public void Dispose() => Disposed = true;
    }
}