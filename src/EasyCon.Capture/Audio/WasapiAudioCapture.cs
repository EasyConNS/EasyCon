using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace EasyCon.Capture.Audio;

internal sealed record AudioCaptureSession(AudioDeviceInfo Device, IWaveIn Capture, IDisposable? NativeDevice = null);

public sealed class WasapiAudioCapture : IAudioCaptureSource
{
    [ThreadStatic]
    private static WasapiAudioCapture? _callbackSource;

    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Func<string, AudioCaptureSession> _openCapture;
    private readonly Queue<AudioCaptureState> _pendingStates = new();
    private IWaveIn? _capture;
    private IDisposable? _nativeDevice;
    private AudioDeviceInfo? _device;
    private AudioFormat? _format;
    private CancellationToken _captureCancellation;
    private long _connectionVersion;
    private long _captureVersion;
    private bool _connectedAnnounced;
    private bool _publishingStates;
    private bool _disposed;
    private AudioCaptureState _state = new(AudioCaptureStatus.Disconnected);

    public WasapiAudioCapture() : this(OpenCapture, OperatingSystem.IsWindows())
    {
    }

    internal WasapiAudioCapture(Func<string, AudioCaptureSession> openCapture, bool isSupported = true)
    {
        _openCapture = openCapture;
        IsSupported = isSupported;
    }

    public bool IsSupported { get; }

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

    public bool TryConnect(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("请选择音频输入设备。", nameof(deviceId));

        if (ReferenceEquals(_callbackSource, this))
            throw new InvalidOperationException("请在音频数据回调结束后异步启动连接。");

        try
        {
            _operationGate.Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            return TryConnectCore(deviceId, cancellationToken);
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
        IWaveIn? capture;
        lock (_lifecycleLock)
        {
            if (_disposed || _state.Status != AudioCaptureStatus.Connecting)
                return Task.FromResult(false);

            // Invalidate opening and callbacks immediately; native cleanup may have to wait for open.
            _connectionVersion++;
            capture = _capture;
            SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Disconnected));
        }

