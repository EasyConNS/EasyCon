using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core;
using OpenCvSharp;

namespace EasyCon2.Avalonia.Services;

public class CaptureService : ICaptureService
{
    private readonly ILogService _logService;
    private readonly System.Timers.Timer _monitorTimer = new(1000);
    private readonly Dictionary<string, int> _sourceIndexMap = new();
    private OpenCVCapture? _capture;

    public bool IsConnected => _capture?.IsOpened ?? false;
    public event Action? ConnectionLost;

    public CaptureService(ILogService logService)
    {
        _logService = logService;

        _monitorTimer.Elapsed += (s, e) =>
        {
            if (_capture == null || !_capture.IsOpened)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _monitorTimer.Stop();
                    ConnectionLost?.Invoke();
                    _logService.AddLog("视频源已从外部断开");
                });
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

        _capture?.Dispose();
        _capture = new OpenCVCapture();
        if (!_capture.Open(deviceId, (int)VideoCaptureAPIs.ANY))
        {
            _capture = null;
            return false;
        }

        _monitorTimer.Start();
        return true;
    }

    public void Disconnect()
    {
        _monitorTimer.Stop();
        _capture?.Release();
        _capture = null;
    }

    public OpenCVCapture? GetCapture() => _capture;
}
