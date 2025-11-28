using Avalonia;
using Avalonia.Media;
using EasyDevice;

namespace EasyAvalonia.VPad;

public delegate IImage GetJCImg(string name);
public delegate SwitchReport GetReport();

internal class JCPainter(IReporter repoter, IControllerAdapter adapter, GetJCImg getImg)
{
    private DateTime _starttime = DateTime.MinValue;
    private static readonly Color _gray50 = Color.FromRgb(50, 50, 50);
    private readonly Brush _brushLightDefault = new SolidColorBrush(Colors.Black, 50 / 255);
    private readonly Brush _brushStickBG = new SolidColorBrush(_gray50);
    private readonly Brush _brushStickBGDown = new SolidColorBrush(Colors.Lime);
    private readonly Brush _brushStickAreaBG = new SolidColorBrush(Colors.Black, 50 / 255);
    private readonly Brush _brushStickAreaBGDown = new SolidColorBrush(Colors.Lime, 200 / 255);
    private readonly Brush _brushButtonDown = new SolidColorBrush(Colors.Lime);
    private readonly Brush _brushButtonUp = new SolidColorBrush(_gray50);

    private readonly GetReport _getReporter = repoter.GetReport;
    private readonly Pen _pen = new(new SolidColorBrush(Colors.Black));

    public void OnPaint(DrawingContext g, Rect rectangle)
    {
        var report = _getReporter();
        int x0,
            y0,
            w0,
            h0;
        int x,
            y,
            w,
            h;
        int p;

        // background
        g.DrawImage(getImg("JoyCon"), rectangle);

        // script lights
        int flashIndex = -1;
        if (adapter.IsRunning())
        {
            if (_starttime == DateTime.MinValue)
                _starttime = DateTime.Now;
            int n = (int)(DateTime.Now - _starttime).TotalMilliseconds / 150;
            flashIndex = Math.Abs(3 - (n % 6));
        }
        else
        {
            _starttime = DateTime.MinValue;
        }

        for (int i = 0; i < 4; i++)
        {
            x = 47;
            y = 32 + (10 * i);
            w = 5;
            h = 5;
            if (i == flashIndex)
            {
                DrawRectangle(g, _pen, new SolidColorBrush(adapter.CurrentLight), x, y, w, h);
            }
            else if (adapter.IsRunning())
            {
                DrawRectangle(
                    g,
                    _pen,
                    new SolidColorBrush(adapter.CurrentLight, 50 / 255),
                    x,
                    y,
                    w,
                    h
                );
            }
            else
            {
                DrawRectangle(g, _pen, _brushLightDefault, x, y, w, h);
            }
        }

        // left stick
        x0 = 11;
        y0 = 21;
        w0 = 25;
        h0 = 25;
        p = 2;
        w = 15;
        h = 15;
        x = x0 + ((w0 - w) * report.LX / SwitchStick.STICK_MAX);
        y = y0 + ((h0 - h) * report.LY / SwitchStick.STICK_MAX);
        DrawEllipse(
            g,
            _pen,
            report.LX == SwitchStick.STICK_CENTER && report.LY == SwitchStick.STICK_CENTER
                ? _brushStickAreaBG
                : _brushStickAreaBGDown,
            x0 + p,
            y0 + p,
            w0 - (p * 2),
            h0 - (p * 2)
        );
        DrawEllipse(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.LCLICK) != 0 ? _brushStickBGDown : _brushStickBG,
            x,
            y,
            w,
            h
        );

