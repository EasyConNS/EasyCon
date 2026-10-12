using EasyCon.Capture.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace EasyCon2.Avalonia.AudioLabels;

public interface IAudioPreview
{
    bool IsSupported { get; }
    /// <summary>Reports seconds played within the clip. Callbacks are serialized; the caller dispatches UI updates.</summary>
    Task PlayAsync(AudioClip clip, CancellationToken cancellationToken, Action<double>? positionChanged = null);
}

/// <summary>Explicit, finite playback to the default output. Never monitors live input.</summary>
public sealed class AudioPreview : IAudioPreview
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task PlayAsync(AudioClip clip, CancellationToken cancellationToken, Action<double>? positionChanged = null)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException();
        return Task.Run(async () =>
        {
            using MemoryStream memory = new(clip.Pcm.ToArray(), writable: false);
            using RawSourceWaveStream stream = new(memory, AudioLabelStore.ToWaveFormat(clip.Format));
            using WasapiOut output = new(AudioClientShareMode.Shared, false, 100);
            TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is null)
                    finished.TrySetResult();
                else
                    finished.TrySetException(args.Exception);
            };
            output.Init(stream);
            cancellationToken.ThrowIfCancellationRequested();
            object positionLock = new();
            double lastPosition = 0;
            Stopwatch elapsed = Stopwatch.StartNew();
            void ReportPosition()
            {
                lock (positionLock)
                {
                    // Use the device clock, not the reader, which runs ahead to fill the output buffer.
                    double position = output.GetPosition() / (double)output.OutputWaveFormat.AverageBytesPerSecond;
                    // NAudio extrapolates from QPC; an uninitialized clock can report an impossible position.
                    // Stop/reset can also return zero. Neither should move the cursor.
                    if (position < lastPosition || position > elapsed.Elapsed.TotalSeconds)
                        return;
                    lastPosition = Math.Min(position, clip.DurationSeconds);
                    positionChanged?.Invoke(lastPosition);
                }
            }
            output.Play();
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try { ReportPosition(); }
                finally { output.Stop(); }
            });
            while (!finished.Task.IsCompleted)
            {
                await Task.WhenAny(finished.Task, Task.Delay(33)).ConfigureAwait(false);
                if (!finished.Task.IsCompleted)
                    ReportPosition();
            }
            await finished.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (positionLock)
            {
                lastPosition = clip.DurationSeconds;
                positionChanged?.Invoke(lastPosition);
            }
        }, cancellationToken);
    }
}