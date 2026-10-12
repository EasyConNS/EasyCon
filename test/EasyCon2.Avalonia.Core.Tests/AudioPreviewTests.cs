using EasyCon.Capture.Audio;
using EasyCon2.Avalonia.AudioLabels;
using NAudio.CoreAudioApi;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
[NonParallelizable]
public class AudioPreviewTests
{
    [TestCase(48000, 2, 16)]
    [TestCase(96000, 1, 32)]
    [Explicit("Uses a real Windows output device with a silent clip.")]
    [Category("Manual")]
    public async Task DeviceClockReportsIntermediatePositionsAndFinishesAtTheClipEnd(int sampleRate, int channels, int bits)
    {
        RequireOutputDevice();
        AudioClip clip = SilentClip(sampleRate, channels, bits);
        List<double> positions = [];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await new AudioPreview().PlayAsync(clip, timeout.Token, positions.Add);
        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Some.InRange(0.1, 0.8), "The device clock must advance during playback, not just on completion.");
            Assert.That(positions, Is.Ordered.Ascending);
            Assert.That(positions, Has.All.InRange(0, 1));
            Assert.That(positions[^1], Is.EqualTo(1));
        });
        await TestContext.Out.WriteLineAsync($"{sampleRate} Hz: {positions.Count} clock updates, first {positions[0]:F3} s, last {positions[^1]:F3} s");
    }

    [TestCase(48000, 2, 16)]
    [TestCase(96000, 1, 32)]
    [Explicit("Uses a real Windows output device with a silent clip.")]
    [Category("Manual")]
    public async Task StoppingReportsTheDevicePositionBeforeTheClockIsReset(int sampleRate, int channels, int bits)
    {
        RequireOutputDevice();
        AudioClip clip = SilentClip(sampleRate, channels, bits);
        List<double> positions = [];
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
        Task playing = new AudioPreview().PlayAsync(clip, cancellation.Token, position =>
        {
            positions.Add(position);
            if (position >= 0.2)
                started.TrySetResult();
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await cancellation.CancelAsync();
            try
            {
                await playing.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Fail("Stopping must cancel the preview.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            Assert.Multiple(() =>
            {
                Assert.That(positions, Is.Ordered.Ascending);
                Assert.That(positions[^1], Is.InRange(0.2, 0.8), "Stopping must preserve the position instead of resetting to zero or completing the clip.");
            });
            await TestContext.Out.WriteLineAsync($"{sampleRate} Hz: stopped at {positions[^1]:F3} s");
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await playing; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }

    private static AudioClip SilentClip(int sampleRate, int channels, int bits) =>
        new(new AudioFormat(sampleRate, channels, bits, "pcm"), new byte[sampleRate * channels * (bits / 8)]);

    private static void RequireOutputDevice()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Windows playback only.");
        using MMDeviceEnumerator devices = new();
        if (devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count == 0)
            Assert.Ignore("No active output device.");
    }
}