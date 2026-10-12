using EasyCon.Capture.Audio;
using System.Buffers.Binary;

namespace EasyCon.Tests;

[TestFixture]
public class AudioDetectionSessionTests
{
    [TestCase(44100)]
    [TestCase(48000)]
    [TestCase(96000)]
    public void TimelineKeepsSampleAlignmentAtCaptureRates(int rate)
    {
        AudioTestRig rig = new(new AudioFormat(rate, 1, 16, "pcm"));
        using AudioDetectionSession session = rig.Open();
        rig.Feed(AudioTestRig.MakeSound(rate));
        Assert.That(session.Match("tone", 1000), Is.GreaterThan(0.98));
    }

    [Test]
    public void FirstPacketIsClippedAtRunBoundary()
    {
        AudioTestRig rig = new();
        rig.Advance(50);
        using AudioDetectionSession session = rig.Open();
        rig.Advance(50);
        rig.Source.Emit(rig.Sound[..1600]);
        rig.Feed(rig.Sound[1600..]);
        Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000), "Only 550 ms of this 600 ms template occurred inside the run.");
    }

    [Test]
    public void LookbackIsImmediateNonConsumingAndExcludesOldOrPartialTemplates()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.Feed(rig.Sound);
        long before = rig.Time;
        double expected = rig.ReferenceScore();
        Assert.That(session.Match("tone", 5000), Is.EqualTo(expected).Within(1e-12), "Partial run history is usable after template warmup.");
        Assert.That(session.Match("tone", 600), Is.EqualTo(expected).Within(1e-12), "Queries do not consume history.");
        Assert.That(rig.Time, Is.EqualTo(before), "AUDIO must not wait for new data.");
        Assert.Throws<InvalidOperationException>(() => session.Match("tone", 300), "A template crossing the left boundary is ineligible.");
        rig.Feed(new byte[rig.Sound.Length + 1600]);
        Assert.That(session.Match("tone", 600), Is.Zero);
        Assert.That(session.Match("tone", 1400), Is.GreaterThan(0.98));
    }

    [Test]
    public void RunsAndReconnectsCannotReuseOldCallbacks()
    {
        AudioTestRig rig = new();
        AudioCaptureState oldState = rig.Source.State;
        using (AudioDetectionSession first = rig.Open())
        {
            rig.Feed(rig.Sound);
            Assert.That(first.Match("tone", 1000), Is.GreaterThan(0.98));
            rig.Source.Reconnect();
            Assert.Throws<InvalidOperationException>(() => first.Match("tone", 1000));
        }
        Assert.That(rig.Source.Subscribers, Is.Zero);
        using (AudioDetectionSession second = rig.Open())
        {
            rig.Advance(100);
            rig.Source.Emit(rig.Sound[..1600], oldState);
            Assert.Throws<InvalidOperationException>(() => second.Match("tone", 1000));
            rig.Feed(rig.Sound);
            Assert.That(second.Match("tone", 1000), Is.GreaterThan(0.98));
        }
        using AudioDetectionSession third = rig.Open();
        Assert.Throws<InvalidOperationException>(() => third.Match("tone", 1000));
        Assert.That(rig.Source.Disposed, Is.False);
    }

    [Test]
    public void BufferOwnsPcmAndRejectsStaleOrEvictedHistory()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.Feed(rig.Sound, eraseAfterDelivery: true);
        Assert.That(session.Match("tone", 1000), Is.GreaterThan(0.98));
        rig.Advance(300);
        Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000), "An old high score cannot hide a missing tail of the query window.");
        rig.Advance(701);
        Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000));
    }

    [Test]
    public void WaitIgnoresPreCallSoundAndTimesOutOnce()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.Feed(rig.Sound);
        long start = rig.Time;
        rig.PlayOnDelay(new byte[16000]);
        Assert.That(session.Wait("tone", 1000), Is.False);
        Assert.That(rig.Time - start, Is.EqualTo(1_000_000_000));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1), "The independent wait listener was removed; run history remains.");
    }

    [TestCase(8000, 16, "pcm")]
    [TestCase(44100, 16, "pcm")]
    [TestCase(96000, 16, "pcm")]
    [TestCase(96000, 32, "float")]
    public void WaitFinishesQueuedTailWhenPollingResumesJustAfterTimeout(int rate, int bitsPerSample, string encoding)
    {
        AudioTestRig rig = new(new AudioFormat(rate, 1, bitsPerSample, encoding));
        using AudioDetectionSession session = rig.Open();
        rig.PlayOnDelay(new byte[rate * bitsPerSample / 8], packetMilliseconds: 10);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 990_000_000)
                rig.Advance(15); // The last pre-deadline packet arrives between worker polls.
        };

        Assert.That(session.Wait("tone", 1000), Is.False);
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch + 1_005_000_000));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
        Assert.That(rig.Source.StateSubscribers, Is.EqualTo(1));
        Assert.That(rig.Source.Disposed, Is.False);
    }

    [Test]
    public void WaitCanMatchTheLastPreDeadlinePacketDuringCompletion()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        // The complete template ends at 990 ms; the last feature needs that final 10 ms packet.
        rig.PlayOnDelay([.. new byte[6240], .. rig.Sound], packetMilliseconds: 10);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 990_000_000)
                rig.Advance(15);
        };

        Assert.That(session.Wait("tone", 1000, rig.ReferenceScore()), Is.True);
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch + 1_005_000_000));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [TestCase(3, false)]
    [TestCase(30, true)]
    public void WaitChecksTheCompletionBudgetDuringAnalysis(int clockStepMilliseconds, bool expires)
    {
        AudioTestRig rig = new();
        bool analysisInProgress = false;
        using AudioDetectionSession session = rig.Open(() =>
        {
            if (analysisInProgress)
                rig.Advance(clockStepMilliseconds);
            return rig.Time;
        });
        rig.PlayOnDelay([.. new byte[6240], .. rig.Sound], packetMilliseconds: 10);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 990_000_000)
                analysisInProgress = true; // Clock keeps moving while the final packet is analyzed.
        };

        if (expires)
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 1000, rig.ReferenceScore()))!.Message,
                Does.Contain("处理宽限"));
        else
        {
            Assert.That(session.Wait("tone", 1000, rig.ReferenceScore()), Is.True);
            Assert.That(rig.Time, Is.GreaterThan(AudioTestRig.Epoch + 1_000_000_000)
                .And.LessThanOrEqualTo(AudioTestRig.Epoch + 1_250_000_000));
        }
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(10)]
    public void WaitCompletionNeverIncludesPacketsAtOrAfterTheListeningDeadline(int lateMilliseconds)
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.PlayOnDelay([.. new byte[6400], .. rig.Sound], packetMilliseconds: 10);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 990_000_000)
            {
                rig.Advance(10 + lateMilliseconds);
                deliver(); // This final piece would complete the matching template, but is too late.
            }
        };

        Assert.That(session.Wait("tone", 1000, rig.ReferenceScore()), Is.False);
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch + (1000 + lateMilliseconds) * 1_000_000L));
    }

    [Test]
    public void WaitStillRejectsSubstantialQueuedAudioAtTimeout()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.PlayOnDelay(new byte[16000]);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 700_000_000)
            {
                rig.Feed(new byte[4800]); // Worker stalls while 300 ms of eligible audio accumulates.
                rig.Advance(5);
            }
        };

        Assert.That(Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 1000))!.Message,
            Does.Contain("积压"));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [TestCase(260, false)]
    [TestCase(261, true)]
    public void WaitCompletionAllowanceIsBounded(int stallMilliseconds, bool expires)
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.PlayOnDelay(new byte[16000], packetMilliseconds: 10);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 990_000_000)
                rig.Advance(stallMilliseconds);
        };

        if (expires)
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 1000))!.Message,
                Does.Contain("处理宽限"));
        else
            Assert.That(session.Wait("tone", 1000), Is.False);
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WaitCompletionStillHonorsCancellationAndDisconnection(bool disconnect)
    {
        AudioTestRig rig = new();
        using CancellationTokenSource cancellation = new();
        using AudioDetectionSession session = rig.Open();
        rig.PlayOnDelay(new byte[16000], packetMilliseconds: 10);
        Action deliver = rig.AfterDelay!;
        rig.AfterDelay = () =>
        {
            deliver();
            if (rig.Time == AudioTestRig.Epoch + 990_000_000)
            {
                rig.Advance(15);
                if (disconnect)
                    rig.Source.Reconnect();
                else
                    cancellation.Cancel();
            }
        };

        if (disconnect)
            Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 1000, token: cancellation.Token));
        else
            Assert.Throws<OperationCanceledException>(() => session.Wait("tone", 1000, token: cancellation.Token));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
        Assert.That(rig.Source.StateSubscribers, Is.EqualTo(1));
        Assert.That(rig.Source.Disposed, Is.False);
    }

    [Test]
    public void WaitUsesTheRawDiagnosticScoreAndReturnsEarlyWithoutCooldown()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        double score = rig.ReferenceScore();
        rig.PlayOnDelay(rig.Sound);
        Assert.That(session.Wait("tone", 2000, score), Is.True);
        Assert.That(rig.Time, Is.EqualTo(AudioTestRig.Epoch + 600_000_000));
        rig.PlayOnDelay(rig.Sound);
        Assert.That(session.Wait("tone", 2000, score), Is.True, "A fresh call has no dedup/cooldown from a prior hit.");
        rig.PlayOnDelay([.. rig.Sound, .. new byte[6400]]);
        Assert.That(session.Wait("tone", 1000, Math.BitIncrement(score)), Is.False, "No rounding before threshold comparison.");
    }

    [TestCase(0.1)]
    [TestCase(1.0)]
    public void BoundaryThresholdsAreAccepted(double threshold)
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        rig.PlayOnDelay(new byte[16000]);
        Assert.That(session.Wait("tone", 1000, threshold), Is.False);
    }

    [TestCase(0)]
    [TestCase(0.099)]
    [TestCase(1.001)]
    [TestCase(85)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidThresholdsAreErrorsAndDoNotLeakSubscriptions(double threshold)
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Wait("tone", 1000, threshold));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [Test]
    public void MissingAudioAndInsufficientWarmupCannotReturnFalseOrZero()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000));
        Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 200));
        rig.PlayOnDelay(new byte[6400]);
        Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 400));
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [Test]
    public void CancellationAndDisconnectReleaseWaitAndRunSubscriptions()
    {
        AudioTestRig rig = new();
        using CancellationTokenSource cancelled = new();
        using (AudioDetectionSession session = rig.Open())
        {
            rig.AfterDelay = () => cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => session.Wait("tone", 8000, token: cancelled.Token));
            Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
            rig.AfterDelay = () => rig.Source.Reconnect();
            Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 8000));
            Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
        }
        Assert.That(rig.Source.Subscribers, Is.Zero);
        Assert.That(rig.Source.StateSubscribers, Is.Zero);
        Assert.That(rig.Source.Disposed, Is.False);
    }

    [Test]
    public void DeadlineRejectsQueuedPreDeadlineWorkAndDoesNotAcceptLateAudio()
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        // Template preparation consumes the whole timeout while timely audio queues up.
        rig.LoadAction = () => rig.Feed(rig.Sound);
        Assert.Throws<InvalidOperationException>(() => session.Wait("tone", 600));
        rig.LoadAction = null;
        // The final 100 ms of this sound arrive exactly at the deadline and are ineligible.
        rig.PlayOnDelay([.. new byte[6400], .. rig.Sound]);
        long start = rig.Time;
        Assert.That(session.Wait("tone", 1000, rig.ReferenceScore()), Is.False);
        Assert.That(rig.Time - start, Is.EqualTo(1_000_000_000));
    }

    [Test]
    public void ClockDiscontinuityMalformedPacketsAndMemoryLossAreErrors()
    {
        AudioTestRig rig = new();
        using (AudioDetectionSession session = rig.Open())
        {
            rig.Feed(rig.Sound[..1600]);
            rig.Advance(500);
            rig.Source.Emit(rig.Sound[..1600]);
            Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000));
        }
        using (AudioDetectionSession session = rig.Open())
        {
            rig.Advance(10);
            rig.Source.Emit([0]);
            Assert.Throws<InvalidOperationException>(() => session.Match("tone", 1000));
        }
        AudioTestRig large = new(new AudioFormat(384000, 32, 32, "float"));
        using AudioDetectionSession bounded = large.Open();
        large.Feed(new byte[38400 * 128 * 4]);
        Assert.That(Assert.Throws<InvalidOperationException>(() => bounded.Match("tone", 500))!.Message, Does.Contain("缓存上限"));
    }

    [TestCase(0)]
    [TestCase(15001)]
    public void LookbackBoundsAreExplicit(int milliseconds)
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Match("tone", milliseconds));
    }

    [TestCase(0)]
    [TestCase(300001)]
    public void TimeoutBoundsAreExplicit(int milliseconds)
    {
        AudioTestRig rig = new();
        using AudioDetectionSession session = rig.Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Wait("tone", milliseconds));
    }
}

