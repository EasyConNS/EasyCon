using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Maa;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 喷射战士画板（移植自 1.6.3 WinForms 版 DrawingBoard 窗体）。算法见 <see cref="SplatoonDrawing"/>。
/// 文案经 <see cref="L10nBridge"/> 取自 Text.DrawingBoard.*；状态类文本（坐标/耗时/进度）只保存状态，
/// 读取时按当前语言生成，窗口激活时经 <see cref="RefreshLocalizedTexts"/> 刷新。
/// </summary>
public partial class DrawingBoardViewModel : ViewModelBase, IDisposable
{
    /// <summary>进度刷新间隔：每格移动只有几十毫秒，逐次刷新会塞满 UI 队列。</summary>
    private const int ProgressIntervalMs = 200;
    /// <summary>实际用时超过此值后，剩余时间按实际速度校正（设备延迟会让实际比计划慢）。</summary>
    private static readonly TimeSpan CalibrateAfter = TimeSpan.FromSeconds(5);

    private readonly IDrawingRunner _runner;
    private readonly IDialogService _dialogService;
    private readonly IImageProcessor _imageProcessor;
    private readonly IUiDispatcher _ui;
    private readonly ILogService _logService;
    private CancellationTokenSource? _drawCancellation;

    /// <summary>适配方式下拉框的顺序。</summary>
    private static readonly ImageFit[] FitModes = [ImageFit.Contain, ImageFit.Cover, ImageFit.Stretch];

    /// <summary>载入的原图（只解码一次）；切换适配方式时从它重新缩放。</summary>
    private PixelImage? _source;

    /// <summary>适配到 320×120 后的画布图片；按阈值转成 <see cref="Mask"/>。</summary>
    private PixelImage? _canvas;

    /// <summary>预览框显示的图片：默认是二值化结果（黑 = 要画的点），勾选"显示原图"时为原图。</summary>
    [ObservableProperty]
    private PixelImage? _preview;

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

    /// <summary>明暗分界，占最大亮度的百分比；50 与旧版写死的分界一致。</summary>
    [ObservableProperty]
    private int _thresholdPercent = 50;

    [ObservableProperty]
    private bool _showOriginal;

    /// <summary>抖动算法在下拉框中的下标，与 <see cref="DitherMode"/> 的顺序一致；默认 0 = 不抖动。</summary>
    [ObservableProperty]
    private int _ditherModeIndex;

    /// <summary>适配方式在 <see cref="FitModes"/> 中的下标；默认等比留白，整张图不变形。</summary>
    [ObservableProperty]
    private int _fitModeIndex;

    /// <summary>原图尺寸，例如"原图 640×480"；未载入时为空。</summary>
    [ObservableProperty]
    private string _sourceSizeText = "";

    [ObservableProperty]
    private bool _isStartEnabled = true;

    /// <summary>绘制进度 0～100。</summary>
    [ObservableProperty]
    private double _progressValue;

    private (int Col, int Row)? _curPos;
    private TimeSpan? _estimate;
    private string? _progressKey;
    private object[] _progressArgs = [];

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

    /// <summary>当前图片按阈值/反转得到的墨点掩码；未载入图片时为 null。</summary>
    public InkMask? Mask { get; private set; }

    partial void OnReverseChanged(bool value) => UpdateMask();

    partial void OnThresholdPercentChanged(int value) => UpdateMask();

    partial void OnShowOriginalChanged(bool value) => UpdateMask();

    partial void OnDitherModeIndexChanged(int value) => UpdateMask();

    private DitherMode SelectedDither => Enum.IsDefined((DitherMode)DitherModeIndex) ? (DitherMode)DitherModeIndex : DitherMode.None;

    partial void OnFitModeIndexChanged(int value) => UpdateCanvas();

    private void UpdateCanvas()
    {
        _canvas = _source == null ? null : _imageProcessor.FitTo(_source, SplatoonDrawing.CanvasWidth, SplatoonDrawing.CanvasHeight,
            FitModes[Math.Clamp(FitModeIndex, 0, FitModes.Length - 1)]);
        UpdateMask();
    }

    private void UpdateMask()
    {
        Mask = _canvas == null ? null : InkMask.Binarize(_canvas, new BinarizeOptions(ThresholdPercent, Reverse, SelectedDither));
        Preview = ShowOriginal ? _canvas : Mask?.ToPreview();
    }

