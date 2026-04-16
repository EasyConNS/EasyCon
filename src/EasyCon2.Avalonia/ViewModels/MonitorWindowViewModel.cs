using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.ViewModels;

public partial class MonitorWindowViewModel : ObservableObject
{
    private readonly ICaptureService _captureService;
    private System.Timers.Timer? _updateTimer;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private string _errorMessage = "视频源未连接";

    [ObservableProperty]
    private Bitmap? _currentFrame;

    public MonitorWindowViewModel(ICaptureService captureService)
    {
        _captureService = captureService;
    }

    public void StartMonitoring()
    {
        if (_captureService.IsConnected)
        {
            HasError = false;
            IsLoading = true;

            // 启动定时器，30fps更新
            _updateTimer = new System.Timers.Timer(33); // ~30fps
            _updateTimer.Elapsed += OnUpdateTimerElapsed;
            _updateTimer.Start();
        }
        else
        {
            HasError = true;
            ErrorMessage = "视频源未连接，请先连接视频源";
        }
    }

    public void StopMonitoring()
    {
        _updateTimer?.Stop();
        _updateTimer = null;
        IsLoading = false;
    }

    private void OnUpdateTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_captureService.IsConnected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HasError = true;
                ErrorMessage = "视频源已断开连接";
                StopMonitoring();
            });
            return;
        }

        try
        {
            var capture = _captureService.GetCapture();
            if (capture != null && capture.IsOpened)
            {
                var mat = capture.GetMatFrame();
                if (!mat.Empty())
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateImage(mat);
                        IsLoading = false;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HasError = true;
                ErrorMessage = $"视频读取失败: {ex.Message}";
            });
        }
    }

    private void UpdateImage(OpenCvSharp.Mat mat)
    {
        try
        {
            // 将OpenCV Mat转换为Avalonia可用的Bitmap
            CurrentFrame = ConvertMatToBitmap(mat);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"图像转换失败: {ex.Message}";
        }
    }

    private Bitmap? ConvertMatToBitmap(OpenCvSharp.Mat mat)
    {
        try
        {
            // 将Mat转换为字节数组
            var matRgb = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.CvtColor(mat, matRgb, OpenCvSharp.ColorConversionCodes.BGR2RGB);

            // 编码为PNG格式
            var encoded = matRgb.ToBytes(".png");
            if (encoded == null || encoded.Length == 0)
            {
                return null;
            }

            // 从内存流创建Bitmap
            using var stream = new System.IO.MemoryStream(encoded);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"图像转换错误: {ex.Message}");
            return null;
        }
    }

    private void Close()
    {
        StopMonitoring();
        CurrentFrame = null;
    }
}
