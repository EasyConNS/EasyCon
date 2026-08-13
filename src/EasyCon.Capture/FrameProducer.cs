using EzCv;
using System.Diagnostics;

namespace EasyCon.Capture;

/// <summary>
/// 单生产者采集循环：后台 Task 持续 Read 最新帧并发布到 FrameStore。
/// 由 OpenCVCapture 的阻塞 Read 自然控制节奏，无需 Thread.Sleep。
/// </summary>
public sealed class FrameProducer : IDisposable
{
    private readonly OpenCVCapture _capture;
    private readonly FrameStore _store = new();
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public FrameProducer(OpenCVCapture capture)
    {
        _capture = capture;
    }

    public FrameStore Store => _store;

    public bool IsOpened => _capture.IsOpened;

    /// <summary>转发采集参数设置（分辨率、FourCC、FPS）。</summary>
    public void SetProperties(int width, int height)
    {
        _capture.SetProperties(width, height);
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loopTask = Task.Run(() => Loop(ct));
        }
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 设备未打开（未连接/已断开）时阻塞 Read 不再生效，需主动降频避免空转。
                if (!_capture.IsOpened)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var mat = _capture.GetMatFrame();
                if (mat.Empty())
                {
                    mat.Dispose();
                    Thread.Sleep(5);   // 尚无新帧，短暂让出 CPU
                    continue;
                }
                _store.Publish(mat);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FrameProducer 采集异常: {ex.Message}");
                Thread.Sleep(5);
            }
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            _cts?.Cancel();
            _cts = null;
        }

        var loop = _loopTask;
        if (loop != null)
        {
            try
            {
                loop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // 采集异常已在 Loop 内部捕获，这里兜底忽略
            }
        }
        _loopTask = null;
        _store.ReleaseCurrent();
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
    }
}