internal sealed class AudioTestRig
{
    public const long Epoch = 10_000_000_000;
    public long Time { get; private set; } = Epoch;
    public TestAudioSource Source { get; }
    public byte[] Sound { get; } = MakeSound();
    public AudioMatchTemplate Template { get; }
    public Action? AfterDelay { get; set; }
    public Action? LoadAction { get; set; }

    public AudioTestRig(AudioFormat? format = null)
    {
        Source = new TestAudioSource(format ?? new AudioFormat(8000, 1, 16, "pcm"), () => Time);
        Template = AudioMatchTemplate.Create(new AudioClip(new AudioFormat(8000, 1, 16, "pcm"), Sound))!;
    }

    public AudioDetectionSession Open(Func<long>? clock = null) => new(Source, (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        LoadAction?.Invoke();
        return Template;
    }, clock ?? (() => Time), (ms, token) =>
    {
        token.ThrowIfCancellationRequested();
        Advance(ms);
        AfterDelay?.Invoke();
    });

    public void Advance(int ms) => Time += ms * 1_000_000L;

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

    public void PlayOnDelay(byte[] pcm, int packetMilliseconds = 100)
    {
        AudioFormat format = Source.State.Format!;
        int packetBytes = format.SampleRate * packetMilliseconds / 1000 * AudioClip.GetBlockAlign(format);
        long next = Time + packetMilliseconds * 1_000_000L;
        int offset = 0;
        AfterDelay = () =>
        {
            if (Time < next || offset >= pcm.Length)
                return;
            int length = Math.Min(packetBytes, pcm.Length - offset);
            Source.Emit(pcm.AsSpan(offset, length).ToArray());
            offset += length;
            next += packetMilliseconds * 1_000_000L;
        };
    }

