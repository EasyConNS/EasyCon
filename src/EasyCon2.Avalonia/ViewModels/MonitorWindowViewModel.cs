using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EasyCon2.Avalonia.Services;
using OpenCvSharp;

namespace EasyCon2.Avalonia.ViewModels;

public partial class MonitorWindowViewModel : ObservableObject
{
    private readonly ICaptureService _captureService;
    private System.Timers.Timer? _updateTimer;
    private System.Timers.Timer? _reconnectCheckTimer;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private string _errorMessage = "视频源未连接";

    [ObservableProperty]
    private Bitmap? _currentFrame;

    /// <summary>
    /// 帧计数器，每次写入新帧后递增，由 VideoFrameBehavior 监听并触发 Image 刷新。
    /// </summary>
    [ObservableProperty]
    private int _frameIndex;

    public MonitorWindowViewModel(ICaptureService captureService)
    {
        _captureService = captureService;
    }

    public void StartMonitoring()
    {
        HasError = false;

        if (_captureService.IsConnected)
        {
            IsLoading = true;
            StopReconnectCheckTimer();

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
        _updateTimer?.Dispose();
        _updateTimer = null;
        StopReconnectCheckTimer();
        IsLoading = false;
    }

    private void StartReconnectCheckTimer()
    {
        if (_reconnectCheckTimer != null) return;

        _reconnectCheckTimer = new System.Timers.Timer(2000);
        _reconnectCheckTimer.Elapsed += OnReconnectCheckElapsed;
        _reconnectCheckTimer.Start();
    }

    private void StopReconnectCheckTimer()
    {
        _reconnectCheckTimer?.Stop();
        _reconnectCheckTimer?.Dispose();
        _reconnectCheckTimer = null;
    }

    private void OnReconnectCheckElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_captureService.IsConnected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StopReconnectCheckTimer();
                StartMonitoring();
            });
        }
    }

    private void OnUpdateTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_captureService.IsConnected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HasError = true;
                ErrorMessage = "视频源已断开连接，等待重连...";
                CurrentFrame = null;
                _updateTimer?.Stop();
                _updateTimer?.Dispose();
                _updateTimer = null;
                StartReconnectCheckTimer();
            });
            return;
        }

        try
        {
            using var mat = _captureService.GetMatFrame();
            if (mat != null && !mat.Empty())
            {
                var bitmap = RenderFrame(mat);
                if (bitmap != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        // 使用属性设置器（而非直接写 _currentFrame 字段），
                        // 确保 PropertyChanged 通知触发 XAML 绑定更新 Image.Source。
                        var oldFrame = CurrentFrame;
                        CurrentFrame = bitmap;
                        FrameIndex++;
                        IsLoading = false;
                        // 旧帧不再被 Image 引用，可安全释放
                        oldFrame?.Dispose();
                    });
                }
                else
                {
                    // 渲染失败，但也需要关闭加载状态
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsLoading = false;
                        System.Diagnostics.Debug.WriteLine("警告: RenderFrame 返回 null");
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
                IsLoading = false;
            });
        }
    }

    /// <summary>
    /// 将 Mat 写入新建的 WriteableBitmap，避免缓冲区复用导致的并发访问问题。
    /// 每帧创建独立的 Bitmap，确保引用变化触发绑定刷新，旧帧由 UI 线程负责释放。
    /// </summary>
    private WriteableBitmap? RenderFrame(Mat mat)
    {
        try
        {
            var width = mat.Width;
            var height = mat.Height;
            var size = new PixelSize(width, height);

            // BGR → BGRA
            using var bgra = new Mat();
            Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);

            // 使用 Mat 的实际步长，而非假设 width * 4
            var srcStep = (int)bgra.Step();

            // 每帧创建独立的 WriteableBitmap，避免与渲染线程的并发访问问题
            var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    var copyBytes = Math.Min(srcStep, fb.RowBytes);
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            (void*)(bgra.Data + y * srcStep),
                            (void*)(fb.Address + y * fb.RowBytes),
                            fb.RowBytes,
                            copyBytes);
                    }
                }
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"图像渲染错误: {ex.Message}");
            return null;
        }
    }

    public void Close()
    {
        StopMonitoring();
        CurrentFrame?.Dispose();
        CurrentFrame = null;
    }
}