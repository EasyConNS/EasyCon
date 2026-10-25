using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Maa;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 喷射战士画板（移植自 1.6.3 WinForms 版 DrawingBoard 窗体）。
/// 输入框默认值、提示文案与按钮启用时机均与旧版一致；算法见 <see cref="SplatoonDrawing"/>。
/// </summary>
public partial class DrawingBoardViewModel : ViewModelBase, IDisposable
{
    private const string MessageTitle = "画板";
    /// <summary>进度刷新间隔：每格移动只有几十毫秒，逐次刷新会塞满 UI 队列。</summary>
    private const int ProgressIntervalMs = 200;
    /// <summary>实际用时超过此值后，剩余时间按实际速度校正（设备延迟会让实际比计划慢）。</summary>
    private static readonly TimeSpan CalibrateAfter = TimeSpan.FromSeconds(5);

    private readonly IDrawingRunner _runner;
    private readonly IDialogService _dialogService;
    private readonly IImageProcessor _imageProcessor;
    private readonly IUiDispatcher _ui;
    private readonly ILogService _logService;
    private PixelImage? _image;
    private CancellationTokenSource? _source;

    [ObservableProperty]
    private byte[]? _picturePng;

    [ObservableProperty]
    private string _startPosXText = "0";

    [ObservableProperty]
    private string _startPosYText = "0";

    [ObservableProperty]
    private string _widthText = "320";

    [ObservableProperty]
    private string _heightText = "120";

    [ObservableProperty]
    private string _durationText = "50";

    [ObservableProperty]
    private string _delayText = "45";

    [ObservableProperty]
    private bool _reverse;

    [ObservableProperty]
    private bool _isStartEnabled = true;

    [ObservableProperty]
    private string _evaluateText = "耗时：";

    [ObservableProperty]
    private string _curPosText = "当前坐标：100，40";

    /// <summary>绘制进度 0～100。</summary>
    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "";

    public DrawingBoardViewModel(IDrawingRunner runner, IDialogService dialogService, IImageProcessor imageProcessor, IUiDispatcher ui, ILogService logService)
    {
        _runner = runner;
        _dialogService = dialogService;
        _imageProcessor = imageProcessor;
        _ui = ui;
        _logService = logService;
    }

    /// <summary>最近一次开始画图的后台任务（测试用于等待绘制结束）。</summary>
    internal Task? DrawTask { get; private set; }

