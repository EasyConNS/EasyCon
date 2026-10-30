using EasyCon.Core.Capabilities;
using EasyScript;
using System.Diagnostics;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>画板参数：按键时序与绘制区域（默认整张画布）。</summary>
public sealed record DrawingBoardSettings(int Wait, int Duration, int StartRow, int StartCol, int ColCount, int RowCount);

/// <summary>扫描出的一段连续墨点：第 <paramref name="Row"/> 行的 <paramref name="StartCol"/>～<paramref name="EndCol"/> 列（含两端）。</summary>
public readonly record struct DrawingLine(int Row, int StartCol, int EndCol);

/// <summary>
/// 一次绘制（或其中已完成部分）的计划耗时与各类操作次数：<paramref name="Clicks"/> 次点按（含归位）、
/// <paramref name="Toggles"/> 次落笔/抬笔、<paramref name="Waits"/> 次延迟。
/// </summary>
public readonly record struct DrawingCost(long PlannedMs, int Clicks, int Toggles, int Waits)
{
    public static DrawingCost operator -(DrawingCost a, DrawingCost b)
        => new(a.PlannedMs - b.PlannedMs, a.Clicks - b.Clicks, a.Toggles - b.Toggles, a.Waits - b.Waits);
}

/// <summary>
/// 每类操作在计划之外平均多花的毫秒数：设备通信往返、计时误差、进度刷新等。
/// 计划耗时只是按键持续与延迟之和，线段越多（落笔/抬笔越多）实际比计划慢得越明显，所以按操作类别分别计。
/// </summary>
public readonly record struct DrawingOverhead(double ClickMs, double ToggleMs, double WaitMs)
{
    /// <summary>某类操作的实测样本少于此数时，该项沿用先验值。</summary>
    public const int MinSamples = 20;

    public TimeSpan Apply(DrawingCost cost)
        => TimeSpan.FromMilliseconds(Math.Max(0, cost.PlannedMs + cost.Clicks * ClickMs + cost.Toggles * ToggleMs + cost.Waits * WaitMs));

    /// <summary>以本次实测为准；样本（<paramref name="samples"/> 中该类次数）不足的项取 <paramref name="prior"/>。</summary>
    public DrawingOverhead Or(DrawingOverhead prior, DrawingCost samples) => new(
        samples.Clicks >= MinSamples ? ClickMs : prior.ClickMs,
        samples.Toggles >= MinSamples ? ToggleMs : prior.ToggleMs,
        samples.Waits >= MinSamples ? WaitMs : prior.WaitMs);
}

/// <summary>
/// 喷射战士画板的扫描/估算/绘制算法，移植自 1.6.3 WinForms 版 EasyCon2/Forms/DrawingBoard.cs。
/// 扫描顺序与移动路径沿用旧版；旧版的缺陷按条修复，与旧版的差异见各方法注释。
/// </summary>
public sealed class SplatoonDrawing
{
    public const int CanvasWidth = 320;
    public const int CanvasHeight = 120;

    /// <summary>归位第一下：左摇杆向左上推一下。Switch 可能丢掉连接后的第一帧输入，丢的是这一下也无妨。</summary>
    public const int HomeTapMs = 50;

    /// <summary>归位第二下：左摇杆向左上推住的时长，足以把光标从画布任意位置推到左上角。</summary>
    public const int HomeHoldMs = 4000;

    /// <summary>左摇杆推到左上的最大角度：X、Y 都取最小值（0 为最左、最上，128 为中位）。</summary>
    private const byte StickUpLeft = 0;

    private readonly IPadInput _pad;
    private readonly int _duration;
    private readonly int _wait;
    private readonly Action<int, CancellationToken> _sleep;
    private readonly Action<DrawingCost, DrawingOverhead>? _progress;
    private readonly GamePadKey _penKey;
    private readonly DrawPause? _pause;
    private readonly Action<int, int, bool>? _step;
    private readonly bool _home;
    private int _row;
    private int _col;
    private bool _penDown;
    private DrawingCost _done;
    private double _clickExcess;
    private double _toggleExcess;
    private double _waitExcess;
    private long _lastTick;

