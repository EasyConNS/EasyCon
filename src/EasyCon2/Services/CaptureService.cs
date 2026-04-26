using EasyCon.Capture;
using EasyCon.Core;
using EasyCon2.Forms;

namespace EasyCon2.Services;

public class CaptureService
{
    private CaptureVideoForm? _captureForm;
    private OpenCVCapture? _cvcap;
    private readonly object _frameLock = new();

    /// <summary>连接状态变化</summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>状态栏消息</summary>
    public event Action<string>? StatusChanged;

    public bool IsConnected => _captureForm?.IsOpened ?? false;

    public IEnumerable<ImgLabel> LoadedLabels =>
        _captureForm?.LoadedLabels ?? [];

    /// <summary>
    /// 获取可用视频源列表
    /// </summary>
    public static IEnumerable<(string name, int index)> GetVideoSources()
        => ECCore.GetCaptureSources();

    /// <summary>
    /// 获取采集卡类型列表
    /// </summary>
    public static IEnumerable<(string name, int value)> GetCaptureTypes()
        => ECCore.GetCaptureTypes();

    public void LoadImgLabels(string imgLabelPath)
    {
        _captureForm?.LoadImgLabels(imgLabelPath);
        StatusChanged?.Invoke($"已加载搜图标签：{LoadedLabels.Count()}");
    }
    /// <summary>
    /// 连接视频源
    /// </summary>
    public bool Connect(int deviceId, int captureType, string imgLabelPath)
    {
        try
        {
            _captureForm?.ForceClose();
            _cvcap?.Release();

            _cvcap = new OpenCVCapture();
            if (!_cvcap.Open(deviceId, captureType))
            {
                _cvcap.Release();
                _cvcap = null;
                StatusChanged?.Invoke("当前采集卡已经被其他程序打开，请先关闭后再尝试");
                return false;
            }
            _cvcap.SetProperties(1920, 1080);

            _captureForm = new CaptureVideoForm(_cvcap, _frameLock);
            LoadImgLabels(imgLabelPath);
            ConnectionStateChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"视频源连接失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 断开视频源
    /// </summary>
    public void Disconnect()
    {
        _captureForm?.ForceClose();
        _cvcap?.Release();
        _cvcap = null;
        _captureForm = null;
        ConnectionStateChanged?.Invoke(false);
        StatusChanged?.Invoke("视频源已断开");
    }

    /// <summary>
    /// 打开搜图控制台
    /// </summary>
    public void ShowCaptureConsole()
    {
        if (_captureForm == null || _captureForm.IsDisposed)
        {
            StatusChanged?.Invoke("请先连接视频源");
            return;
        }

        if (!_captureForm.Visible)
            _captureForm.Show();
        else
            _captureForm.Activate();
    }

    /// <summary>
    /// 构建图像搜索外部变量字典（供脚本编译使用）
    /// </summary>
    public Dictionary<string, Func<int>> BuildExternalGetters()
    {
        if (_captureForm == null || _cvcap == null)
            return [];

        var cap = _cvcap;
        var frameLock = _frameLock;

        return _captureForm.LoadedLabels.ToDictionary(
            il => il.name,
            il => (Func<int>)(() =>
            {
                lock (frameLock)
                {
                    using var mat = cap.GetMatFrame();
                    if (mat.Empty()) return 0;
                    il.Search(mat, out var md);
                    return (int)Math.Ceiling(md);
                }
            }));
    }
}