    public string CurPosText => _curPos is { } pos
        ? Format("Text.DrawingBoard.CurPos", pos.Col, pos.Row)
        : T("Text.DrawingBoard.CurPosNone");

    public string EvaluateText => _estimate is { } estimate
        ? Format("Text.DrawingBoard.EstimateResult", estimate.TotalMinutes)
        : T("Text.DrawingBoard.EstimateNone");

    public string ProgressText => _progressKey == null ? "" : Format(_progressKey, _progressArgs);

    /// <summary>语言切换后由窗口激活时调用，重新生成状态类文本。</summary>
    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(CurPosText));
        OnPropertyChanged(nameof(EvaluateText));
        OnPropertyChanged(nameof(ProgressText));
    }

    [RelayCommand]
    private async Task LoadPictureAsync()
    {
        IReadOnlyList<string> files = await _dialogService.OpenFilesAsync(T("Text.DrawingBoard.LoadPicture"),
            [new FileDialogFilter(T("Text.DrawingBoard.ImageFilter"), ["*.png", "*.jpg"])],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (files.Count == 0)
            return;
        await LoadPictureFileAsync(files[0]);
    }

    internal async Task LoadPictureFileAsync(string filePath)
    {
        PixelImage? image = _imageProcessor.LoadPixels(filePath);
        if (image == null)
        {
            await ShowMessageAsync(T("Text.DrawingBoard.LoadFailed"));
            return;
        }
        // 任意尺寸都按适配方式缩放到 320×120（旧版只接受 320×120，且尺寸检查用 && 写错了）
        _source = image;
        SourceSizeText = Format("Text.DrawingBoard.SourceSize", image.Width, image.Height);
        UpdateCanvas();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!_runner.IsConnected)
        {
            await ShowMessageAsync(T("Text.DrawingBoard.NotConnected"));
            return;
        }
        if (DrawTask is { IsCompleted: false })
            return;
        if (Mask == null)
        {
            await ShowMessageAsync(T("Text.DrawingBoard.NoPicture"));
            return;
        }
        if (!TryParseSettings(out DrawingBoardSettings? settings, out string? error))
        {
            await ShowMessageAsync(error);
            return;
        }

        _drawCancellation = new CancellationTokenSource();
        CancellationToken token = _drawCancellation.Token;
        // 取开始时的掩码快照：绘制过程中调整阈值等参数不影响正在画的图
        InkMask mask = Mask;
        SetProgress(0, "Text.DrawingBoard.Preparing");
        DrawTask = Task.Run(() => RunDraw(mask, settings, token));
        IsStartEnabled = false;
    }

    private void RunDraw(InkMask mask, DrawingBoardSettings settings, CancellationToken token)
    {
        long total = 0;
        long done = 0;
        Stopwatch clock = new();
        try
        {
            List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(mask, settings);
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
            string took = FormatClock(clock.Elapsed);
            _ui.Post(() =>
            {
                SetProgress(100, "Text.DrawingBoard.Finished", took);
                IsStartEnabled = true;
            });
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // 用户停止或关闭窗口：按键已由运行租约释放，开始按钮由 StopAsync 恢复
            double percent = Percent(done, total);
            string reached = FormatPercent(done, total);
            _ui.Post(() => SetProgress(percent, "Text.DrawingBoard.Stopped", reached));
        }
        catch (Exception ex)
        {
            // 旧版任务异常被静默吞掉，开始按钮一直禁用到点击停止，用户无从得知原因
            string message = Format("Text.DrawingBoard.Error", DescribeError(ex));
            double percent = Percent(done, total);
            string reached = FormatPercent(done, total);
            _logService.AddLog(message);
            _ui.Post(() =>
            {
                SetProgress(percent, "Text.DrawingBoard.Interrupted", reached);
                IsStartEnabled = true;
                _ = ShowMessageAsync(message);
            });
        }
    }

    private void PostProgress(long done, long total, TimeSpan elapsed)
    {
        double percent = Percent(done, total);
        object[] args = [FormatPercent(done, total), FormatClock(elapsed), FormatClock(EstimateRemaining(done, total, elapsed))];
        _ui.Post(() => SetProgress(percent, "Text.DrawingBoard.Progress", args));
    }

    private void SetProgress(double value, string key, params object[] args)
    {
        ProgressValue = value;
        _progressKey = key;
        _progressArgs = args;
        OnPropertyChanged(nameof(ProgressText));
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
        _drawCancellation?.Cancel();
        // 等后台绘制真正退出（运行租约此时已释放按键）再恢复开始按钮；旧版固定等 1 秒，期间绘制可能仍在发键
        if (DrawTask != null)
            await DrawTask;
        IsStartEnabled = true;
    }

    [RelayCommand]
    private async Task EvaluateAsync()
    {
        if (Mask == null)
        {
            await ShowMessageAsync(T("Text.DrawingBoard.NoPicture"));
            return;
        }

        if (!TryParseSettings(out DrawingBoardSettings? settings, out string? error))
        {
            await ShowMessageAsync(error);
            return;
        }

        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(Mask, settings);
        _estimate = SplatoonDrawing.Estimate(rows, settings.Wait, settings.Duration);
        OnPropertyChanged(nameof(EvaluateText));
    }

    /// <summary>
    /// 预览图左键按下：按预览框实际尺寸换算成 320×120 画布坐标（列,行）。
    /// 旧版固定除以 2，但预览框是 822×282 拉伸显示，显示的坐标与画布对不上。
    /// </summary>
    public void OnPicturePressed(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;
        int col = Math.Clamp((int)(x * SplatoonDrawing.CanvasWidth / width), 0, SplatoonDrawing.CanvasWidth - 1);
        int row = Math.Clamp((int)(y * SplatoonDrawing.CanvasHeight / height), 0, SplatoonDrawing.CanvasHeight - 1);
        _curPos = (col, row);
        OnPropertyChanged(nameof(CurPosText));
    }

    /// <summary>窗口关闭时调用：中止进行中的绘制（运行租约结束时松开全部按键）。</summary>
    public void Dispose()
    {
        _drawCancellation?.Cancel();
    }

    /// <summary>
    /// 校验并读取各输入框。旧版直接 int.Parse，非数字会让绘制任务静默中断，
    /// 负数或越界的起点会在扫描时越界。
    /// </summary>
    private bool TryParseSettings([NotNullWhen(true)] out DrawingBoardSettings? settings, [NotNullWhen(false)] out string? error)
    {
        settings = null;
        if (!TryParseField("Text.DrawingBoard.Delay", DelayText, 0, 10000, out int wait, out error)
            || !TryParseField("Text.DrawingBoard.Duration", DurationText, 1, 10000, out int duration, out error)
            || !TryParseField("Text.DrawingBoard.StartRow", StartPosYText, 0, SplatoonDrawing.CanvasHeight - 1, out int startRow, out error)
            || !TryParseField("Text.DrawingBoard.StartCol", StartPosXText, 0, SplatoonDrawing.CanvasWidth - 1, out int startCol, out error)
            || !TryParseField("Text.DrawingBoard.ColCount", WidthText, 1, SplatoonDrawing.CanvasWidth, out int colCount, out error)
            || !TryParseField("Text.DrawingBoard.RowCount", HeightText, 1, SplatoonDrawing.CanvasHeight, out int rowCount, out error))
            return false;
        settings = new DrawingBoardSettings(wait, duration, startRow, startCol, colCount, rowCount);
        return true;
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        ControllerOperationException { ErrorCode: "Busy" } => T("Text.DrawingBoard.Busy"),
        ControllerOperationException { ErrorCode: "InputDisconnected" } => T("Text.DrawingBoard.NotConnected"),
        // 未经用户停止的取消来自运行租约：设备断开或被接管
        OperationCanceledException => T("Text.DrawingBoard.Disconnected"),
        _ => ex.Message,
    };

    private static bool TryParseField(string nameKey, string text, int min, int max, out int value, [NotNullWhen(false)] out string? error)
    {
        if (int.TryParse(text.Trim(), out value) && value >= min && value <= max)
        {
            error = null;
            return true;
        }
        error = Format("Text.DrawingBoard.InvalidField", T(nameKey), min, max);
        return false;
    }

    private Task ShowMessageAsync(string message) => _dialogService.ShowMessageAsync(T("Text.DrawingBoard.Title"), message);

    private static string T(string key) => L10nBridge.T(key);

    private static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}