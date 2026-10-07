using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Services;
using OpenCvSharp;
using System;

namespace EasyCon2.Avalonia.Monitoring;

public partial class MonitorViewModel : ObservableObject
{
    private readonly ICaptureService _captureService;
    private readonly object _renderLock = new();
    private System.Timers.Timer? _updateTimer;
    private System.Timers.Timer? _reconnectCheckTimer;
    private volatile bool _closed;

    // 双缓冲池：两块位图交替写入/显示，跨帧复用，避免每帧 ~16MB 的原生分配抖动
    private readonly WriteableBitmap?[] _frameBuffers = new WriteableBitmap?[2];
    private PixelSize _frameBufferSize;
    // 中间 BGRA 转换结果，CvtColor 在尺寸/类型不变时复用其缓冲，仅首帧分配
    private Mat? _bgraMat;
    // 下一帧写入的缓冲槽位；仅在 _renderLock 内（渲染线程）读写
    private int _writeIndex;
    // 已投递的帧尚未被 UI 线程应用；为真时跳过本 tick，防止覆写仍绑定在 Image.Source 上的缓冲
    private volatile bool _pendingDisplay;
    // 缓冲池重建代数；Close/变分辨率时递增，使在途的迟到帧回调失效
    private volatile int _bufferGeneration;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private string _errorMessage = L10n.T("Text.Monitor.NotConnected");

    /// <summary>
    /// 当前显示帧。指向 <see cref="_frameBuffers"/> 缓冲池中的位图，生命周期归缓冲池
    /// （Close 统一释放），所以任何路径都不得对它逐帧 Dispose。
    /// </summary>
    [ObservableProperty]
    private Bitmap? _currentFrame;

    /// <summary>
    /// 帧计数器，每次写入新帧后递增，由 VideoFrameBehavior 监听并触发 Image 刷新。
    /// </summary>
    [ObservableProperty]
    private int _frameIndex;

    public MonitorViewModel(ICaptureService captureService)
    {
        _captureService = captureService;
        // 初始化时默认显示未连接错误
        HasError = true;
        ErrorMessage = L10n.T("Text.Monitor.NotConnected");
    }

    public void StartMonitoring()
    {
        // Close 后迟到的启动请求（如重连回调）不得复活监视循环
        if (_closed) return;

        _closed = false;
        HasError = false;

        // 先停掉可能仍在运行的旧 timer，避免反复开关监视器时叠加出多个 30fps 循环
        StopMonitoring();

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
            ErrorMessage = L10n.T("Text.Monitor.ConnectFirst");
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
        if (_closed || !_captureService.IsConnected) return;

        Dispatcher.UIThread.Post(() =>
        {
            // 回调可能在 Close 之后才执行，此时不能重建监视循环
            if (_closed) return;

            StopReconnectCheckTimer();
            StartMonitoring();
        });
    }

    private void OnUpdateTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_closed)
            return;

        if (!_captureService.IsConnected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Close 之后不再转入重连检查，否则重连定时器会在关闭后永久空转
                if (_closed) return;

                HasError = true;
                ErrorMessage = L10n.T("Text.Monitor.Reconnecting");
                CurrentFrame = null;
                StopMonitoring();
                StartReconnectCheckTimer();
            });
            return;
        }

        // 上一帧尚未被 UI 线程应用：跳过本 tick，既不覆写显示中的缓冲，也避免 Post 回调堆积
        if (_pendingDisplay)
            return;

        // 1080p 渲染可能超 33ms；System.Timers.Timer 的 Elapsed 默认重入，
        // 用互斥跳过上帧未完成的 tick，避免多帧并发渲染与 Post 堆积
        if (!Monitor.TryEnter(_renderLock))
            return;

        try
        {
            using var lease = _captureService.AcquireLatestFrame();
            if (lease != null && !lease.Mat.Empty())
            {
                var bitmap = RenderFrame(lease.Mat);
                if (bitmap != null)
                {
                    _pendingDisplay = true;
                    var generation = _bufferGeneration;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _pendingDisplay = false;

                        // Close 或缓冲池重建后的迟到帧：位图归池复用，不得在此释放
                        if (_closed || _bufferGeneration != generation)
                            return;

                        // 使用属性设置器（而非直接写 _currentFrame 字段），
                        // 确保 PropertyChanged 通知触发 XAML 绑定更新 Image.Source。
                        CurrentFrame = bitmap;
                        FrameIndex++;
                        IsLoading = false;
                    });
                    // 本帧已投递，下一帧写入另一块缓冲
                    _writeIndex = 1 - _writeIndex;
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
                ErrorMessage = string.Format(L10n.T("Text.Monitor.ReadFail"), ex.Message);
                IsLoading = false;
            });
        }
        finally
        {
            Monitor.Exit(_renderLock);
        }
    }

    /// <summary>
    /// 双缓冲轮换渲染：两块 WriteableBitmap 交替写入/显示。被写入的缓冲上一次
    /// 绑定到 Image.Source 已是一帧之前（33ms），规避了渲染线程读取与写入的并发，
    /// 同时位图与中间 BGRA Mat 均跨帧复用，消除逐帧分配。
    /// </summary>
    private WriteableBitmap? RenderFrame(Mat mat)
    {
        try
        {
            // Close 后在途的 tick 到此为止：缓冲池可能已被释放，不得再新建位图
            if (_closed)
                return null;

            var size = new PixelSize(mat.Width, mat.Height);

            // 分辨率变化（重建捕获源后）丢弃旧缓冲池，使在途帧回调失效
            if (_frameBuffers[0] != null && size != _frameBufferSize)
            {
                for (var i = 0; i < _frameBuffers.Length; i++)
                {
                    _frameBuffers[i]?.Dispose();
                    _frameBuffers[i] = null;
                }
                _bufferGeneration++;
                _pendingDisplay = false;
                _writeIndex = 0;
            }

            var bitmap = _frameBuffers[_writeIndex];
            if (bitmap == null)
            {
                bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                _frameBuffers[_writeIndex] = bitmap;
            }
            _frameBufferSize = size;

            // BGR → BGRA
            _bgraMat ??= new Mat();
            Cv2.CvtColor(mat, _bgraMat, ColorConversionCodes.BGR2BGRA);

            // 使用 Mat 的实际步长，而非假设 width * 4
            var srcStep = (int)_bgraMat.Step();

            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    var copyBytes = Math.Min(srcStep, fb.RowBytes);
                    for (int y = 0; y < size.Height; y++)
                    {
                        Buffer.MemoryCopy(
                            (void*)(_bgraMat.Data + y * srcStep),
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
        _closed = true;
        _bufferGeneration++;
        StopMonitoring();
        // 先解绑显示再释放，缩小合成线程仍引用旧位图的窗口
        CurrentFrame = null;
        // 与渲染 tick 互斥：等在途 tick 离开 native 段（CvtColor/bitmap.Lock）再释放
        // Mat 与缓冲池，否则会踩已释放内存——AV 异常不可捕获，直接崩进程
        lock (_renderLock)
        {
            // CurrentFrame 指向缓冲池中的位图，由这里统一释放（不逐帧 Dispose）
            foreach (var buffer in _frameBuffers)
                buffer?.Dispose();
            Array.Clear(_frameBuffers);
            _bgraMat?.Dispose();
            _bgraMat = null;
        }
    }
}