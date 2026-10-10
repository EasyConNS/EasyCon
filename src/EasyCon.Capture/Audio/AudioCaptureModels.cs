namespace EasyCon.Capture.Audio;

public enum AudioCaptureStatus
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

public sealed record AudioDeviceInfo(string Id, string Name, string Backend = "wasapi");

public sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample, string Encoding);

public sealed record AudioCaptureState(
    AudioCaptureStatus Status,
    AudioDeviceInfo? Device = null,
    AudioFormat? Format = null,
    string? Error = null);

public sealed record AudioChunk(
    long TimestampNs,
    AudioFormat Format,
    ReadOnlyMemory<byte> Pcm,
    int BytesRecorded);

public sealed record AudioLevel(double Peak, double Rms, bool Silent);

public interface IAudioCaptureSource : IDisposable
{
    bool IsSupported { get; }

    AudioCaptureState State { get; }

    IReadOnlyList<AudioDeviceInfo> GetAvailableDevices();

    bool TryConnect(string deviceId);

    Task DisconnectAsync();

    /// <summary>
    /// Stops the source only when it is still waiting for its first data block.
    /// Returns <see langword="true"/> when the connecting operation was stopped.
    /// </summary>
    Task<bool> DisconnectIfConnectingAsync();

    event Action<AudioCaptureState>? StateChanged;

    event Action<AudioLevel>? LevelChanged;

    event Action<AudioChunk>? DataAvailable;
}