    /// <param name="pad">运行租约下的手柄输入（与脚本运行同一条独占通道），见 <see cref="IDrawingRunner"/>。</param>
    /// <param name="sleep">等待实现；默认为可取消的真实等待，估算耗时时替换为累加器。</param>
    /// <param name="progress">
    /// 每次点按、落笔/抬笔或等待完成后回调已完成部分，与 <see cref="EstimateCost"/> 同一口径，画完时恰好等于估算值；
    /// 同时给出到目前为止实测的 <see cref="Overhead"/>。在绘制线程上调用。
    /// </param>
    /// <param name="penKey">按住它走过的格子被处理：A 为画笔，B 为橡皮擦（见画板的橡皮擦模式）。</param>
    /// <param name="pause">暂停开关；每次按键前检查，暂停时若正按着画笔会先松开，继续时原位重新按下。</param>
    /// <param name="home">开画前是否先归位（见 <see cref="Draw"/>）；关闭时假定光标已在左上角。</param>
    /// <param name="step">光标每走一格、每次落笔/抬笔后回调（行, 列, 画笔是否按下），用于在预览上标出已画部分与光标；在绘制线程上调用。</param>
    public SplatoonDrawing(IPadInput pad, int wait, int duration, Action<int, CancellationToken>? sleep = null, Action<DrawingCost, DrawingOverhead>? progress = null,
        GamePadKey penKey = GamePadKey.A, DrawPause? pause = null, Action<int, int, bool>? step = null, bool home = true)
    {
        _pad = pad;
        _wait = wait;
        _duration = duration;
        _sleep = sleep ?? Sleep;
        _progress = progress;
        _penKey = penKey;
        _pause = pause;
        _step = step;
        _home = home;
    }

    /// <summary>
    /// 按行扫描掩码的绘制区域（超出画布的部分截掉），得到每行的墨点线段；没有墨点的行不出现在结果中。
    /// </summary>
    public static List<List<DrawingLine>> SearchLines(InkMask mask, DrawingBoardSettings settings)
    {
        int endRow = settings.StartRow + Math.Min(settings.RowCount, CanvasHeight - settings.StartRow);
        int endCol = settings.StartCol + Math.Min(settings.ColCount, CanvasWidth - settings.StartCol);
        List<List<DrawingLine>> rows = [];
        for (int row = settings.StartRow; row < endRow; row++)
        {
            List<DrawingLine> lines = [];
            int? runStart = null;
            for (int col = settings.StartCol; col < endCol; col++)
            {
                bool ink = mask[col, row];
                if (runStart == null && ink)
                {
                    runStart = col;
                }
                else if (runStart != null && !ink)
                {
                    lines.Add(new DrawingLine(row, runStart.Value, col - 1));
                    runStart = null;
                }
            }

            // 扫到行尾时线段仍未结束：终点为区域最后一列
            // （旧版用 end.Y == 0 判断，会把第 0 列的单个墨点误画成整行）
            if (runStart != null)
                lines.Add(new DrawingLine(row, runStart.Value, endCol - 1));

            if (lines.Count > 0)
                rows.Add(lines);
        }
        return rows;
    }

    /// <summary>
    /// 估算：以与 <see cref="Draw"/> 完全相同的发键序列空跑一遍，取进度回调的最终值（每一次等待与按键持续之和，及各类操作次数）。
    /// 旧版只累计列方向移动的格数再乘以 (延迟 + 按键持续)，漏算了行方向移动以及每次移动、落笔、抬笔后的等待。
    /// </summary>
    public static DrawingCost EstimateCost(IReadOnlyList<IReadOnlyList<DrawingLine>> rows, int wait, int duration, bool home = true)
    {
        DrawingCost total = default;
        SplatoonDrawing dryRun = new(NullPad.Instance, wait, duration, (_, _) => { }, (done, _) => total = done, home: home);
        dryRun.Draw(rows, CancellationToken.None);
        return total;
    }

