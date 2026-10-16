using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon.Capture;

/// <summary>单生产者。停止超时仍保留任务身份，拒绝重启，迟到的 Read 不再发布。</summary>
public sealed class FrameProducer : IDisposable
{
    private readonly IFrameSource _capture;
    private readonly FrameStore _store;
    private readonly FrameNormalization _normalization;
    private readonly string _sourceId;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;
    private string? _lastError;
    private bool _sourceOpened;

    public FrameProducer(IFrameSource capture, FrameStore? store = null,
        FrameNormalization? normalization = null, string sourceId = "camera")
    {
        _capture = capture;
        _store = store ?? new FrameStore();
        _normalization = normalization ?? new FrameNormalization();
        _sourceId = sourceId;
        _sourceOpened = capture.IsOpened;
    }

    public FrameStore Store => _store;
    public long FrameCount => _store.FrameCount;
    public long SourceEpoch { get; private set; }
    public bool IsOpened => Volatile.Read(ref _sourceOpened);
    public bool IsStopping => _cts?.IsCancellationRequested == true && _loopTask?.IsCompleted == false;
    public string? LastError => Volatile.Read(ref _lastError);
    public void SetProperties(int width, int height) => _capture.SetProperties(width, height);

    public void Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loopTask?.IsCompleted == false)
            {
                if (_cts!.IsCancellationRequested) throw new InvalidOperationException("上一采集循环仍在停止中");
                return;
            }
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            long epoch = SourceEpoch = _store.BeginSource();
            _loopTask = Task.Run(() => Loop(token, epoch));
        }
    }

    private void Loop(CancellationToken token, long epoch)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                bool opened = _capture.IsOpened;
                Volatile.Write(ref _sourceOpened, opened);
                if (!opened)
                {
                    token.WaitHandle.WaitOne(100);
                    continue;
                }
                using Mat raw = _capture.GetMatFrame();
                long arrival = Stopwatch.GetTimestamp();
                if (token.IsCancellationRequested) break;
                if (raw.Empty())
                {
                    token.WaitHandle.WaitOne(5);
                    continue;
                }
                Mat? normalized = _normalization.Apply(raw);
                try
                {
                    FrameMetadata metadata = new(_sourceId, epoch, 0, arrival, Stopwatch.Frequency,
                        raw.Size(), normalized.Size(), _normalization.Id);
                    lock (_lifecycleLock)
                    {
                        if (token.IsCancellationRequested) break;
                        _store.Publish(normalized, metadata);
                        normalized = null;
                        Volatile.Write(ref _lastError, null);
                    }
                }
                finally { normalized?.Dispose(); }
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex.Message);
                Debug.WriteLine($"FrameProducer: {ex.Message}");
                token.WaitHandle.WaitOne(20);
            }
        }
    }

    public bool Stop(TimeSpan? timeout = null)
    {
        Task? loop;
        lock (_lifecycleLock)
        {
            if (_loopTask?.IsCompleted != false)
            {
                _store.ReleaseCurrent();
                return true;
            }
            _cts?.Cancel();
            loop = _loopTask;
            _store.ReleaseCurrent();
        }
        if (loop == null) return true;
        try { return loop.Wait(timeout ?? TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { return loop.IsCompleted; }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (Stop())
        {
            Volatile.Write(ref _sourceOpened, false);
            _capture.Dispose();
            _cts?.Dispose();
        }
        else
        {
            // 阻塞 Read 的所有者保留到实际退出，不能释放它正在使用的 native 对象。
            _ = _loopTask!.ContinueWith(_ =>
            {
                Volatile.Write(ref _sourceOpened, false);
                _capture.Dispose();
                _cts?.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}