using EasyCon.Capture.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EasyCon2.Avalonia.AudioLabels;

public interface IAudioPreview
{
    bool IsSupported { get; }
    Task PlayAsync(AudioClip clip, CancellationToken cancellationToken);
}

/// <summary>Explicit, finite playback to the default output. Never monitors live input.</summary>
public sealed class AudioPreview : IAudioPreview
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task PlayAsync(AudioClip clip, CancellationToken cancellationToken)
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
            output.Play();
            using CancellationTokenRegistration registration = cancellationToken.Register(output.Stop);
            await finished.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
    }
}