    /// <summary>计划耗时（不含额外开销），即 <see cref="EstimateCost"/> 的 <see cref="DrawingCost.PlannedMs"/>。</summary>
    public static TimeSpan Estimate(IReadOnlyList<IReadOnlyList<DrawingLine>> rows, int wait, int duration, bool home = true)
        => TimeSpan.FromMilliseconds(EstimateCost(rows, wait, duration, home).PlannedMs);

    /// <summary>
    /// 到目前为止每类操作的实测额外耗时（平均值，不小于 0）。从上一次操作结束量到本次操作结束，
    /// 所以操作之间的程序开销也算在内；暂停的时间不算。
    /// </summary>
    public DrawingOverhead Overhead => new(
        Average(_clickExcess, _done.Clicks), Average(_toggleExcess, _done.Toggles), Average(_waitExcess, _done.Waits));

    private static double Average(double excess, int count) => count > 0 ? Math.Max(0, excess / count) : 0;

    /// <summary>
    /// 先归位（左摇杆向左上推 <see cref="HomeTapMs"/>，再推住 <see cref="HomeHoldMs"/> 把光标推到左上角；可关闭），再逐行绘制。
    /// 被取消时抛出 <see cref="OperationCanceledException"/>。
    /// 归位的第一下兼作"垫帧"：Switch 可能吞掉连接后的第一帧输入，若第一下就是落笔，整个第 0 行会画不出来。
    /// 与旧版不同：每次按键与等待都响应取消（旧版只在两段线之间检查）；
    /// 取消、出错或画完后的按键释放由运行租约统一写出中性报告，这里不再 Reset。
    /// 不修改 <paramref name="rows"/>（旧版就地 Reverse 各行），同一份扫描结果可先估算再绘制、或重复绘制。
    /// </summary>
    public void Draw(IReadOnlyList<IReadOnlyList<DrawingLine>> rows, CancellationToken token)
    {
        _row = 0;
        _col = 0;
        _penDown = false;
        _done = default;
        _clickExcess = _toggleExcess = _waitExcess = 0;
        _lastTick = Stopwatch.GetTimestamp();
        // 没有要画的点就不碰手柄（也不归位）
        if (rows.Count == 0)
            return;
        token.ThrowIfCancellationRequested();
        if (_home)
            Home(token);
        foreach (IReadOnlyList<DrawingLine> lines in rows)
        {
            // 从离光标近的一端开始画这一行
            IEnumerable<DrawingLine> ordered = Math.Abs(_col - lines[0].StartCol) > Math.Abs(_col - lines[^1].EndCol)
                ? lines.Reverse()
                : lines;

            foreach (DrawingLine line in ordered)
            {
                token.ThrowIfCancellationRequested();
                // 在线段离光标近的一端落笔，按住 A 走到另一端
                MoveTo(line.Row, NearerStart(line) ? line.StartCol : line.EndCol, token);
                CheckPause(token);
                _pad.PressButtons(_penKey);
                _penDown = true;
                Advance(Operation.Toggle, 0);
                _step?.Invoke(_row, _col, true);
                Wait(_wait, token);
                MoveTo(line.Row, NearerStart(line) ? line.EndCol : line.StartCol, token);
                _pad.ReleaseButtons(_penKey);
                _penDown = false;
                Advance(Operation.Toggle, 0);
                _step?.Invoke(_row, _col, false);
                Wait(_wait, token);
            }
        }
    }

    /// <summary>
    /// 左摇杆以最大角度向左上推一下（可能被丢掉），再推住直到光标停在角上，每次回中后等一次延迟。光标从哪里出发都会归到 (0, 0)。
    /// </summary>
    private void Home(CancellationToken token)
    {
        foreach (int milliseconds in (ReadOnlySpan<int>)[HomeTapMs, HomeHoldMs])
        {
            CheckPause(token);
            _pad.ClickStick(GamePadKey.LS, StickUpLeft, StickUpLeft, milliseconds, token);
            Advance(Operation.Click, milliseconds);
            Wait(_wait, token);
        }
        _step?.Invoke(_row, _col, false);
    }

