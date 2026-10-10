using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Services;
using OpenCvSharp;

namespace EasyCon2.Avalonia.Services;

public class CaptureService : ICaptureService, IDisposable
{
    private readonly ILogService _logService;
    private readonly object _captureLock = new();

    /// <summary>串行化连接生命周期（Open/Dispose 可阻塞数秒，不能占用 _captureLock）。</summary>
    private readonly object _connectLock = new();
    private readonly System.Timers.Timer _monitorTimer = new(1000);
    private readonly object _sourceIndexLock = new();
    private readonly Dictionary<string, int> _sourceIndexMap = new();
    private FrameProducer? _producer;

    private readonly Size resol = new(1920, 1080);
    public string CaptureType { get; set; } = "ANY";

    public bool IsConnected
    {
        get
        {
            lock (_captureLock)
            {
                return _producer?.IsOpened ?? false;
            }
        }
    }

    public event Action? ConnectionLost;
#pragma warning disable CS0067
    public event Action? ConnectionRestored;
#pragma warning restore CS0067

    /// <summary>连接状态可能已变化（连接/断开成功后触发，含 Flow 桥接的外部连接）。非 UI 线程。</summary>
    public event Action? ConnectionStateChanged;

    public CaptureService(ILogService logService)
    {
        _logService = logService;

        _monitorTimer.Elapsed += (s, e) =>
        {
            lock (_captureLock)
            {
                var producer = Volatile.Read(ref _producer);
                if (producer == null || !producer.IsOpened)
                {
                    _monitorTimer.Stop();
                    Dispatcher.UIThread.Post(() =>
                    {
                        ConnectionLost?.Invoke();
                        _logService.AddLog("视频源已从外部断开");
                    });
                }
            }
        };
    }

    public string[] GetAvailableSources()
    {
        var sources = ECCore.GetCaptureSources().ToList();
        lock (_sourceIndexLock)
        {
            _sourceIndexMap.Clear();
            foreach (var (name, index) in sources)
                _sourceIndexMap[name] = index;
        }
        return sources.Select(x => x.name).ToArray();
    }

    public bool TryConnect(string sourceName)
    {
        lock (_sourceIndexLock)
        {
            return TryConnect(_sourceIndexMap.TryGetValue(sourceName, out var idx) ? idx : 0);
        }
    }

    /// <summary>按设备索引连接（Flow 服务桥接用：画布/agent 报告的是枚举索引）。</summary>
    public bool TryConnect(int deviceId)
    {
        // capture.Open 可阻塞数秒（native 打开采集卡）、旧 producer.Dispose 最长 5 秒，
        // 都放在 _captureLock 之外执行：锁内只做引用替换，避免拖住并发的
        // IsConnected / SetCaptureProperties / AcquireLatestFrame 调用方。
        // 并发的多次 TryConnect/Disconnect 由 _connectLock 串行化。
        var ok = false;
        lock (_connectLock)
        {
            // 转换期内不许监视定时器评价新旧 producer 之间的过渡态，
            // 否则它会读到“已断开”误发 ConnectionLost（重连误报的根源）
            _monitorTimer.Stop();

            // 先摘引用再 Dispose：AcquireLatestFrame 等热路径立刻看到未连接，
            // 不会摸到已释放的 Store
            var oldProducer = Volatile.Read(ref _producer);
            Volatile.Write(ref _producer, null);
            oldProducer?.Dispose();

            var capture = new OpenCVCapture();
            if (capture.Open(deviceId, (int)GetCaptureApi()))
            {
                capture.SetResolution(resol.Width, resol.Height);
                capture.SetProperties();
                capture.GetProperties();

                var producer = new FrameProducer(capture);
                producer.Start();
                Volatile.Write(ref _producer, producer);
                ok = true;
            }
            else
            {
                capture.Dispose();
                // 失败留在干净的无连接状态；监视定时器保持停止，由调用方决定重试
            }
        }

        if (ok)
            _monitorTimer.Start();
        // 失败也广播：旧 producer 已被摘除，连接状态确实发生了变化
        ConnectionStateChanged?.Invoke();
        return ok;
    }

    /// <summary>
    /// 异步断开：FrameProducer.Dispose 会同步等待采集循环退出（采集卡阻塞在
    /// Read 上时最长 5 秒），放线程池执行以免冻结调用方（UI 线程）。
    /// </summary>
    public Task DisconnectAsync()
    {
        _monitorTimer.Stop();
        return Task.Run(() =>
        {
            lock (_connectLock)
            {
                var producer = Volatile.Read(ref _producer);
                Volatile.Write(ref _producer, null);
                producer?.Dispose();
            }
            ConnectionStateChanged?.Invoke();
        });
    }

    public void Dispose()
    {
        _monitorTimer.Dispose();
        // 退出时仍在连接：释放采集线程与 OpenCV 原生句柄（Dispose 最长等采集循环 5 秒）
        Volatile.Read(ref _producer)?.Dispose();
    }

    /// <summary>
    /// 获取最新一帧的租约。热路径无锁，仅对 _producer 引用做易失读取。
    /// 未连接或尚无帧时返回 null；调用者须持有租约直至不再使用 Mat。
    /// </summary>
    public FrameLease? AcquireLatestFrame()
    {
        var producer = Volatile.Read(ref _producer);
        return producer?.Store.AcquireLatest();
    }

    /// <summary>当前帧存储（未连接为 null）。Flow 服务桥接用：复用 GUI 的采集管线而非二次打开采集卡。</summary>
    public FrameStore? GetFrameStore()
    {
        var producer = Volatile.Read(ref _producer);
        return producer?.Store;
    }

    public void SetCaptureProperties(int width, int height)
    {
        lock (_captureLock)
        {
            _producer?.SetProperties(width, height);
        }
    }

    private VideoCaptureAPIs GetCaptureApi()
    {
        return CaptureType switch
        {
            "DSHOW" => VideoCaptureAPIs.DSHOW,
            "MSMF" => VideoCaptureAPIs.MSMF,
            "DC1394" => VideoCaptureAPIs.DC1394,
            _ => VideoCaptureAPIs.ANY
        };
    }
}