        return Task.Run(() =>
        {
            _operationGate.Wait();
            try
            {
                if (capture != null)
                    StopCurrentCapture(capture);
                return true;
            }
            finally
            {
                _operationGate.Release();
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
            _connectionVersion++;
            _state = new AudioCaptureState(AudioCaptureStatus.Disconnected);
        }

        // A concurrent stop may be joining this callback. Never wait for its gate here.
        if (ReferenceEquals(_callbackSource, this) || !_operationGate.Wait(0))
        {
            _ = Task.Run(() => DisposeResources());
            return;
        }

        DisposeResources(gateAcquired: true);
    }

    private void DisposeResources(bool gateAcquired = false)
    {
        if (!gateAcquired)
            _operationGate.Wait();
        try
        {
            StopCurrentCapture();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private bool TryConnectCore(string deviceId, CancellationToken cancellationToken)
    {
        StopCurrentCapture();
        long version;
        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
                return false;

            version = ++_connectionVersion;
            SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Connecting));
        }

        AudioCaptureSession? session = null;
        bool installed = false;
        try
        {
            if (!IsSupported)
                throw new NotSupportedException("当前平台不支持 WASAPI 音频采集。");

            session = _openCapture(deviceId);
            AudioFormat format = ToAudioFormat(session.Capture.WaveFormat);
            lock (_lifecycleLock)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                if (version != _connectionVersion)
                    throw new OperationCanceledException();

                _device = session.Device;
                _format = format;
                _connectedAnnounced = false;
                _captureCancellation = cancellationToken;
                _captureVersion = version;
                _nativeDevice = session.NativeDevice;
                _capture = session.Capture;
                session.Capture.DataAvailable += OnDataAvailable;
                session.Capture.RecordingStopped += OnRecordingStopped;
                installed = true;
                SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Connecting, session.Device, format));
            }

            session.Capture.StartRecording();
            return true;
        }
        catch (Exception ex)
        {
            if (installed)
                StopCurrentCapture(session!.Capture);
            else if (session != null)
                DisposeCapture(session.Capture, session.NativeDevice);

            lock (_lifecycleLock)
            {
                if (!_disposed && version == _connectionVersion)
                    SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Failed, Error: $"打开音频输入失败：{ex.Message}"));
            }
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
                _connectionVersion++;
            }

            StopCurrentCapture();
            lock (_lifecycleLock)
            {
                if (!_disposed)
                    SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Disconnected));
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (sender is not IWaveIn capture)
            return;

        WasapiAudioCapture? previous = _callbackSource;
        _callbackSource = this;
        try
        {
            PublishAudioData(capture, args);
        }
        finally
        {
            _callbackSource = previous;
        }
    }

    private void PublishAudioData(IWaveIn capture, WaveInEventArgs args)
    {
        AudioFormat format;
        int bytesRecorded = Math.Min(args.BytesRecorded, args.Buffer.Length);
        lock (_lifecycleLock)
        {
            if (!IsCurrentCaptureLocked(capture) || _format == null || bytesRecorded <= 0)
                return;

            format = _format;
            if (!_connectedAnnounced)
            {
                _connectedAnnounced = true;
                SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Connected, _device, format));
            }
        }

        byte[] pcm = new byte[bytesRecorded];
        Buffer.BlockCopy(args.Buffer, 0, pcm, 0, bytesRecorded);
        AudioLevel level = AudioLevelCalculator.Calculate(pcm, format);
        AudioChunk chunk = new(ToNanoseconds(Stopwatch.GetTimestamp()), format, pcm, pcm.Length);
        if (!IsCurrentCapture(capture))
            return;

        PublishDataAvailable(chunk);
        if (IsCurrentCapture(capture))
            PublishLevelChanged(level);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (sender is not IWaveIn capture)
            return;

        lock (_lifecycleLock)
        {
            if (!IsCurrentCaptureLocked(capture))
                return;

            string message = args.Exception?.Message ?? "音频输入已停止。";
            SetStateLocked(new AudioCaptureState(AudioCaptureStatus.Failed, _device, _format, message));
        }

        // This may run on the capture thread; disposal must not join that same thread.
        _ = Task.Run(() => StopCurrentCapture(capture));
    }

    private void StopCurrentCapture(IWaveIn? expectedCapture = null)
    {
        IWaveIn? capture;
        IDisposable? nativeDevice;
        lock (_lifecycleLock)
        {
            if (expectedCapture != null && !ReferenceEquals(expectedCapture, _capture))
                return;

            capture = _capture;
            nativeDevice = _nativeDevice;
            _capture = null;
            _nativeDevice = null;
            _device = null;
            _format = null;
            _connectedAnnounced = false;

            if (capture != null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
            }
        }

        DisposeCapture(capture, nativeDevice);
    }

    private static void DisposeCapture(IWaveIn? capture, IDisposable? nativeDevice)
    {
        try
        {
            capture?.StopRecording();
        }
        catch
        {
            // Cleanup must also succeed after Windows invalidates an endpoint.
        }

        try
        {
            capture?.Dispose();
        }
        catch
        {
            // A failed native cleanup must not prevent releasing the endpoint.
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

    private bool IsCurrentCapture(IWaveIn capture)
    {
        lock (_lifecycleLock)
            return IsCurrentCaptureLocked(capture);
    }

    private bool IsCurrentCaptureLocked(IWaveIn capture)
    {
        return !_disposed && ReferenceEquals(capture, _capture)
            && _captureVersion == _connectionVersion && !_captureCancellation.IsCancellationRequested;
    }

    private void SetStateLocked(AudioCaptureState state)
    {
        _state = state;
        _pendingStates.Enqueue(state);
        if (_publishingStates)
            return;

        _publishingStates = true;
        _ = Task.Run(PublishPendingStates);
    }

    private void PublishPendingStates()
    {
        while (true)
        {
            AudioCaptureState state;
            lock (_lifecycleLock)
            {
                if (!_pendingStates.TryDequeue(out state!))
                {
                    _publishingStates = false;
                    return;
                }
                if (_disposed && state.Status != AudioCaptureStatus.Disconnected)
                    continue;
            }

            Action<AudioCaptureState>? handlers = StateChanged;
            if (handlers == null)
                continue;

            foreach (Action<AudioCaptureState> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(state);
                }
                catch
                {
                    // Notifications run in order, outside lifecycle gates and the audio callback.
                }
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

    private static AudioCaptureSession OpenCapture(string deviceId)
    {
        using MMDeviceEnumerator enumerator = new();
        MMDevice device = enumerator.GetDevice(deviceId);
        try
        {
            return new AudioCaptureSession(new AudioDeviceInfo(device.ID, device.FriendlyName), new SafeWasapiCapture(device), device);
        }
        catch
        {
            device.Dispose();
            throw;
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