using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace EasyCon.Capture.Audio;

// A capture owns one session. A reconnect creates a new instance and native client.
internal sealed class SafeWasapiCapture : IWaveIn
{
    private readonly object _lifecycleLock = new();
    private readonly IWasapiCaptureClient _client;
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private WaveFormat _waveFormat;
    private Thread? _captureThread;
    private volatile bool _stopRequested;
    private bool _recordingStarted;
    private bool _resourcesReleased;
    private bool _disposed;

    public SafeWasapiCapture(MMDevice device)
        : this(new NativeWasapiCaptureClient(device))
    {
    }

    internal SafeWasapiCapture(IWasapiCaptureClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        try
        {
            _waveFormat = client.MixFormat;
            ValidateWaveFormat(_waveFormat);
        }
        catch
        {
            ReleaseResources(false, null);
            throw;
        }
    }

    public WaveFormat WaveFormat
    {
        get
        {
            lock (_lifecycleLock)
                return _waveFormat.AsStandardWaveFormat();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ValidateWaveFormat(value);
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_recordingStarted)
                    throw new InvalidOperationException("The recording format cannot change after starting.");

                _waveFormat = value;
            }
        }
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording()
    {
        Exception? startException = null;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recordingStarted)
                throw new InvalidOperationException("This capture session has already been started.");

            _recordingStarted = true;
            _captureThread = new Thread(CaptureThread)
            {
                IsBackground = true,
                Name = "EasyCon WASAPI capture",
            };
            try
            {
                _captureThread.Start();
            }
            catch (Exception ex)
            {
                _captureThread = null;
                startException = ex;
            }
        }

        if (startException != null)
        {
            Exception? exception = ReleaseResources(false, startException);
            PublishRecordingStopped(exception);
            ExceptionDispatchInfo.Capture(startException).Throw();
        }
    }

    public void StopRecording()
    {
        lock (_lifecycleLock)
        {
            if (_recordingStarted)
                RequestStopLocked();
        }
    }

    public void Dispose()
    {
        Thread? captureThread;
        lock (_lifecycleLock)
        {
            _disposed = true;
            RequestStopLocked();
            captureThread = _captureThread;
        }

        if (captureThread != null)
        {
            // A DataAvailable/RecordingStopped observer may dispose on this thread.
            // Its return lets the thread finish cleanup, without joining itself.
            if (captureThread != Thread.CurrentThread)
                captureThread.Join();
        }
        else
        {
            ReleaseResources(false, null);
        }
    }

    private void CaptureThread()
    {
        Exception? exception = null;
        bool initialized = false;
        try
        {
            if (!_stopRequested)
            {
                _client.Initialize(_waveFormat);
                initialized = true;
                int bufferFrameCount = _client.BufferFrameCount;
                if (bufferFrameCount <= 0)
                    throw new InvalidOperationException("The audio endpoint returned an empty capture buffer.");

                int pollMilliseconds = (int)Math.Clamp((long)bufferFrameCount * 1000 / _waveFormat.SampleRate / 2, 1, 50);
                if (!_stopRequested)
                {
                    _client.Start();
                    while (!_stopRequested && !_stopSignal.Wait(pollMilliseconds))
                        ReadAvailablePackets(_waveFormat.BlockAlign);
                }
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        finally
        {
            // Stop can fail after Windows audio service shutdown, even after a read
            // failure was caught. Each cleanup stage must still be attempted.
            exception = ReleaseResources(initialized, exception);
            PublishRecordingStopped(exception);
            lock (_lifecycleLock)
                _captureThread = null;
        }
    }

    private void ReadAvailablePackets(int bytesPerFrame)
    {
        while (!_stopRequested && _client.GetNextPacketSize() > 0)
        {
            IntPtr pointer = _client.GetBuffer(out int framesAvailable, out AudioClientBufferFlags flags);
            byte[]? packet = null;
            Exception? exception = null;
            try
            {
                if (framesAvailable < 0)
                    throw new InvalidOperationException("The audio endpoint returned a negative frame count.");

                if (framesAvailable > 0)
                {
                    // Preserve every whole frame, including packets larger than the
                    // nominal endpoint buffer. A Silent packet may have a null pointer.
                    packet = new byte[checked(framesAvailable * bytesPerFrame)];
                    if ((flags & AudioClientBufferFlags.Silent) == 0)
                    {
                        if (pointer == IntPtr.Zero)
                            throw new InvalidOperationException("The audio endpoint returned a null data buffer.");

                        Marshal.Copy(pointer, packet, 0, packet.Length);
                    }
                    else if (_waveFormat.BitsPerSample == 8)
                    {
                        // PCM8 is unsigned: silence is the midpoint, not byte zero.
                        Array.Fill(packet, (byte)0x80);
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            try
            {
                _client.ReleaseBuffer(framesAvailable);
            }
            catch (Exception ex)
            {
                exception = CombineExceptions(exception, ex);
            }

            if (exception != null)
                ExceptionDispatchInfo.Capture(exception).Throw();

            if (packet != null)
                PublishDataAvailable(packet);
            else
                break;
        }
    }

    private Exception? ReleaseResources(bool initialized, Exception? exception)
    {
        lock (_lifecycleLock)
        {
            if (_resourcesReleased)
                return exception;

            _resourcesReleased = true;
            _stopSignal.Dispose();
            if (initialized)
            {
                try
                {
                    _client.Stop();
                }
                catch (Exception ex)
                {
                    exception = CombineExceptions(exception, ex);
                }
            }

            try
            {
                _client.Dispose();
            }
            catch (Exception ex)
            {
                exception = CombineExceptions(exception, ex);
            }

            return exception;
        }
    }

    private void RequestStopLocked()
    {
        _stopRequested = true;
        if (!_resourcesReleased)
            _stopSignal.Set();
    }

    private void PublishDataAvailable(byte[] packet)
    {
        EventHandler<WaveInEventArgs>? handlers = DataAvailable;
        if (handlers == null)
            return;

        WaveInEventArgs args = new(packet, packet.Length);
        foreach (EventHandler<WaveInEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // An observer cannot terminate the native capture thread.
            }
        }
    }

    private void PublishRecordingStopped(Exception? exception)
    {
        EventHandler<StoppedEventArgs>? handlers = RecordingStopped;
        if (handlers == null)
            return;

        StoppedEventArgs args = new(exception);
        foreach (EventHandler<StoppedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Cleanup and other observers must survive a faulty stop handler.
            }
        }
    }

    private static Exception CombineExceptions(Exception? first, Exception next)
    {
        return first == null ? next : new AggregateException(first, next);
    }

    private static void ValidateWaveFormat(WaveFormat format)
    {
        WaveFormat standard = format.AsStandardWaveFormat();
        if (standard.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
            || standard.Channels <= 0 || standard.SampleRate <= 0
            || standard.BitsPerSample is not (8 or 16 or 24 or 32)
            || (standard.Encoding == WaveFormatEncoding.IeeeFloat && standard.BitsPerSample != 32)
            || format.BlockAlign != standard.Channels * (standard.BitsPerSample / 8))
        {
            throw new NotSupportedException("The audio endpoint returned an unsupported capture format.");
        }
    }
}

internal interface IWasapiCaptureClient : IDisposable
{
    WaveFormat MixFormat { get; }
    int BufferFrameCount { get; }
    void Initialize(WaveFormat format);
    void Start();
    void Stop();
    int GetNextPacketSize();
    IntPtr GetBuffer(out int framesAvailable, out AudioClientBufferFlags flags);
    void ReleaseBuffer(int framesAvailable);
}

internal sealed class NativeWasapiCaptureClient : IWasapiCaptureClient
{
    private readonly AudioClient _audioClient;
    private AudioCaptureClient? _captureClient;

    public NativeWasapiCaptureClient(MMDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _audioClient = device.AudioClient;
    }

    public WaveFormat MixFormat => _audioClient.MixFormat;
    public int BufferFrameCount => _audioClient.BufferSize;

    public void Initialize(WaveFormat format)
    {
        _audioClient.Initialize(AudioClientShareMode.Shared,
            AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
            1_000_000, 0, format, Guid.Empty);
        _captureClient = _audioClient.AudioCaptureClient;
    }

    public void Start() => _audioClient.Start();
    public void Stop() => _audioClient.Stop();
    public int GetNextPacketSize() => _captureClient!.GetNextPacketSize();

    public IntPtr GetBuffer(out int framesAvailable, out AudioClientBufferFlags flags)
    {
        return _captureClient!.GetBuffer(out framesAvailable, out flags);
    }

    public void ReleaseBuffer(int framesAvailable) => _captureClient!.ReleaseBuffer(framesAvailable);

    // AudioClient owns and releases its AudioCaptureClient service as well.
    public void Dispose() => _audioClient.Dispose();
}