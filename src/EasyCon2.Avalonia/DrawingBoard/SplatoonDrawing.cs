using EasyCon2.Avalonia.Models;
using EasyDevice;
using System.Drawing;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>画板用到的设备操作（旧版直接调用 NintendoSwitch.IsConnected/Down/Up/Reset）。</summary>
public interface IDrawingPad
{
    bool IsConnected();
    void Down(ECKey key);
    void Up(ECKey key);
    void Reset();
}

/// <summary>画板参数，对应旧版窗体上的各输入框。</summary>
public sealed record DrawingBoardSettings(int Wait, int Duration, int StartRow, int StartCol, int Col, int Row, bool Reverse);

/// <summary>扫描出的一段连续墨点。沿用旧版约定：X 存行号，Y 存列号。</summary>
public class DrawingLine
{
    public Point start;
    public Point end;
}

/// <summary>
/// 喷射战士画板的扫描/估算/绘制算法，移植自 1.6.3 WinForms 版 EasyCon2/Forms/DrawingBoard.cs。
/// 扫描顺序与移动路径沿用旧版；旧版的缺陷按条修复，与旧版的差异见各方法注释。
/// </summary>
public sealed class SplatoonDrawing
{
    private readonly ECKey _a = ECKeyUtil.Button(SwitchButton.A);
    private readonly ECKey _left = ECKeyUtil.HAT(SwitchHAT.LEFT);
    private readonly ECKey _right = ECKeyUtil.HAT(SwitchHAT.RIGHT);
    private readonly ECKey _up = ECKeyUtil.HAT(SwitchHAT.TOP);
    private readonly ECKey _down = ECKeyUtil.HAT(SwitchHAT.BOTTOM);
    private readonly IDrawingPad _ns;
    private readonly int _duration;
    private readonly int _wait;
    private Point _pos;

    public SplatoonDrawing(IDrawingPad pad, int wait, int duration)
    {
        _ns = pad;
        _wait = wait;
        _duration = duration;
    }

    /// <summary>按行扫描图片，得到每行的墨点线段（旧版开始画图与估算耗时共用的同一段代码）。</summary>
    public static List<List<DrawingLine>> SearchLines(PixelImage pic, DrawingBoardSettings settings)
    {
        List<List<DrawingLine>> rows = new();
        int start_row = settings.StartRow;
        int start_col = settings.StartCol;
        int col = settings.Col;
        int row = settings.Row;
        bool reverse = settings.Reverse;

        if (start_row + row > 120)
        {
            row = 120 - start_row;
        }

        if (start_col + col > 320)
        {
            col = 320 - start_col;
        }

        // search line
        for (int i = start_row; i < start_row + row; i++)
        {
            bool line_find = false;
            List<DrawingLine> Lines = new List<DrawingLine>();
            for (int j = start_col; j < start_col + col; j++)
            {
                Color color = pic.GetPixel(j, i);
                if (reverse)
                {
                    if (color.R + color.G + color.B > 255 * 3 / 2 && line_find == false)
                    {
                        DrawingLine line = new DrawingLine();
                        line.start.X = i;
                        line.start.Y = j;
                        line.end.X = i;
                        Lines.Add(line);
                        line_find = true;
                    }
                    else if (color.R + color.G + color.B < 255 * 3 / 2 && line_find == true)
                    {
                        Lines.Last().end.Y = j - 1;
                        line_find = false;
                    }
                }
                else
                {
                    if (color.R + color.G + color.B < 255 * 3 / 2 && line_find == false)
                    {
                        DrawingLine line = new DrawingLine();
                        line.start.X = i;
                        line.start.Y = j;
                        line.end.X = i;
                        Lines.Add(line);
                        line_find = true;
                    }
                    else if (color.R + color.G + color.B > 255 * 3 / 2 && line_find == true)
                    {
                        Lines.Last().end.Y = j - 1;
                        line_find = false;
                    }
                }
            }

            // 扫到行尾时线段仍未结束：终点补为区域最后一列
            // （旧版用 end.Y == 0 判断，会把第 0 列的单个墨点误画成整行）
            if (line_find)
                Lines.Last().end.Y = start_col + col - 1;

            if (Lines.Count > 0)
                rows.Add(Lines);
        }
        return rows;
    }

