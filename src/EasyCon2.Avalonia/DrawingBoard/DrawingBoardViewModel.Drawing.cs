using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Maa;
using EasyScript;
using System.Diagnostics;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>绘制：开始/停止、后台按运行租约发键（含橡皮擦模式）、进度与剩余时间、错误描述。</summary>
public partial class DrawingBoardViewModel
{
    /// <summary>进度刷新间隔：每格移动只有几十毫秒，逐次刷新会塞满 UI 队列。</summary>
    private const int ProgressIntervalMs = 200;
    /// <summary>实际用时超过此值后，剩余时间按实际速度校正（设备延迟会让实际比计划慢）。</summary>
    private static readonly TimeSpan CalibrateAfter = TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _drawCancellation;

    /// <summary>
    /// 橡皮擦模式：用户事先把整个画布涂黑，程序按住 B（橡皮擦）擦出应留白的点，而不是按住 A（画笔）画出墨点。
    /// 最终效果相同；黑多白少的图要处理的点更少、更快。预览仍显示最终效果。
    /// </summary>
    [ObservableProperty]
    private bool _eraserMode;

    [ObservableProperty]
    private bool _isStartEnabled = true;

    /// <summary>绘制进度 0～100。</summary>
    [ObservableProperty]
    private double _progressValue;

    private string? _progressKey;
    private object[] _progressArgs = [];

    /// <summary>最近一次开始画图的后台任务（测试用于等待绘制结束）。</summary>
    internal Task? DrawTask { get; private set; }

    partial void OnEraserModeChanged(bool value) => UpdateEstimate();

    /// <summary>实际要走笔的点：画笔模式是墨点，橡皮擦模式是要擦成空白的点。</summary>
    private InkMask StrokeMask(InkMask mask) => EraserMode ? mask.Inverted() : mask;

    private GamePadKey StrokeKey => EraserMode ? GamePadKey.B : GamePadKey.A;

    public string ProgressText => _progressKey == null ? "" : Format(_progressKey, _progressArgs);

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
        // 取开始时的掩码与模式快照：绘制过程中调整阈值、切换模式等不影响正在画的图
        InkMask strokes = StrokeMask(Mask);
        GamePadKey key = StrokeKey;
        SetProgress(0, "Text.DrawingBoard.Preparing");
        DrawTask = Task.Run(() => RunDraw(strokes, key, settings, token));
        IsStartEnabled = false;
    }

    private void RunDraw(InkMask strokes, GamePadKey key, DrawingBoardSettings settings, CancellationToken token)
    {
        long total = 0;
        long done = 0;
        Stopwatch clock = new();
        try
        {
            List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(strokes, settings);
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
                }, penKey: key);
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

    private static string DescribeError(Exception ex) => ex switch
    {
        ControllerOperationException { ErrorCode: "Busy" } => T("Text.DrawingBoard.Busy"),
        ControllerOperationException { ErrorCode: "InputDisconnected" } => T("Text.DrawingBoard.NotConnected"),
        // 未经用户停止的取消来自运行租约：设备断开或被接管
        OperationCanceledException => T("Text.DrawingBoard.Disconnected"),
        _ => ex.Message,
    };
}