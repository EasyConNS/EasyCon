using EasyCon.Core.Capabilities;
using EasyScript;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>画板参数：按键时序与绘制区域（默认整张画布）。</summary>
public sealed record DrawingBoardSettings(int Wait, int Duration, int StartRow, int StartCol, int ColCount, int RowCount);

/// <summary>扫描出的一段连续墨点：第 <paramref name="Row"/> 行的 <paramref name="StartCol"/>～<paramref name="EndCol"/> 列（含两端）。</summary>
public readonly record struct DrawingLine(int Row, int StartCol, int EndCol);

/// <summary>
/// 喷射战士画板的扫描/估算/绘制算法，移植自 1.6.3 WinForms 版 EasyCon2/Forms/DrawingBoard.cs。
/// 扫描顺序与移动路径沿用旧版；旧版的缺陷按条修复，与旧版的差异见各方法注释。
/// </summary>
public sealed class SplatoonDrawing
{
    public const int CanvasWidth = 320;
    public const int CanvasHeight = 120;

    private readonly IPadInput _pad;
    private readonly int _duration;
    private readonly int _wait;
    private readonly Action<int, CancellationToken> _sleep;
    private readonly Action<long>? _progress;
    private readonly GamePadKey _penKey;
    private readonly DrawPause? _pause;
    private readonly Action<int, int, bool>? _step;
    private int _row;
    private int _col;
    private bool _penDown;
    private long _done;

    /// <param name="pad">运行租约下的手柄输入（与脚本运行同一条独占通道），见 <see cref="IDrawingRunner"/>。</param>
    /// <param name="sleep">等待实现；默认为可取消的真实等待，估算耗时时替换为累加器。</param>
    /// <param name="progress">
    /// 每次点按或等待完成后回调已完成的计划耗时（毫秒），与 <see cref="Estimate"/> 同一口径，画完时恰好等于估算总耗时。
    /// 在绘制线程上调用。
    /// </param>
    /// <param name="penKey">按住它走过的格子被处理：A 为画笔，B 为橡皮擦（见画板的橡皮擦模式）。</param>
    /// <param name="pause">暂停开关；每次按键前检查，暂停时若正按着画笔会先松开，继续时原位重新按下。</param>
    /// <param name="step">光标每走一格、每次落笔/抬笔后回调（行, 列, 画笔是否按下），用于在预览上标出已画部分与光标；在绘制线程上调用。</param>
    public SplatoonDrawing(IPadInput pad, int wait, int duration, Action<int, CancellationToken>? sleep = null, Action<long>? progress = null,
        GamePadKey penKey = GamePadKey.A, DrawPause? pause = null, Action<int, int, bool>? step = null)
    {
        _pad = pad;
        _wait = wait;
        _duration = duration;
        _sleep = sleep ?? Sleep;
        _progress = progress;
        _penKey = penKey;
        _pause = pause;
        _step = step;
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
    /// 估算耗时：以与 <see cref="Draw"/> 完全相同的发键序列空跑一遍，取进度回调的最终值（每一次等待与按键持续之和）。
    /// 旧版只累计列方向移动的格数再乘以 (延迟 + 按键持续)，漏算了行方向移动以及每次移动、落笔、抬笔后的等待。
    /// </summary>
    public static TimeSpan Estimate(IReadOnlyList<IReadOnlyList<DrawingLine>> rows, int wait, int duration)
    {
        long total = 0;
        SplatoonDrawing dryRun = new(NullPad.Instance, wait, duration, (_, _) => { }, done => total = done);
        dryRun.Draw(rows, CancellationToken.None);
        return TimeSpan.FromMilliseconds(total);
    }

    /// <summary>
    /// 从画布左上角出发逐行绘制。被取消时抛出 <see cref="OperationCanceledException"/>。
    /// 与旧版不同：每次按键与等待都响应取消（旧版只在两段线之间检查）；
    /// 取消、出错或画完后的按键释放由运行租约统一写出中性报告，这里不再 Reset。
    /// 不修改 <paramref name="rows"/>（旧版就地 Reverse 各行），同一份扫描结果可先估算再绘制、或重复绘制。
    /// </summary>
    public void Draw(IReadOnlyList<IReadOnlyList<DrawingLine>> rows, CancellationToken token)
    {
        _row = 0;
        _col = 0;
        _penDown = false;
        _done = 0;
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
                _step?.Invoke(_row, _col, true);
                Wait(_wait, token);
                MoveTo(line.Row, NearerStart(line) ? line.EndCol : line.StartCol, token);
                _pad.ReleaseButtons(_penKey);
                _penDown = false;
                _step?.Invoke(_row, _col, false);
                Wait(_wait, token);
            }
        }
    }

    /// <summary>光标是否严格更靠近线段起点（等距时视为靠近终点，沿用旧版）。</summary>
    private bool NearerStart(DrawingLine line) => Math.Abs(_col - line.StartCol) < Math.Abs(_col - line.EndCol);

    private void Wait(int milliseconds, CancellationToken token)
    {
        _sleep(milliseconds, token);
        Advance(milliseconds);
    }

    private void Click(GamePadKey key, CancellationToken token)
    {
        CheckPause(token);
        _pad.ClickButtons(key, _duration, token);
        Advance(_duration);
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
    }

    private void Advance(int milliseconds)
    {
        _done += Math.Max(0, milliseconds);
        _progress?.Invoke(_done);
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