        // right stick
        x0 = 63;
        y0 = 52;
        w0 = 25;
        h0 = 25;
        p = 2;
        w = 15;
        h = 15;
        x = x0 + ((w0 - w) * report.RX / SwitchStick.STICK_MAX);
        y = y0 + ((h0 - h) * report.RY / SwitchStick.STICK_MAX);
        DrawEllipse(
            g,
            _pen,
            report.RX == SwitchStick.STICK_CENTER && report.RY == SwitchStick.STICK_CENTER
                ? _brushStickAreaBG
                : _brushStickAreaBGDown,
            x0 + p,
            y0 + p,
            w0 - (p * 2),
            h0 - (p * 2)
        );
        DrawEllipse(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.RCLICK) != 0 ? _brushStickBGDown : _brushStickBG,
            x,
            y,
            w,
            h
        );

        // HAT
        var dkey = ((SwitchHAT)report.HAT).GetDirectionFromHAT();
        DrawPath(
            g,
            _pen,
            dkey.HasFlag(DirectionKey.Up) ? _brushButtonDown : _brushButtonUp,
            RoundedRect(21, 55, 6, 6, 2, 2)
        );
        DrawPath(
            g,
            _pen,
            dkey.HasFlag(DirectionKey.Down) ? _brushButtonDown : _brushButtonUp,
            RoundedRect(21, 67, 6, 6, 2, 2)
        );
        DrawPath(
            g,
            _pen,
            dkey.HasFlag(DirectionKey.Left) ? _brushButtonDown : _brushButtonUp,
            RoundedRect(15, 61, 6, 6, 2, 2)
        );
        DrawPath(
            g,
            _pen,
            dkey.HasFlag(DirectionKey.Right) ? _brushButtonDown : _brushButtonUp,
            RoundedRect(27, 61, 6, 6, 2, 2)
        );

        // buttons
        g.DrawImage(
            getImg($"JoyCon_ZL_{Math.Sign(report.Button & (ushort)SwitchButton.ZL)}"),
            rectangle
        );
        g.DrawImage(
            getImg($"JoyCon_ZR_{Math.Sign(report.Button & (ushort)SwitchButton.ZR)}"),
            rectangle
        );
        g.DrawImage(
            getImg($"JoyCon_L_{Math.Sign(report.Button & (ushort)SwitchButton.L)}"),
            rectangle
        );
        g.DrawImage(
            getImg($"JoyCon_R_{Math.Sign(report.Button & (ushort)SwitchButton.R)}"),
            rectangle
        );
        DrawEllipse(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.A) != 0 ? _brushButtonDown : _brushButtonUp,
            79,
            29,
            9,
            9
        );
        DrawEllipse(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.B) != 0 ? _brushButtonDown : _brushButtonUp,
            71,
            37,
            9,
            9
        );
        DrawEllipse(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.X) != 0 ? _brushButtonDown : _brushButtonUp,
            71,
            21,
            9,
            9
        );
        DrawEllipse(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.Y) != 0 ? _brushButtonDown : _brushButtonUp,
            63,
            29,
            9,
            9
        );
        DrawPath(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.MINUS) != 0 ? _brushButtonDown : _brushButtonUp,
            RoundedRect(29, 12, 5, 5, 1, 1)
        );
        DrawPath(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.PLUS) != 0 ? _brushButtonDown : _brushButtonUp,
            RoundedRect(65, 12, 5, 5, 1, 1)
        );
        DrawPath(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.CAPTURE) != 0 ? _brushButtonDown : _brushButtonUp,
            RoundedRect(27, 82, 5, 5, 1, 1)
        );
        DrawPath(
            g,
            _pen,
            (report.Button & (ushort)SwitchButton.HOME) != 0 ? _brushButtonDown : _brushButtonUp,
            RoundedRect(67, 82, 5, 5, 1, 1)
        );
    }

    private void DrawRectangle(DrawingContext g, Pen pen, Brush brush, int x, int y, int w, int h)
    {
        g.DrawRectangle(brush, pen, new Rect(x, y, w, h));
    }

    private void DrawEllipse(DrawingContext g, Pen pen, Brush brush, int x, int y, int w, int h)
    {
        g.DrawEllipse(brush, pen, new Point(x, y), w, h);
    }

    private void DrawPath(DrawingContext g, Pen pen, Brush brush, Geometry path)
    {
        g.DrawGeometry(brush, pen, path);
    }

    private RectangleGeometry RoundedRect(int x, int y, int w, int h, int r1, int r2)
    {
        return new RectangleGeometry(new Rect(x, y, w, h), r1, r2);
    }
}