    /// <summary>光标是否严格更靠近线段起点（等距时视为靠近终点，沿用旧版）。</summary>
    private bool NearerStart(DrawingLine line) => Math.Abs(_col - line.StartCol) < Math.Abs(_col - line.EndCol);

    private void Wait(int milliseconds, CancellationToken token)
    {
        _sleep(milliseconds, token);
        Advance(Operation.Wait, milliseconds);
    }

    private void Click(GamePadKey key, CancellationToken token)
    {
        CheckPause(token);
        _pad.ClickButtons(key, _duration, token);
        Advance(Operation.Click, _duration);
    }

    /// <summary>
    /// 暂停时阻塞到继续。若正按着画笔，暂停前先松开、继续后在原位重新按下并等一次延迟（同一格重画无影响）；
    /// 这些额外动作不计入计划耗时，进度仍按原计划走到 100%。
    /// </summary>
    private void CheckPause(CancellationToken token)
    {
        if (_pause is not { IsPaused: true })
            return;
        bool lift = _penDown;
        if (lift)
            _pad.ReleaseButtons(_penKey);
        _pause.WaitWhilePaused(token);
        if (lift)
        {
            _pad.PressButtons(_penKey);
            _sleep(_wait, token);
        }
        // 暂停及恢复时的重新落笔不计入下一次操作的额外耗时
        _lastTick = Stopwatch.GetTimestamp();
    }

    private enum Operation { Click, Toggle, Wait }

    /// <summary>记一次操作：计划耗时、次数，以及实际用时超出计划的部分。</summary>
    private void Advance(Operation operation, int milliseconds)
    {
        milliseconds = Math.Max(0, milliseconds);
        long now = Stopwatch.GetTimestamp();
        double excess = Stopwatch.GetElapsedTime(_lastTick, now).TotalMilliseconds - milliseconds;
        _lastTick = now;
        _done = _done with { PlannedMs = _done.PlannedMs + milliseconds };
        switch (operation)
        {
            case Operation.Click:
                _done = _done with { Clicks = _done.Clicks + 1 };
                _clickExcess += excess;
                break;
            case Operation.Toggle:
                _done = _done with { Toggles = _done.Toggles + 1 };
                _toggleExcess += excess;
                break;
            default:
                _done = _done with { Waits = _done.Waits + 1 };
                _waitExcess += excess;
                break;
        }
        _progress?.Invoke(_done, Overhead);
    }

    private static void Sleep(int milliseconds, CancellationToken token)
    {
        if (token.WaitHandle.WaitOne(Math.Max(0, milliseconds)))
            throw new OperationCanceledException(token);
    }

    /// <summary>先上下移到目标行，再左右移到目标列；每格一次点按，每个方向走完后多等一次。光标位置逐格更新并回调。</summary>
    private void MoveTo(int row, int col, CancellationToken token)
    {
        GamePadKey vertical = row > _row ? GamePadKey.DOWN : GamePadKey.TOP;
        int rowStep = Math.Sign(row - _row);
        for (int i = Math.Abs(row - _row); i > 0; i--)
        {
            Click(vertical, token);
            _row += rowStep;
            _step?.Invoke(_row, _col, _penDown);
            Wait(_wait, token);
        }
        Wait(_wait, token);

        GamePadKey horizontal = col > _col ? GamePadKey.RIGHT : GamePadKey.LEFT;
        int colStep = Math.Sign(col - _col);
        for (int i = Math.Abs(col - _col); i > 0; i--)
        {
            Click(horizontal, token);
            _col += colStep;
            _step?.Invoke(_row, _col, _penDown);
            Wait(_wait, token);
        }
        Wait(_wait, token);
    }

    /// <summary>估算用的空跑手柄：不发任何键。</summary>
    private sealed class NullPad : IPadInput
    {
        public static readonly NullPad Instance = new();
        public void ClickButtons(GamePadKey key, int duration, CancellationToken token) { }
        public void PressButtons(GamePadKey key) { }
        public void ReleaseButtons(GamePadKey key) { }
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }
        public void SetStick(GamePadKey key, byte x, byte y) { }
        public void ChangeAmiibo(uint index) { }
    }
}