    /// <summary>旧版"估算耗时"按钮：只累计列方向移动的格数。</summary>
    public static int EvaluatePoints(List<List<DrawingLine>> rows)
    {
        Point Pos = new Point(0, 0);
        int point_num = 0;
        foreach (List<DrawingLine> Lines in rows)
        {
            // search nearest line
            if (Math.Abs(Pos.Y - Lines[0].start.Y) > Math.Abs(Pos.Y - Lines.Last().end.Y))
                Lines.Reverse();

            // draw line
            foreach (DrawingLine line in Lines)
            {
                if (Math.Abs(Pos.Y - line.start.Y) < Math.Abs(Pos.Y - line.end.Y))
                {
                    point_num += Math.Abs(Pos.Y - line.start.Y);
                    Pos.Y = line.start.Y;
                    Pos.X = line.start.X;
                }
                else
                {
                    point_num += Math.Abs(Pos.Y - line.end.Y);
                    Pos.Y = line.end.Y;
                    Pos.X = line.end.X;
                }

                if (Math.Abs(Pos.Y - line.start.Y) < Math.Abs(Pos.Y - line.end.Y))
                {
                    point_num += Math.Abs(Pos.Y - line.end.Y);
                    Pos.Y = line.end.Y;
                    Pos.X = line.end.X;
                }
                else
                {
                    point_num += Math.Abs(Pos.Y - line.start.Y);
                    Pos.Y = line.start.Y;
                    Pos.X = line.start.X;
                }
            }
        }
        return point_num;
    }

    public static string FormatEvaluation(int pointNum, int wait, int duration)
    {
        return "耗时：" + (pointNum * (wait + duration) / 1000.0 / 60.0).ToString("0.00") + "mins";
    }

    /// <summary>
    /// 旧版 draw_task_opt 的绘制循环。被取消时返回 false。
    /// 与旧版不同：每次按键等待都响应取消（旧版只在两段线之间检查），取消后立即 Reset 松开全部按键。
    /// </summary>
    public bool Draw(List<List<DrawingLine>> rows, CancellationToken token)
    {
        _pos = new Point(0, 0);
        try
        {
            foreach (List<DrawingLine> Lines in rows)
            {
                // search nearest line
                if (Math.Abs(_pos.Y - Lines[0].start.Y) > Math.Abs(_pos.Y - Lines.Last().end.Y))
                    Lines.Reverse();

                // draw line
                foreach (DrawingLine line in Lines)
                {
                    token.ThrowIfCancellationRequested();
                    if (Math.Abs(_pos.Y - line.start.Y) < Math.Abs(_pos.Y - line.end.Y))
                        MoveTo(line.start.X, line.start.Y, token);
                    else
                        MoveTo(line.start.X, line.end.Y, token);
                    _ns.Down(_a);
                    Sleep(_wait, token);
                    if (Math.Abs(_pos.Y - line.start.Y) < Math.Abs(_pos.Y - line.end.Y))
                        MoveTo(line.end.X, line.end.Y, token);
                    else
                        MoveTo(line.end.X, line.start.Y, token);
                    _ns.Reset();
                    Sleep(_wait, token);
                }
            }
            _ns.Reset();
            Sleep(_wait, token);
            _ns.Reset();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 取消可能发生在 A 或方向键按住期间
            _ns.Reset();
            return false;
        }
    }

    private static void Sleep(int milliseconds, CancellationToken token)
    {
        if (token.WaitHandle.WaitOne(Math.Max(0, milliseconds)))
            throw new OperationCanceledException(token);
    }

    private void MoveTo(int x, int y, CancellationToken token)
    {
        int x_start = _pos.X;
        int x_end = x;
        int y_start = _pos.Y;
        int y_end = y;
        // move x
        if (_pos.X > x)
        {
            x_start = x;
            x_end = _pos.X;
        }

        for (int i = x_start; i < x_end; i++)
        {
            if (_pos.X < x)
            {
                _ns.Down(_down);
                Sleep(_duration, token);
                _ns.Up(_down);
            }
            else
            {
                _ns.Down(_up);
                Sleep(_duration, token);
                _ns.Up(_up);
            }
            Sleep(_wait, token);
        }
        Sleep(_wait, token);

        if (_pos.Y > y)
        {
            y_start = y;
            y_end = _pos.Y;
        }

        for (int i = y_start; i < y_end; i++)
        {
            if (_pos.Y < y)
            {
                _ns.Down(_right);
                Sleep(_duration, token);
                _ns.Up(_right);
            }
            else
            {
                _ns.Down(_left);
                Sleep(_duration, token);
                _ns.Up(_left);
            }
            Sleep(_wait, token);
        }
        Sleep(_wait, token);

        _pos.X = x;
        _pos.Y = y;
    }
}