using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Services;
using EzCv;

namespace EasyCon2.Avalonia.Services;

public class CaptureService : ICaptureService
{
    private readonly ILogService _logService;
    private readonly object _captureLock = new();
    private readonly System.Timers.Timer _monitorTimer = new(1000);
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

    public CaptureService(ILogService logService)
    {
        _logService = logService;

        _monitorTimer.Elapsed += (s, e) =>
        {
            lock (_captureLock)
            {
                if (_producer == null || !_producer.IsOpened)
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
        _sourceIndexMap.Clear();
        foreach (var (name, index) in sources)
            _sourceIndexMap[name] = index;
        return sources.Select(x => x.name).ToArray();
    }

    public bool TryConnect(string sourceName)
    {
        int deviceId = _sourceIndexMap.TryGetValue(sourceName, out var idx) ? idx : 0;

        lock (_captureLock)
        {
            _producer?.Dispose();
            var capture = new OpenCVCapture();
            if (!capture.Open(deviceId, (int)GetCaptureApi()))
            {
                capture.Dispose();
                return false;
            }

            capture.SetResolution(resol.Width, resol.Height);
            capture.SetProperties();
            capture.GetProperties();

            _producer = new FrameProducer(capture);
            _producer.Start();
        }

        _monitorTimer.Start();
        return true;
    }

    public void Disconnect()
    {
        _monitorTimer.Stop();
        lock (_captureLock)
        {
            _producer?.Dispose();
            _producer = null;
        }
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