using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Maa;
using EasyCon.Core.Services;
using OpenCvSharp;

namespace EasyCon2.Avalonia.Services;

public class CaptureService : ICaptureService, IDisposable
{
    private readonly ILogService _logService;
    private readonly FrameHub _frames;
    private readonly bool _ownsFrames;
    private readonly System.Timers.Timer _monitorTimer = new(1000);
    private readonly Dictionary<string, CaptureSourceDescriptor> _sourceIndexMap = new();
    private readonly object _sourceIndexLock = new();

    public CaptureService(ILogService logService, FrameHub? frames = null)
    {
        _logService = logService;
        _frames = frames ?? new FrameHub();
        _ownsFrames = frames == null;
        _monitorTimer.Elapsed += (_, _) =>
        {
            if (_frames.CaptureConnected) return;
            _monitorTimer.Stop();
            Dispatcher.UIThread.Post(() =>
            {
                ConnectionLost?.Invoke();
                _logService.AddLog($"视频源不可用: {_frames.LastError ?? "已断开或等待有效帧超时"}");
            });
        };
    }

    public string CaptureType { get; set; } = "ANY";
    public FrameHub FrameHub => _frames;
    public string? ActiveSource => _frames.Selection?.DisplayKey ?? (_frames.DeviceIndex is { } index ? $"视频源 {index}" : _frames.SourceId);
    public FrameNormalization? Normalization { get; set; }
    public string? ActiveBackend => _frames.Backend?.ToString();
    public CaptureSourceDescriptor? DescribeSelection(string? displayKey)
    {
        lock (_sourceIndexLock)
            return displayKey != null && _sourceIndexMap.TryGetValue(displayKey, out CaptureSourceDescriptor? source) ? source : null;
    }
    public bool IsConnected => _frames.CaptureConnected;
    public event Action? ConnectionLost;
#pragma warning disable CS0067
    public event Action? ConnectionRestored;
#pragma warning restore CS0067
    public event Action? ConnectionStateChanged;

    public string[] GetAvailableSources()
    {
        IReadOnlyList<CaptureSourceDescriptor> sources;
        try { sources = CaptureSourceCatalog.Enumerate(GetCaptureApi()); }
        catch (Exception ex)
        {
            _logService.AddLog($"无法枚举所选采集后端: {ex.Message}");
            lock (_sourceIndexLock) _sourceIndexMap.Clear();
            return [];
        }
        lock (_sourceIndexLock)
        {
            _sourceIndexMap.Clear();
            foreach (CaptureSourceDescriptor source in sources) _sourceIndexMap[source.DisplayKey] = source;
        }
        return sources.Select(x => x.DisplayKey).ToArray();
    }

    public bool TryConnect(string sourceName)
    {
        CaptureSourceDescriptor? source;
        lock (_sourceIndexLock) _sourceIndexMap.TryGetValue(sourceName, out source);
        if (source == null || source.Backend != CaptureSourceCatalog.ResolveBackend(GetCaptureApi()))
        {
            _logService.AddLog($"视频来源不可用，请刷新并重新选择: {sourceName}");
            return false;
        }
        return Connect(() => _frames.Connect(source, Normalization));
    }

    public bool TryConnect(int deviceId)
    {
        CaptureSourceDescriptor? source;
        lock (_sourceIndexLock) source = _sourceIndexMap.Values.SingleOrDefault(s => s.Index == deviceId && s.Backend == CaptureSourceCatalog.ResolveBackend(GetCaptureApi()));
        if (source == null)
        {
            _logService.AddLog($"视频序号不属于当前后端的枚举，请刷新来源: {deviceId}");
            return false;
        }
        return Connect(() => _frames.Connect(source, Normalization));
    }

    public IReadOnlyList<CaptureSourceDescriptor> GetSourceDescriptors()
    {
        GetAvailableSources();
        lock (_sourceIndexLock) return _sourceIndexMap.Values.ToArray();
    }

    private VideoCaptureAPIs GetCaptureApi() => CaptureType switch
    {
        "DSHOW" => VideoCaptureAPIs.DSHOW,
        "MSMF" => VideoCaptureAPIs.MSMF,
        "DC1394" => VideoCaptureAPIs.DC1394,
        _ => VideoCaptureAPIs.ANY,
    };

    private bool Connect(Func<bool> open)
    {
        _monitorTimer.Stop();
        try
        {
            return open();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"视频源连接失败: {ex.Message}");
            return false;
        }
        finally
        {
            if (IsConnected) _monitorTimer.Start();
            ConnectionStateChanged?.Invoke();
        }
    }

    public Task DisconnectAsync()
    {
        _monitorTimer.Stop();
        return Task.Run(() =>
        {
            bool stopped = _frames.Disconnect();
            ConnectionStateChanged?.Invoke();
            if (!stopped) throw new InvalidOperationException("视频源正在停止，等待旧 Read 退出");
        });
    }

    public FrameLease? AcquireLatestFrame() => _frames.AcquireLatest();
    public FrameStore? GetFrameStore() => _frames.CaptureConnected ? _frames.Store : null;

    public void SetCaptureProperties(int width, int height)
    {
        if (width != FrameNormalization.Width || height != FrameNormalization.Height)
            throw new ArgumentException("统一帧源固定为 1920×1080；变换请配置 FrameNormalization");
    }

    public void Dispose()
    {
        _monitorTimer.Dispose();
        if (_ownsFrames) _frames.Dispose();
    }
}