    public double ReferenceScore()
    {
        AudioTemplateMatcher matcher = new(Template, new AudioFormat(8000, 1, 16, "pcm"));
        double peak = 0;
        matcher.Append(Sound, score => peak = Math.Max(peak, score.Score));
        return peak;
    }

    internal static byte[] MakeSound(int rate = 8000)
    {
        byte[] pcm = new byte[(int)(rate * 0.6) * 2];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            double time = i / (double)rate;
            double frequency = time < 0.2 ? 600 : time < 0.4 ? 1100 : 1800;
            double value = 0.6 * Math.Min(1, Math.Min(time * 100, (0.6 - time) * 100)) * Math.Sin(2 * Math.PI * frequency * time);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(value * short.MaxValue));
        }
        return pcm;
    }
}

internal sealed class TestAudioSource(AudioFormat format, Func<long> clock) : IAudioCaptureSource
{
    public bool IsSupported => true;
    public AudioCaptureState State { get; private set; } = new(AudioCaptureStatus.Connected, Format: format);
    public bool Disposed { get; private set; }
    private Action<AudioChunk>? _data;
    private Action<AudioCaptureState>? _states;
    public int Subscribers => _data?.GetInvocationList().Length ?? 0;
    public int StateSubscribers => _states?.GetInvocationList().Length ?? 0;
    public event Action<AudioChunk>? DataAvailable { add => _data += value; remove => _data -= value; }
    public event Action<AudioCaptureState>? StateChanged { add => _states += value; remove => _states -= value; }
    public event Action<AudioLevel>? LevelChanged { add { } remove { } }
    public void Emit(byte[] pcm, AudioCaptureState? session = null)
        => _data?.Invoke(new AudioChunk(clock(), format, pcm, pcm.Length) { Session = session ?? State });
    public void Reconnect()
    {
        State = new AudioCaptureState(AudioCaptureStatus.Connected, Format: format);
        _states?.Invoke(State);
    }
    public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices() => [];
    public bool TryConnect(string deviceId, CancellationToken cancellationToken = default) => throw new AssertionException("Must borrow the connected source.");
    public Task DisconnectAsync() => throw new AssertionException("Must not disconnect the shared source.");
    public Task<bool> DisconnectIfConnectingAsync() => throw new AssertionException("Must not disconnect the shared source.");
    public void Dispose() => Disposed = true;
}