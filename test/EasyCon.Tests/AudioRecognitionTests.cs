using EasyCon.Capture.Audio;
using System.Buffers.Binary;

namespace EasyCon.Tests;

[TestFixture]
public class AudioRecognitionTests
{
    [Test]
    public async Task PeakIsNonConsumingAndSurvivesMoreThanFifteenSecondsOfLowerScores()
    {
        AudioTestRig rig = new();
        using AudioRecognition recognition = rig.Open();
        Assert.That(recognition.Score, Is.Zero);
        rig.Feed(AudioTestRig.MakeSound(frequencyShift: 30));
        await AudioTestRig.UntilAsync(() => recognition.Score > 0);
        double first = recognition.Score;
        rig.Feed(rig.Sound, eraseAfterDelivery: true);
        await AudioTestRig.UntilAsync(() => recognition.Score >= rig.ReferenceScore() - 1e-8);
        double peak = recognition.Score;
        Assert.That(peak, Is.GreaterThan(first));
        Assert.That(recognition.Score, Is.EqualTo(peak));
        for (int second = 0; second < 16; second++)
        {
            rig.Feed(new byte[16000]);
            // Give the background consumer a chance to drain its bounded queue.
            await Task.Delay(40);
            Assert.That(recognition.Score, Is.EqualTo(peak));
        }
        Assert.That(rig.Time, Is.GreaterThan(AudioTestRig.Epoch + 15_000_000_000L));
    }

    [Test]
    public async Task AudioBeforeLoadAndDelayedCallbacksCannotCompleteTheNewTemplate()
    {
        AudioTestRig rig = new();
        rig.Feed(rig.Sound);
        long oldTimestamp = rig.Time;
        using AudioRecognition recognition = rig.Open();
        rig.Source.Emit(new AudioChunk(oldTimestamp, rig.Source.State.Format!, rig.Sound, rig.Sound.Length) { Session = rig.Source.State });
        rig.Advance(300);
        rig.Source.Emit(rig.Sound); // First half precedes LOAD and must be trimmed.
        await Task.Delay(50);
        Assert.That(recognition.Score, Is.Zero);
        rig.Feed(rig.Sound);
        await AudioTestRig.UntilAsync(() => recognition.Score > 98);
    }

    [Test]
    public async Task DisposedGenerationAndOldDevicePacketsCannotPolluteReload()
    {
        AudioTestRig rig = new();
        AudioRecognition old = rig.Open();
        Action<AudioChunk>? callbacks = rig.Source.SnapshotCallbacks();
        AudioCaptureState oldState = rig.Source.State;
        rig.Feed(rig.Sound);
        old.Dispose();
        rig.Source.Reconnect();
        using AudioRecognition next = rig.Open();
        rig.Advance(600);
        AudioChunk delayed = new(rig.Time, oldState.Format!, rig.Sound, rig.Sound.Length) { Session = oldState };
        callbacks?.Invoke(delayed);
        rig.Source.Emit(delayed);
        await Task.Delay(50);
        Assert.That(next.Score, Is.Zero);
        rig.Feed(rig.Sound);
        await AudioTestRig.UntilAsync(() => next.Score > 98);
        Assert.That(rig.Source.Subscribers, Is.EqualTo(1));
    }

    [TestCase("disconnect")]
    [TestCase("reconnect")]
    [TestCase("invalid")]
    [TestCase("overflow")]
    [TestCase("stall")]
    [TestCase("identity")]
    public async Task InputFailuresAreTerminalAndNeverReturnAnOldHighScore(string fault)
    {
        AudioTestRig rig = new();
        using AudioRecognition recognition = rig.Open();
        rig.Feed(rig.Sound);
        await AudioTestRig.UntilAsync(() => recognition.Score > 98);
        rig.Advance(10);
        switch (fault)
        {
            case "disconnect": rig.Source.SetState(new(AudioCaptureStatus.Disconnected)); break;
            case "reconnect": rig.Source.Reconnect(); break;
            case "invalid": rig.Source.Emit([1]); break;
            case "overflow": rig.Source.Emit(new byte[48000]); break;
            case "stall": rig.Advance(1001); break;
            case "identity": rig.Source.Emit(new AudioChunk(rig.Time, rig.Source.State.Format!, new byte[160], 160)); break;
        }
        Assert.Throws<InvalidOperationException>(() => _ = recognition.Score);
        await AudioTestRig.UntilAsync(() => rig.Source.Subscribers == 0);
        rig.Source.Reconnect();
        Assert.Throws<InvalidOperationException>(() => _ = recognition.Score);
        Assert.That(rig.Source.StateSubscribers, Is.Zero);
        Assert.That(rig.Source.Disposed, Is.False);
    }

    [Test]
    public async Task InvalidSamplesFailOnTheBackgroundConsumerAndAreVisibleToTheReader()
    {
        AudioTestRig rig = new(new AudioFormat(8000, 1, 32, "float"));
        using AudioRecognition recognition = rig.Open();
        byte[] invalid = new byte[3200];
        BinaryPrimitives.WriteSingleLittleEndian(invalid, float.NaN);
        rig.Feed(invalid);
        await AudioTestRig.UntilAsync(() => rig.Source.Subscribers == 0);
        Assert.That(Assert.Throws<InvalidOperationException>(() => _ = recognition.Score)!.Message, Does.Contain("无效音频"));
    }

    [Test]
    public async Task CancellationReleasesOnlyOwnedListenersEvenBeforeTheWorkerStarts()
    {
        AudioTestRig rig = new();
        using AudioHistoryBuffer editor = new(rig.Source);
        using CancellationTokenSource stop = new();
        using AudioRecognition recognition = rig.Open(stop.Token);
        stop.Cancel();
        await AudioTestRig.UntilAsync(() => rig.Source.Subscribers == 1);
        Assert.Throws<OperationCanceledException>(() => _ = recognition.Score);
        recognition.Dispose();
        recognition.Dispose();
        Assert.That(rig.Source.StateSubscribers, Is.EqualTo(1));
        Assert.That(rig.Source.Disposed, Is.False);
    }
}