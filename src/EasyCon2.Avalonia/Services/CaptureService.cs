using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core;
using OpenCvSharp;

namespace EasyCon2.Avalonia.Services;

public class CaptureService : ICaptureService
{
    private readonly ILogService _logService;
    private readonly object _captureLock = new();
    private readonly System.Timers.Timer _monitorTimer = new(1000);
    private readonly Dictionary<string, int> _sourceIndexMap = new();
    private OpenCVCapture? _capture;

    private readonly Size resol = new(1920, 1080);

    public bool IsConnected
    {
        get
        {
            lock (_captureLock)
            {
                return _capture?.IsOpened ?? false;
            }
        }
    }

    public event Action? ConnectionLost;
    public event Action? ConnectionRestored;

    public CaptureService(ILogService logService)
    {
        _logService = logService;

        _monitorTimer.Elapsed += (s, e) =>
        {
            lock (_captureLock)
            {
                if (_capture == null || !_capture.IsOpened)
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
            _capture?.Dispose();
            _capture = new OpenCVCapture();
            if (!_capture.Open(deviceId, (int)VideoCaptureAPIs.ANY))
            {
                _capture = null;
                return false;
            }
        }

        _capture.SetResolution(resol.Width, resol.Height);
        _capture.SetProperties();
        _capture.GetProperties();
        _monitorTimer.Start();
        return true;
    }

    public void Disconnect()
    {
        _monitorTimer.Stop();
        lock (_captureLock)
        {
            _capture?.Release();
            _capture = null;
        }
    }

    /// <summary>
    /// 线程安全地获取一帧图像。返回的是 Mat 的 Clone 副本，确保调用者拥有唯一的引用。
    /// </summary>
    public Mat? GetMatFrame()
    {
        lock (_captureLock)
        {
            if (_capture == null || !_capture.IsOpened)
                return null;

            var mat = _capture.GetMatFrame();
            if (mat.Empty())
            {
                mat.Dispose();
                return null;
            }

            return mat.Clone();
        }
    }

    public void SetCaptureProperties(int width, int height)
    {
        lock (_captureLock)
        {
            _capture?.SetProperties(width, height);
        }
    }
}