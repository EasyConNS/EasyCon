using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Services;

namespace EasyCon2.Avalonia.Core.Services;

public class CaptureService : ICaptureService
{
    private readonly ILogService _logService;
    private readonly Dictionary<string, int> _sourceIndexMap = new();
    private OpenCVCapture? _capture;

    public bool IsOpened { get; private set; }
    public IEnumerable<ImgLabelInfo> LoadedLabels => _loadedLabels;
    private List<ImgLabelInfo> _loadedLabels = [];

    public event Action? CaptureStatusChanged;

    public CaptureService(ILogService logService)
    {
        _logService = logService;
    }

    public void LoadImgLabels(string path)
    {
        var (labels, _, _) = ECCore.LoadImgLabels(path, AppDomain.CurrentDomain.BaseDirectory);
        _loadedLabels = labels.Select(il => new ImgLabelInfo(il.name)).ToList();
        _logService.AddLog($"已加载搜图标签：{_loadedLabels.Count}");
    }

    public string[] GetCaptureSources()
    {
        var sources = ECCore.GetCaptureSources().ToArray();
        _sourceIndexMap.Clear();
        foreach (var (name, index) in sources)
            _sourceIndexMap[name] = index;
        return sources.Select(d => d.name).ToArray();
    }

    public bool TryConnect(string sourceName)
    {
        if (!_sourceIndexMap.TryGetValue(sourceName, out var deviceId))
        {
            _logService.AddLog($"未找到视频源: {sourceName}");
            return false;
        }

        _capture = new OpenCVCapture();
        if (!_capture.Open(deviceId))
        {
            _logService.AddLog("视频源连接失败");
            _capture.Dispose();
            _capture = null;
            return false;
        }

        _capture.SetResolution(1920, 1080);
        IsOpened = true;
        CaptureStatusChanged?.Invoke();
        _logService.AddLog("视频源已连接");
        return true;
    }

    public void Disconnect()
    {
        _capture?.Dispose();
        _capture = null;
        IsOpened = false;
        CaptureStatusChanged?.Invoke();
        _logService.AddLog("视频源已断开");
    }

    public (string name, int value)[] GetCaptureTypes()
    {
        return ECCore.GetCaptureTypes().ToArray();
    }

    public void SetOpened(bool opened)
    {
        IsOpened = opened;
        CaptureStatusChanged?.Invoke();
    }
}