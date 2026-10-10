using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace EasyCon.Capture.Audio;

public sealed class WasapiAudioCapture : IAudioCaptureSource
{
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private WasapiCapture? _capture;
    private MMDevice? _nativeDevice;
    private AudioDeviceInfo? _device;
    private AudioFormat? _format;
    private bool _connectedAnnounced;
    private int _callbackThreadId;
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
            using MMDeviceEnumerator enumerator = new();
            MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            List<AudioDeviceInfo> result = new(devices.Count);
            foreach (MMDevice device in devices)
            {
                using (device)
                    result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }

            return result;
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

        _operationGate.Wait();
        try
        {
            return TryConnectCore(deviceId);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task DisconnectAsync()
    {
        return Task.Run(DisconnectCore);
    }

    public Task<bool> DisconnectIfConnectingAsync()
    {
        return Task.Run(DisconnectIfConnectingCore);
    }

    public void Dispose()
    {
        _operationGate.Wait();
        try
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
            }

            StopCurrentCapture();
            lock (_lifecycleLock)
                _state = new AudioCaptureState(AudioCaptureStatus.Disconnected);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private bool TryConnectCore(string deviceId)
    {
        StopCurrentCapture();
        lock (_lifecycleLock)
            ThrowIfDisposed();

        if (!IsSupported)
        {
            SetState(new AudioCaptureState(
                AudioCaptureStatus.Failed,
                Error: "当前平台不支持 WASAPI 音频采集。"));
            return false;
        }

        MMDevice? device = null;
        WasapiCapture? capture = null;
        bool installed = false;
        try
        {
            using MMDeviceEnumerator enumerator = new();
            device = enumerator.GetDevice(deviceId);
            AudioDeviceInfo info = new(device.ID, device.FriendlyName);
            capture = new WasapiCapture(device);
            AudioFormat format = ToAudioFormat(capture.WaveFormat);

            lock (_lifecycleLock)
            {
                ThrowIfDisposed();
                _device = info;
                _format = format;
                _connectedAnnounced = false;
                _nativeDevice = device;
                _capture = capture;
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                installed = true;
            }

            SetState(new AudioCaptureState(AudioCaptureStatus.Connecting, info, format));
            capture.StartRecording();
            return true;
        }
        catch (Exception ex)
        {
            if (installed)
                StopCurrentCapture();
            else
                DisposeCapture(capture, device);

            SetState(new AudioCaptureState(
                AudioCaptureStatus.Failed,
                Error: $"打开音频输入失败：{ex.Message}"));
            return false;
        }
    }

    private void DisconnectCore()
    {
        _operationGate.Wait();
        try
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;
            }

            StopCurrentCapture();
            SetState(new AudioCaptureState(AudioCaptureStatus.Disconnected));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private bool DisconnectIfConnectingCore()
    {
        _operationGate.Wait();
        try
        {
            WasapiCapture? capture;
            MMDevice? nativeDevice;
            int callbackThreadId;
            AudioCaptureState disconnectedState;
            lock (_lifecycleLock)
            {
                if (_disposed || _state.Status != AudioCaptureStatus.Connecting)
                    return false;

                DetachCurrentCaptureLocked(out capture, out nativeDevice, out callbackThreadId);
                disconnectedState = new AudioCaptureState(AudioCaptureStatus.Disconnected);
                _state = disconnectedState;
            }

            DisposeCapture(capture, nativeDevice, callbackThreadId);
            PublishStateChanged(disconnectedState);
            return true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (sender is not WasapiCapture capture)
            return;

        AudioFormat? format;
        AudioDeviceInfo? device;
        AudioCaptureState? connectedState = null;
        int bytesRecorded = args.BytesRecorded;
        lock (_lifecycleLock)
        {
            if (_disposed || !ReferenceEquals(capture, _capture) || _format == null || bytesRecorded <= 0)
                return;

            _callbackThreadId = Environment.CurrentManagedThreadId;
            bytesRecorded = Math.Min(bytesRecorded, args.Buffer.Length);
            if (bytesRecorded <= 0)
                return;

            format = _format;
            device = _device;
            if (!_connectedAnnounced)
            {
                _connectedAnnounced = true;
                connectedState = new AudioCaptureState(AudioCaptureStatus.Connected, device, format);
                _state = connectedState;
            }
        }

        if (connectedState != null)
            PublishStateChanged(connectedState);

        byte[] pcm = new byte[bytesRecorded];
        Buffer.BlockCopy(args.Buffer, 0, pcm, 0, bytesRecorded);

        AudioLevel level;
        try
        {
            level = AudioLevelCalculator.Calculate(pcm, format);
        }
        catch
        {
            level = new AudioLevel(0, 0, true);
        }

        AudioChunk chunk = new(ToNanoseconds(Stopwatch.GetTimestamp()), format, pcm, pcm.Length);
        if (!IsCurrentCapture(capture))
            return;

        PublishDataAvailable(chunk);
        if (IsCurrentCapture(capture))
            PublishLevelChanged(level);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (sender is not WasapiCapture capture)
            return;

        AudioCaptureState? failedState = null;
        lock (_lifecycleLock)
        {
            if (_disposed || !ReferenceEquals(capture, _capture))
                return;

            string message = args.Exception?.Message ?? "音频输入已停止。";
            failedState = new AudioCaptureState(AudioCaptureStatus.Failed, _device, _format, message);
            _state = failedState;
        }

        StopCurrentCapture(capture, Environment.CurrentManagedThreadId);

        lock (_lifecycleLock)
        {
            if (_disposed || !ReferenceEquals(_state, failedState))
                return;
        }

        PublishStateChanged(failedState);
    }

    private void StopCurrentCapture(WasapiCapture? expectedCapture = null, int callbackThreadIdOverride = 0)
    {
        WasapiCapture? capture;
        MMDevice? nativeDevice;
        int callbackThreadId;
        lock (_lifecycleLock)
        {
            if (expectedCapture != null && !ReferenceEquals(expectedCapture, _capture))
                return;

            DetachCurrentCaptureLocked(out capture, out nativeDevice, out callbackThreadId);
        }

        if (callbackThreadIdOverride != 0)
            callbackThreadId = callbackThreadIdOverride;
        DisposeCapture(capture, nativeDevice, callbackThreadId);
    }

    private void DetachCurrentCaptureLocked(
        out WasapiCapture? capture,
        out MMDevice? nativeDevice,
        out int callbackThreadId)
    {
        capture = _capture;
        nativeDevice = _nativeDevice;
        callbackThreadId = _callbackThreadId;
        _capture = null;
        _nativeDevice = null;
        _device = null;
        _format = null;
        _connectedAnnounced = false;
        _callbackThreadId = 0;

        if (capture != null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
        }
    }

    private static void DisposeCapture(WasapiCapture? capture, MMDevice? nativeDevice, int callbackThreadId = 0)
    {
        if (capture != null)
        {
            if (callbackThreadId != 0 && callbackThreadId == Environment.CurrentManagedThreadId)
            {
                _ = Task.Run(() => StopAndDisposeCapture(capture, nativeDevice));
                return;
            }

            StopAndDisposeCapture(capture, nativeDevice);
            return;
        }

        try
        {
            nativeDevice?.Dispose();
        }
        catch
        {
            // The native endpoint may already have been released by Windows.
        }
    }

    private static void StopAndDisposeCapture(WasapiCapture capture, MMDevice? nativeDevice)
    {
        try
        {
            capture.StopRecording();
        }
        catch
        {
            // The capture may already have stopped because the endpoint vanished.
        }

        DisposeCaptureResources(capture, nativeDevice);
    }

    private static void DisposeCaptureResources(WasapiCapture capture, MMDevice? nativeDevice)
    {
        try
        {
            capture.Dispose();
        }
        catch
        {
            // Cleanup must not prevent the caller from reporting the connection state.
        }

        try
        {
            nativeDevice?.Dispose();
        }
        catch
        {
            // The native endpoint may already have been released by Windows.
        }
    }

    private bool IsCurrentCapture(WasapiCapture capture)
    {
        lock (_lifecycleLock)
            return !_disposed && ReferenceEquals(capture, _capture);
    }

    private void SetState(AudioCaptureState state)
    {
        lock (_lifecycleLock)
        {
            if (_disposed && state.Status != AudioCaptureStatus.Disconnected)
                return;

            _state = state;
        }

        PublishStateChanged(state);
    }

    private void PublishStateChanged(AudioCaptureState state)
    {
        Action<AudioCaptureState>? handlers = StateChanged;
        if (handlers == null)
            return;

        foreach (Action<AudioCaptureState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(state);
            }
            catch
            {
                // A state observer must not break the capture lifecycle.
            }
        }
    }

    private void PublishDataAvailable(AudioChunk chunk)
    {
        Action<AudioChunk>? handlers = DataAvailable;
        if (handlers == null)
            return;

        foreach (Action<AudioChunk> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(chunk);
            }
            catch
            {
                // A consumer cannot be allowed to stop the audio callback thread.
            }
        }
    }

    private void PublishLevelChanged(AudioLevel level)
    {
        Action<AudioLevel>? handlers = LevelChanged;
        if (handlers == null)
            return;

        foreach (Action<AudioLevel> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(level);
            }
            catch
            {
                // A consumer cannot be allowed to stop the audio callback thread.
            }
        }
    }

    private static AudioFormat ToAudioFormat(WaveFormat format)
    {
        string encoding = format.Encoding switch
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