using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace EasyCon.Capture.Audio;

public sealed class WasapiAudioCapture : IAudioCaptureSource
{
    private readonly object _lifecycleLock = new();
    private WasapiCapture? _capture;
    private AudioDeviceInfo? _device;
    private AudioFormat? _format;
    private bool _connectedAnnounced;
    private bool _stopping;
    private bool _disposed;
    private AudioCaptureState _state = new(AudioCaptureStatus.Disconnected);

    public bool IsSupported => OperatingSystem.IsWindows();

    public AudioCaptureState State
    {
        get
        {
            lock (_lifecycleLock)
                return _state;
        }
    }

    public event Action<AudioCaptureState>? StateChanged;

    public event Action<AudioLevel>? LevelChanged;

    public event Action<AudioChunk>? DataAvailable;

    public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices()
    {
        if (!IsSupported)
            return [];

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public bool TryConnect(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("请选择音频输入设备。", nameof(deviceId));

        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            StopLocked();

            if (!IsSupported)
            {
                SetStateLocked(new AudioCaptureState(
                    AudioCaptureStatus.Failed,
                    Error: "当前平台不支持 WASAPI 音频采集。"));
                return false;
            }

            MMDevice? device = null;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDevice(deviceId);
                var info = new AudioDeviceInfo(device.ID, device.FriendlyName);
                var capture = new WasapiCapture(device);
                var format = ToAudioFormat(capture.WaveFormat);

                _device = info;
                _format = format;
                _connectedAnnounced = false;
                _capture = capture;
                SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Connecting, info, format));

                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();
                return true;
            }
            catch (Exception ex)
            {
                device?.Dispose();
                StopLocked();
                SetStateLocked(new AudioCaptureState(
                    AudioCaptureStatus.Failed,
                    Error: $"打开音频输入失败：{ex.Message}"));
                return false;
            }
        }
    }

    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                StopLocked();
                SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Disconnected));
            }
        });
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            StopLocked();
            _state = new AudioCaptureState(AudioCaptureStatus.Disconnected);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        AudioFormat? format;
        AudioDeviceInfo? device;
        lock (_lifecycleLock)
        {
            if (_disposed || _capture == null || _format == null || args.BytesRecorded <= 0)
                return;

            format = _format;
            device = _device;
            if (!_connectedAnnounced)
            {
                _connectedAnnounced = true;
                SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Connected, device, format));
            }
        }

        var pcm = new byte[args.BytesRecorded];
        Buffer.BlockCopy(args.Buffer, 0, pcm, 0, args.BytesRecorded);
        var level = AudioLevelCalculator.Calculate(pcm, format);
        var chunk = new AudioChunk(ToNanoseconds(Stopwatch.GetTimestamp()), format, pcm, pcm.Length);

        try
        {
            DataAvailable?.Invoke(chunk);
            LevelChanged?.Invoke(level);
        }
        catch
        {
            // A consumer cannot be allowed to stop the audio callback thread.
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _stopping || _state.Status == AudioCaptureStatus.Disconnected)
                return;

            var message = args.Exception?.Message ?? "音频输入已停止。";
            SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Failed, _device, _format, message));
        }
    }

    private void StopLocked()
    {
        var capture = _capture;
        _capture = null;
        _device = null;
        _format = null;
        _connectedAnnounced = false;
        if (capture == null)
        {
            _stopping = false;
            return;
        }

        _stopping = true;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try
        {
            capture.StopRecording();
        }
        catch
        {
            // The capture may already have stopped because the endpoint vanished.
        }
        try
        {
            capture.Dispose();
        }
        finally
        {
            _stopping = false;
        }
    }

    private void SetStateLocked(AudioCaptureState state)
    {
        _state = state;
        try
        {
            StateChanged?.Invoke(state);
        }
        catch
        {
            // A state observer must not break the capture lifecycle.
        }
    }

    private static AudioFormat ToAudioFormat(WaveFormat format)
    {
        var encoding = format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat => "float",
            WaveFormatEncoding.Pcm => "pcm",
            _ => throw new NotSupportedException($"不支持的音频编码：{format.Encoding}。"),
        };

        if (format.Channels <= 0 || format.SampleRate <= 0 || format.BitsPerSample is not (8 or 16 or 24 or 32))
            throw new NotSupportedException("音频设备返回了不支持的采样格式。");

        if (encoding == "float" && format.BitsPerSample != 32)
            throw new NotSupportedException("浮点音频必须使用 32 bit 采样。");

        return new AudioFormat(format.SampleRate, format.Channels, format.BitsPerSample, encoding);
    }

    private static long ToNanoseconds(long timestamp)
    {
        long seconds = timestamp / Stopwatch.Frequency;
        long remainder = timestamp % Stopwatch.Frequency;
        return checked(seconds * 1_000_000_000L + remainder * 1_000_000_000L / Stopwatch.Frequency);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WasapiAudioCapture));
    }
}