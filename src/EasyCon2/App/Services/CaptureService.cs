using EasyCon.Capture;
using EasyCon.Core;
using EasyCon2.Forms;
using OpenCvSharp.Extensions;
using System.Drawing;

namespace EasyCon2.App.Services;

public class CaptureService
{
    private CaptureVideoForm? _captureForm;

    /// <summary>连接状态变化</summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>状态栏消息</summary>
    public event Action<string>? StatusChanged;

    /// <summary>标签加载完成</summary>
    public event Action<int>? LabelsLoaded;

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

    /// <summary>
    /// 连接视频源
    /// </summary>
    public bool Connect(int deviceId, int captureType, string imgLabelPath)
    {
        try
        {
            _captureForm?.Close();
            _captureForm = new CaptureVideoForm(deviceId, captureType);
            _captureForm.FormClosing += (s, e) =>
            {
                // 隐藏窗口而非关闭，保持视频源连接
                e.Cancel = true;
                ((Form)s!).Hide();
            };
            _captureForm.LoadImgLabels(imgLabelPath);
            StatusChanged?.Invoke($"已加载搜图标签：{LoadedLabels.Count()}");
            LabelsLoaded?.Invoke(LoadedLabels.Count());
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
        _captureForm?.Close();
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
    /// 获取当前帧
    /// </summary>
    public Bitmap? GetCurrentFrame()
    {
        return _captureForm?.GetImage();
    }

    /// <summary>
    /// 构建图像搜索外部变量字典（供脚本编译使用）
    /// </summary>
    public Dictionary<string, Func<int>> BuildExternalGetters()
    {
        if (_captureForm == null)
            return [];

        return _captureForm.LoadedLabels.ToDictionary(
            il => il.name,
            il => (Func<int>)(() =>
            {
                var bmp = GetCurrentFrame();
                if (bmp == null) return 0;
                using var mat = BitmapConverter.ToMat(bmp);
                il.Search(mat, out var md);
                return (int)Math.Ceiling(md);
            }));
    }
}
