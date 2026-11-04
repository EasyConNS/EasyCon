using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Maa;
using EasyCon2.Avalonia.Models;
using EasyScript;
using System.Diagnostics;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 绘制：开始/暂停/继续/停止、后台按运行租约发键（含橡皮擦模式）、进度（百分比、段数、用时）、
/// 绘制中在预览上标出已画部分与光标、错误描述。
/// </summary>
public partial class DrawingBoardViewModel
{
    /// <summary>进度刷新间隔：每格移动只有几十毫秒，逐次刷新会塞满 UI 队列。</summary>
    private const int ProgressIntervalMs = 200;

    /// <summary>
    /// 最近一次绘制实测的各类操作额外耗时，用于下一次的预计耗时与起步阶段的剩余时间。
    /// 本次运行期间有效（同一台设备、同一种连接），重启程序后从 0 重新学习。测试会在每个用例前清零。
    /// </summary>
    internal static DrawingOverhead LearnedOverhead { get; set; }

    private CancellationTokenSource? _drawCancellation;

    /// <summary>
    /// 橡皮擦模式：用户事先把整个画布涂黑，程序按住 B（橡皮擦）擦出应留白的点，而不是按住 A（画笔）画出墨点。
    /// 最终效果相同；黑多白少的图要处理的点更少、更快。预览仍显示最终效果。
    /// </summary>
    [ObservableProperty]
    private bool _eraserMode;

    /// <summary>
    /// 开画前自动归位：按住左上把光标推到画布左上角。默认开启；关闭时弹出风险提示
    /// （光标不在左上角会整体错位；Switch 可能忽略第一帧输入，第一下落笔丢失会让第一行画不出来）。
    /// </summary>
    [ObservableProperty]
    private bool _autoHome = true;

    /// <summary>绘制时保持唤醒：阻止系统休眠、熄屏与屏幕保护（macOS 进入屏幕保护后计时明显变慢）。默认开启。</summary>
    [ObservableProperty]
    private bool _stayAwake = true;

    [ObservableProperty]
    private bool _isStartEnabled = true;

    /// <summary>后台绘制进行中（含暂停）；控制暂停按钮可用，并禁止修改框选区域。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetAllCommand))]
    private bool _isDrawing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private bool _isPaused;

    private DrawPause? _pause;

    /// <summary>实际用时；暂停期间停表，不计入用时与剩余时间校正。</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>绘制进度 0～100。</summary>
    [ObservableProperty]
    private double _progressValue;

    private string? _progressKey;
    private object[] _progressArgs = [];

    // 以下由绘制线程写入，节流后渲染成预览叠加图
    private InkMask? _runStrokes;
    private (int StartCol, int StartRow, int ColCount, int RowCount) _runRegion;
    private bool _runEraser;
    private bool[]? _runDone;
    private (int Row, int Col) _runCursor;
    private bool _runPenDown;
    private int _segmentsDone;
    private int _segmentsTotal;
    private DrawingCost _costDone;
    private DrawingCost _costTotal;
    private DrawingOverhead _runOverhead;

    /// <summary>最近一次开始画图的后台任务（测试用于等待绘制结束）。</summary>
    internal Task? DrawTask { get; private set; }

    partial void OnEraserModeChanged(bool value) => UpdateEstimate();

    partial void OnAutoHomeChanged(bool value)
    {
        UpdateEstimate();
        if (!value)
            _ = ShowMessageAsync(T("Text.DrawingBoard.AutoHomeOffWarning"));
    }

    /// <summary>实际要走笔的点：画笔模式是墨点，橡皮擦模式是要擦成空白的点。</summary>
    private InkMask StrokeMask(InkMask mask) => EraserMode ? mask.Inverted() : mask;

    private GamePadKey StrokeKey => EraserMode ? GamePadKey.B : GamePadKey.A;

    public string ProgressText => _progressKey == null ? "" : Format(_progressKey, _progressArgs);