    [RelayCommand]
    private async Task LoadPictureAsync()
    {
        IReadOnlyList<string> files = await _dialogService.OpenFilesAsync("加载图片",
            [new FileDialogFilter("Files", ["*.png", "*.jpg"])],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (files.Count == 0)
            return;
        await LoadPictureFileAsync(files[0]);
    }

    internal async Task LoadPictureFileAsync(string filepath)
    {
        PixelImage? image = _imageProcessor.LoadPixels(filepath);
        if (image == null)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "图片加载失败");
            return;
        }
        // 旧版用 && 判断（宽高都不对才提示），且提示后仍会用该图绘制，小图会在绘制中途越界
        if (image.Width != 320 || image.Height != 120)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "图片不是320*120大小，请重新修改");
            return;
        }

        _image = image;
        PicturePng = _imageProcessor.LoadAsPng(filepath);
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!_runner.IsConnected)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "串口未连接");
            return;
        }
        if (DrawTask is { IsCompleted: false })
            return;
        if (_image == null)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "未加载图片");
            return;
        }
        if (!TryParseSettings(out DrawingBoardSettings? settings, out string? error))
        {
            await _dialogService.ShowMessageAsync(MessageTitle, error);
            return;
        }

        _source = new CancellationTokenSource();
        CancellationToken token = _source.Token;
        PixelImage image = _image;
        ProgressValue = 0;
        ProgressText = "准备中…";
        //开启一个task执行任务
        DrawTask = Task.Run(() => DrawTaskOpt(image, settings, token));
        IsStartEnabled = false;
    }

    private void DrawTaskOpt(PixelImage image, DrawingBoardSettings settings, CancellationToken token)
    {
        long total = 0;
        long done = 0;
        Stopwatch clock = new();
        try
        {
            List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, settings);
            total = (long)SplatoonDrawing.Estimate(rows, settings.Wait, settings.Duration).TotalMilliseconds;
            _runner.Run((pad, runToken) =>
            {
                // 取得手柄后才开始计时
                clock.Start();
                long lastReport = -ProgressIntervalMs;
                PostProgress(0, total, TimeSpan.Zero);
                SplatoonDrawing drawing = new(pad, settings.Wait, settings.Duration, progress: value =>
                {
                    done = value;
                    long now = clock.ElapsedMilliseconds;
                    if (now - lastReport < ProgressIntervalMs)
                        return;
                    lastReport = now;
                    PostProgress(done, total, clock.Elapsed);
                });
                drawing.Draw(rows, runToken);
            }, token);
            string finished = "已完成 · 用时 " + FormatClock(clock.Elapsed);
            _ui.Post(() =>
            {
                ProgressValue = 100;
                ProgressText = finished;
                IsStartEnabled = true;
            });
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // 用户停止或关闭窗口：按键已由运行租约释放，开始按钮由 StopAsync 恢复
            string stopped = $"已停止 · 完成 {FormatPercent(done, total)}";
            _ui.Post(() => ProgressText = stopped);
        }
        catch (Exception ex)
        {
            // 旧版任务异常被静默吞掉，开始按钮一直禁用到点击停止，用户无从得知原因
            string message = "画图中断: " + DescribeError(ex);
            string interrupted = $"已中断 · 完成 {FormatPercent(done, total)}";
            _logService.AddLog(message);
            _ui.Post(() =>
            {
                ProgressText = interrupted;
                IsStartEnabled = true;
                _ = _dialogService.ShowMessageAsync(MessageTitle, message);
            });
        }
    }

    private void PostProgress(long done, long total, TimeSpan elapsed)
    {
        double percent = Percent(done, total);
        string text = $"{FormatPercent(done, total)} · 已用 {FormatClock(elapsed)} · 剩余约 {FormatClock(EstimateRemaining(done, total, elapsed))}";
        _ui.Post(() =>
        {
            ProgressValue = percent;
            ProgressText = text;
        });
    }

    private static double Percent(long done, long total) => total > 0 ? Math.Min(100, done * 100.0 / total) : 100;

    /// <summary>向下取整，避免没画完就显示 100%。</summary>
    private static string FormatPercent(long done, long total) => Math.Floor(Percent(done, total)) + "%";

    /// <summary>
    /// 剩余时间：起步阶段按计划耗时；实际用时超过 <see cref="CalibrateAfter"/> 后，按"实际用时 / 已完成计划耗时"的比例校正。
    /// </summary>
    internal static TimeSpan EstimateRemaining(long done, long total, TimeSpan elapsed)
    {
        long planned = Math.Max(0, total - done);
        if (done <= 0 || elapsed < CalibrateAfter)
            return TimeSpan.FromMilliseconds(planned);
        return TimeSpan.FromMilliseconds(planned * elapsed.TotalMilliseconds / done);
    }

    internal static string FormatClock(TimeSpan time)
        => time.TotalHours >= 1 ? (int)time.TotalHours + ":" + time.ToString(@"mm\:ss") : time.ToString(@"mm\:ss");

    [RelayCommand]
    private async Task StopAsync()
    {
        _source?.Cancel();
        // 等后台绘制真正退出（运行租约此时已释放按键）再恢复开始按钮；旧版固定等 1 秒，期间绘制可能仍在发键
        if (DrawTask != null)
            await DrawTask;
        IsStartEnabled = true;
    }

    [RelayCommand]
    private async Task EvaluateAsync()
    {
        if (_image == null)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "未加载图片");
            return;
        }

        if (!TryParseSettings(out DrawingBoardSettings? settings, out string? error))
        {
            await _dialogService.ShowMessageAsync(MessageTitle, error);
            return;
        }

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(_image, settings);
        TimeSpan elapsed = SplatoonDrawing.Estimate(rows, settings.Wait, settings.Duration);
        EvaluateText = SplatoonDrawing.FormatEvaluation(elapsed);
    }

    /// <summary>
    /// 预览图左键按下：按预览框实际尺寸换算成 320×120 画布坐标（列,行）。
    /// 旧版固定除以 2，但预览框是 822×282 拉伸显示，显示的坐标与画布对不上。
    /// </summary>
    public void OnPicturePressed(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;
        int col = Math.Clamp((int)(x * 320 / width), 0, 319);
        int row = Math.Clamp((int)(y * 120 / height), 0, 119);
        CurPosText = "当前位置:" + col.ToString() + "," + row.ToString();
    }

    /// <summary>窗口关闭时调用：中止进行中的绘制（运行租约结束时松开全部按键）。</summary>
    public void Dispose()
    {
        _source?.Cancel();
    }

    /// <summary>
    /// 校验并读取各输入框。旧版直接 int.Parse，非数字会让绘制任务静默中断，
    /// 负数或越界的起点会在扫描时越界。
    /// </summary>
    private bool TryParseSettings([NotNullWhen(true)] out DrawingBoardSettings? settings, [NotNullWhen(false)] out string? error)
    {
        settings = null;
        if (!TryParseField("延迟", DelayText, 0, 10000, out int wait, out error)
            || !TryParseField("按键持续", DurationText, 1, 10000, out int duration, out error)
            || !TryParseField("起始行", StartPosYText, 0, 119, out int start_row, out error)
            || !TryParseField("起始列", StartPosXText, 0, 319, out int start_col, out error)
            || !TryParseField("列数", WidthText, 1, 320, out int col, out error)
            || !TryParseField("行数", HeightText, 1, 120, out int row, out error))
            return false;
        settings = new DrawingBoardSettings(wait, duration, start_row, start_col, col, row, Reverse);
        return true;
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        ControllerOperationException { ErrorCode: "Busy" } => "手柄正被脚本或其他任务占用，请先停止后再画",
        ControllerOperationException { ErrorCode: "InputDisconnected" } => "串口未连接",
        // 未经用户停止的取消来自运行租约：设备断开或被接管
        OperationCanceledException => "手柄连接已断开",
        _ => ex.Message,
    };

    private static bool TryParseField(string name, string text, int min, int max, out int value, [NotNullWhen(false)] out string? error)
    {
        if (int.TryParse(text.Trim(), out value) && value >= min && value <= max)
        {
            error = null;
            return true;
        }
        error = $"{name}应为 {min}～{max} 的整数";
        return false;
    }
}