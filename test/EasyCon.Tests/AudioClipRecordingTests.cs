using EasyCon.Capture.Audio;

namespace EasyCon.Tests;

[TestFixture]
public class AudioClipRecordingTests
{
    [Test]
    public async Task RecordingStartsAtTheClickAndCopiesOnlyNewCompleteFrames()
    {
        AudioTestRig rig = new(new AudioFormat(8000, 2, 16, "pcm"));
        using AudioHistoryBuffer history = new(rig.Source);
        byte[] old = Enumerable.Repeat((byte)17, 3200).ToArray();
        rig.Source.Emit(old);
        AudioClip snapshot = history.Snapshot()!;
        using AudioClipRecording recording = new(rig.Source, () => rig.Time, false);
        rig.Source.Emit(old); // This queued block ended before the click.
        byte[] packet = Enumerable.Range(0, 3200).Select(i => (byte)(i % 251)).ToArray();
        rig.Advance(25); // The first 75 ms precedes the click.
        rig.Source.Emit(packet);
        rig.Advance(100);
        rig.Source.Emit(packet);
        recording.Stop();
        AudioClip clip = await recording.Completion;
        Array.Clear(packet);
        Assert.Multiple(() =>
        {
            Assert.That(clip.FrameCount, Is.EqualTo(1000));
            Assert.That(clip.Pcm.ToArray(), Is.EqualTo(Enumerable.Range(2400, 800).Concat(Enumerable.Range(0, 3200)).Select(i => (byte)(i % 251)).ToArray()));
            Assert.That(recording.Duration, Is.EqualTo(0.125));
            Assert.That(recording.ReachedLimit, Is.False);
            Assert.That(snapshot.Pcm.ToArray(), Is.EqualTo(old), "Recording must not change an existing snapshot.");
        });
        rig.Advance(100);
        rig.Source.Emit(old);
        Assert.That(recording.Duration, Is.EqualTo(0.125), "Stopped recordings must be frozen.");
    }

    [TestCase(8000, 2, 16)]
    [TestCase(384000, 32, 32)]
    public async Task AutomaticLimitKeepsTheBeginningInsteadOfRollingOver(int rate, int channels, int bits)
    {
        AudioTestRig rig = new(new AudioFormat(rate, channels, bits, "pcm"));
        using AudioClipRecording recording = new(rig.Source, () => rig.Time, false);
        int alignment = AudioClip.GetBlockAlign(rig.Source.State.Format!);
        int expected = (int)Math.Min(rate * 15L, AudioClip.MaximumBytes / alignment) * alignment;
        byte[] input = new byte[expected + rate / 10 * alignment];
        new Random(17).NextBytes(input);
        rig.Feed(input);
        AudioClip clip = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(clip.Pcm.ToArray(), Is.EqualTo(input[..expected]));
            Assert.That(recording.ReachedLimit, Is.True);
            Assert.That(clip.DurationSeconds, Is.EqualTo(recording.MaximumSeconds));
            Assert.That(clip.Pcm.Length, Is.LessThanOrEqualTo(AudioClip.MaximumBytes));
        });
    }

    [Test]
    public async Task WallClockLimitEndsRecordingEvenWithALateFinalPacket()
    {
        AudioTestRig rig = new();
        using AudioClipRecording recording = new(rig.Source, () => rig.Time, false);
        rig.Feed(new byte[8000 * 2 * 14]);
        rig.Advance(1000);
        recording.CheckInput();
        AudioClip clip = await recording.Completion;
        Assert.That(clip.DurationSeconds, Is.EqualTo(14));
        Assert.That(recording.ReachedLimit, Is.True);
    }

    [TestCase("reconnect")]
    [TestCase("invalid")]
    [TestCase("stall")]
    [TestCase("late-data")]
    public void InterruptedInputCannotBecomeASuccessfulPartialRecording(string interruption)
    {
        AudioTestRig rig = new();
        using AudioClipRecording recording = new(rig.Source, () => rig.Time, false);
        rig.Feed(new byte[1600]);
        switch (interruption)
        {
            case "reconnect": rig.Source.Reconnect(); break;
            case "invalid": rig.Advance(100); rig.Source.Emit([1]); break;
            case "stall": rig.Advance(1001); recording.CheckInput(); break;
            case "late-data": rig.Advance(1001); rig.Source.Emit(new byte[1600]); break;
        }
        recording.Stop();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await recording.Completion);
    }

    [Test]
    public void EmptyRecordingFailsAndDisposalOnlyReleasesItsOwnSubscriptions()
    {
        AudioTestRig rig = new();
        using AudioHistoryBuffer history = new(rig.Source);
        int subscribers = rig.Source.Subscribers;
        int states = rig.Source.StateSubscribers;
        using (AudioClipRecording empty = new(rig.Source, () => rig.Time, false))
        {
            empty.Stop();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await empty.Completion);
        }
        AudioClipRecording pending = new(rig.Source, () => rig.Time, false);
        pending.Dispose();
        pending.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(pending.Completion.IsCanceled, Is.True);
            Assert.That(rig.Source.Subscribers, Is.EqualTo(subscribers));
            Assert.That(rig.Source.StateSubscribers, Is.EqualTo(states));
            Assert.That(rig.Source.Disposed, Is.False);
            Assert.That(rig.Source.State.Status, Is.EqualTo(AudioCaptureStatus.Connected));
        });
        rig.Feed(new byte[1600]);
        Assert.That(history.Snapshot(), Is.Not.Null);
    }

    [Test]
    public async Task OldSessionCallbackCannotEnterNewRecording()
    {
        AudioTestRig rig = new();
        AudioCaptureState old = rig.Source.State;
        rig.Source.Reconnect();
        using AudioClipRecording recording = new(rig.Source, () => rig.Time, false);
        rig.Advance(100);
        rig.Source.Emit(new byte[1600], old);
        rig.Advance(100);
        byte[] current = Enumerable.Repeat((byte)42, 1600).ToArray();
        rig.Source.Emit(current);
        recording.Stop();
        Assert.That((await recording.Completion).Pcm.ToArray(), Is.EqualTo(current));
    }
}