    public string PauseButtonText => T(IsPaused ? "Text.DrawingBoard.Continue" : "Text.DrawingBoard.Pause");

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
        bool home = AutoHome;
        bool stayAwake = StayAwake;
        List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(strokes, settings);
        _runStrokes = strokes;
        _runRegion = (settings.StartCol, settings.StartRow, settings.ColCount, settings.RowCount);
        _runEraser = EraserMode;
        _runDone = new bool[strokes.Width * strokes.Height];
        _runCursor = (0, 0);
        _runPenDown = false;
        _segmentsDone = 0;
        _segmentsTotal = rows.Sum(row => row.Count);
        _costDone = default;
        _costTotal = SplatoonDrawing.EstimateCost(rows, settings.Wait, settings.Duration, home);
        _runOverhead = default;
        _pause = new DrawPause();
        _clock.Reset();
        IsPaused = false;
        IsDrawing = true;
        IsStartEnabled = false;
        SetProgress(0, "Text.DrawingBoard.Preparing");
        DrawTask = Task.Run(() => RunDraw(rows, key, home, stayAwake, settings, _pause, token));
    }

    private void RunDraw(List<List<DrawingLine>> rows, GamePadKey key, bool home, bool stayAwake, DrawingBoardSettings settings, DrawPause pause, CancellationToken token)
    {
        try
        {
            _runner.Run((pad, runToken) =>
            {
                // 取得手柄后才开始计时（暂停时停表）
                if (!pause.IsPaused)
                    _clock.Start();
                long lastReport = -ProgressIntervalMs;
                PostProgress();
                SplatoonDrawing drawing = new(pad, settings.Wait, settings.Duration, progress: (done, overhead) =>
                {
                    _costDone = done;
                    _runOverhead = overhead;
                    long now = _clock.ElapsedMilliseconds;
                    if (now - lastReport < ProgressIntervalMs)
                        return;
                    lastReport = now;
                    PostProgress();
                }, penKey: key, pause: pause, step: OnStep, home: home);
                drawing.Draw(rows, runToken);
            }, stayAwake, token);
            _clock.Stop();
            LearnOverhead();
            string took = FormatClock(_clock.Elapsed);
            PixelImage final = RenderProgressImage();
            _ui.Post(() =>
            {
                ShowProgressImage(final);
                SetProgress(100, "Text.DrawingBoard.Finished", took);
                EndDrawing();
            });
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // 用户停止或关闭窗口：按键已由运行租约释放，开始按钮由 StopAsync 恢复
            _clock.Stop();
            LearnOverhead();
            double percent = Percent(_costDone.PlannedMs, _costTotal.PlannedMs);
            string reached = FormatPercent(_costDone.PlannedMs, _costTotal.PlannedMs);
            PixelImage final = RenderProgressImage();
            _ui.Post(() =>
            {
                ShowProgressImage(final);
                SetProgress(percent, "Text.DrawingBoard.Stopped", reached);
            });
        }
        catch (Exception ex)
        {
            // 旧版任务异常被静默吞掉，开始按钮一直禁用到点击停止，用户无从得知原因
            _clock.Stop();
            double percent = Percent(_costDone.PlannedMs, _costTotal.PlannedMs);
            string reached = FormatPercent(_costDone.PlannedMs, _costTotal.PlannedMs);
            PixelImage final = RenderProgressImage();
            // 本地化文本只能在 UI 线程上取
            _ui.Post(() =>
            {
                string message = Format("Text.DrawingBoard.Error", DescribeError(ex));
                _logService.AddLog(message);
                ShowProgressImage(final);
                SetProgress(percent, "Text.DrawingBoard.Interrupted", reached);
                EndDrawing();
                _ = ShowMessageAsync(message);
            });
        }
    }

    /// <summary>绘制线程：记录光标；画笔按下时走过的点即已画（橡皮擦模式即已擦）；抬笔即完成一段。</summary>
    private void OnStep(int row, int col, bool penDown)
    {
        _runCursor = (row, col);
        if (_runPenDown && !penDown)
            _segmentsDone++;
        _runPenDown = penDown;
        if (!penDown)
            return;
        bool[] done = _runDone!;
        int i = row * SplatoonDrawing.CanvasWidth + col;
        if (!done[i] && _runStrokes![col, row])
            done[i] = true;
    }

    /// <summary>暂停/继续。暂停在下一次按键前生效；若正按着画笔会先松开，继续时原位重新按下。</summary>
    [RelayCommand(CanExecute = nameof(IsDrawing))]
    private void TogglePause()
    {
        if (_pause == null)
            return;
        if (IsPaused)
        {
            IsPaused = false;
            _clock.Start();
            _pause.Resume();
            PostProgress();
        }
        else
        {
            _pause.Pause();
            _clock.Stop();
            IsPaused = true;
            // 节流期间的最新进度可能还没刷到预览上，暂停时立即刷新一次
            ShowProgressImage(RenderProgressImage());
            double percent = Percent(_costDone.PlannedMs, _costTotal.PlannedMs);
            SetProgress(percent, "Text.DrawingBoard.Paused", FormatPercent(_costDone.PlannedMs, _costTotal.PlannedMs),
                _segmentsDone, _segmentsTotal, FormatClock(_clock.Elapsed));
        }
    }

    private void EndDrawing()
    {
        IsStartEnabled = true;
        IsDrawing = false;
        IsPaused = false;
    }

    /// <summary>在绘制线程上取快照并投递到界面：进度文字与预览叠加图。</summary>
    private void PostProgress()
    {
        DrawingCost done = _costDone;
        DrawingCost total = _costTotal;
        TimeSpan elapsed = _clock.Elapsed;
        double percent = Percent(done.PlannedMs, total.PlannedMs);
        object[] args = [FormatPercent(done.PlannedMs, total.PlannedMs), _segmentsDone, _segmentsTotal, FormatClock(elapsed),
            FormatClock(EstimateRemaining(done, total, _runOverhead, LearnedOverhead))];
        PixelImage image = RenderProgressImage();
        _ui.Post(() =>
        {
            ShowProgressImage(image);
            // 暂停后投递过来的旧进度不覆盖"已暂停"
            if (!IsPaused)
                SetProgress(percent, "Text.DrawingBoard.Progress", args);
        });
    }

    private PixelImage RenderProgressImage() => PreviewOverlay.RenderProgress(_runStrokes!, _runDone!, _runEraser, _runCursor, _runRegion);

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
    /// 剩余时间：剩下各类操作的次数 × 本次实测的额外耗时，加上剩余计划耗时；某类操作样本不足时用上次学到的值。
    /// 不按"实际用时 / 计划用时"整体比例外推：图案疏密不均时（如抖动区与空白区），前后两段的落笔比例差别很大。
    /// </summary>
    internal static TimeSpan EstimateRemaining(DrawingCost done, DrawingCost total, DrawingOverhead measured, DrawingOverhead learned)
        => measured.Or(learned, done).Apply(total - done);

    /// <summary>绘制线程：把本次实测并入学到的额外耗时；日志与预计耗时在 UI 线程上更新（本地化文本只能在 UI 线程上取）。</summary>
    private void LearnOverhead()
    {
        DrawingOverhead learned = _runOverhead.Or(LearnedOverhead, _costDone);
        if (learned == LearnedOverhead)
            return;
        LearnedOverhead = learned;
        _ui.Post(() =>
        {
            _logService.AddLog(Format("Text.DrawingBoard.OverheadLog",
                learned.ClickMs.ToString("0.0"), learned.ToggleMs.ToString("0.0"), learned.WaitMs.ToString("0.0")));
            UpdateEstimate();
        });
    }

    internal static string FormatClock(TimeSpan time)
        => time.TotalHours >= 1 ? (int)time.TotalHours + ":" + time.ToString(@"mm\:ss") : time.ToString(@"mm\:ss");

    [RelayCommand]
    private async Task StopAsync()
    {
        // 取消同样会打断暂停中的等待
        _drawCancellation?.Cancel();
        // 等后台绘制真正退出（运行租约此时已释放按键）再恢复开始按钮；旧版固定等 1 秒，期间绘制可能仍在发键
        if (DrawTask != null)
            await DrawTask;
        EndDrawing();
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