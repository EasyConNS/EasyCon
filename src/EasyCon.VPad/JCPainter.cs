using EasyDevice;
using System.Drawing.Drawing2D;

namespace EasyVPad;

public delegate Bitmap GetJCImg(string name);
public delegate SwitchReport GetReport();

internal class JCPainter
{
    DateTime _starttime = DateTime.MinValue;
    readonly Brush _brushLightDefault = new SolidBrush(Color.FromArgb(50, Color.Black));
    readonly Brush _brushStickBG = new SolidBrush(Color.FromArgb(50, 50, 50));
    readonly Brush _brushStickBGDown = new SolidBrush(Color.Lime);
    readonly Brush _brushStickAreaBG = new SolidBrush(Color.FromArgb(50, Color.Black));
    readonly Brush _brushStickAreaBGDown = new SolidBrush(Color.FromArgb(200, Color.Lime));
    readonly Brush _brushButtonDown = new SolidBrush(Color.Lime);
    readonly Brush _brushButtonUp = new SolidBrush(Color.FromArgb(50, 50, 50));

    readonly GetReport getReporter;
    readonly IControllerAdapter script;
    readonly GetJCImg getJCImg;

    public JCPainter(IReporter repoter, IControllerAdapter adapter, GetJCImg getImg)
    {
        getReporter = repoter.GetReport;
        script = adapter;
        getJCImg = getImg;
    }

    public void OnPaint(Graphics g, Rectangle rectangle)
    {
        var report = getReporter();
        int x0, y0, w0, h0;
        int x, y, w, h;
        int p;
        Pen pen = Pens.Black;

        // background
        g.DrawImage(getJCImg("JoyCon"), rectangle);

        // script lights
        int flashIndex = -1;
        if (script.IsRunning())
        {
            if (_starttime == DateTime.MinValue)
                _starttime = DateTime.Now;
            int n = (int)(DateTime.Now - _starttime).TotalMilliseconds / 150;
            flashIndex = Math.Abs(3 - n % 6);
        }
        else
            _starttime = DateTime.MinValue;
        for (int i = 0; i < 4; i++)
        {
            x = 47;
            y = 32 + 10 * i;
            w = 5;
            h = 5;
            if (i == flashIndex)
                DrawRectangle(g, pen, new SolidBrush(script.CurrentLight), x, y, w, h);
            else if (script.IsRunning())
                DrawRectangle(g, pen, new SolidBrush(Color.FromArgb(50, script.CurrentLight)), x, y, w, h);
            else
                DrawRectangle(g, pen, _brushLightDefault, x, y, w, h);
        }

        // left stick
        x0 = 11;
        y0 = 21;
        w0 = 25;
        h0 = 25;
        p = 2;
        w = 15;
        h = 15;
        x = x0 + (w0 - w) * report.LX / SwitchStick.STICK_MAX;
        y = y0 + (h0 - h) * report.LY / SwitchStick.STICK_MAX;
        DrawEllipse(g, pen, report.LX == SwitchStick.STICK_CENTER && report.LY == SwitchStick.STICK_CENTER ? _brushStickAreaBG : _brushStickAreaBGDown, x0 + p, y0 + p, w0 - p * 2, h0 - p * 2);
        DrawEllipse(g, pen, (report.Button & (ushort)SwitchButton.LCLICK) != 0 ? _brushStickBGDown : _brushStickBG, x, y, w, h);

        // right stick
        x0 = 63;
        y0 = 52;
        w0 = 25;
        h0 = 25;
        p = 2;
        w = 15;
        h = 15;
        x = x0 + (w0 - w) * report.RX / SwitchStick.STICK_MAX;
        y = y0 + (h0 - h) * report.RY / SwitchStick.STICK_MAX;
        DrawEllipse(g, pen, report.RX == SwitchStick.STICK_CENTER && report.RY == SwitchStick.STICK_CENTER ? _brushStickAreaBG : _brushStickAreaBGDown, x0 + p, y0 + p, w0 - p * 2, h0 - p * 2);
        DrawEllipse(g, pen, (report.Button & (ushort)SwitchButton.RCLICK) != 0 ? _brushStickBGDown : _brushStickBG, x, y, w, h);

        // HAT
        var dkey = ((SwitchHAT)report.HAT).GetDirectionFromHAT();
        DrawPath(g, pen, dkey.HasFlag(DirectionKey.Up) ? _brushButtonDown : _brushButtonUp, RoundedRect(21, 55, 6, 6, 2, 2, 2, 2));
        DrawPath(g, pen, dkey.HasFlag(DirectionKey.Down) ? _brushButtonDown : _brushButtonUp, RoundedRect(21, 67, 6, 6, 2, 2, 2, 2));
        DrawPath(g, pen, dkey.HasFlag(DirectionKey.Left) ? _brushButtonDown : _brushButtonUp, RoundedRect(15, 61, 6, 6, 2, 2, 2, 2));
        DrawPath(g, pen, dkey.HasFlag(DirectionKey.Right) ? _brushButtonDown : _brushButtonUp, RoundedRect(27, 61, 6, 6, 2, 2, 2, 2));

        // buttons
        g.DrawImage(getJCImg($"JoyCon_ZL_{Math.Sign((report.Button & (ushort)SwitchButton.ZL))}"), rectangle);
        g.DrawImage(getJCImg($"JoyCon_ZR_{Math.Sign((report.Button & (ushort)SwitchButton.ZR))}"), rectangle);
        g.DrawImage(getJCImg($"JoyCon_L_{Math.Sign((report.Button & (ushort)SwitchButton.L))}"), rectangle);
        g.DrawImage(getJCImg($"JoyCon_R_{Math.Sign((report.Button & (ushort)SwitchButton.R))}"), rectangle);
        DrawEllipse(g, pen, (report.Button & (ushort)SwitchButton.A) != 0 ? _brushButtonDown : _brushButtonUp, 79, 29, 9, 9);
        DrawEllipse(g, pen, (report.Button & (ushort)SwitchButton.B) != 0 ? _brushButtonDown : _brushButtonUp, 71, 37, 9, 9);
        DrawEllipse(g, pen, (report.Button & (ushort)SwitchButton.X) != 0 ? _brushButtonDown : _brushButtonUp, 71, 21, 9, 9);
        DrawEllipse(g, pen, (report.Button & (ushort)SwitchButton.Y) != 0 ? _brushButtonDown : _brushButtonUp, 63, 29, 9, 9);
        DrawPath(g, pen, (report.Button & (ushort)SwitchButton.MINUS) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(29, 12, 5, 5, 1, 1, 1, 1));
        DrawPath(g, pen, (report.Button & (ushort)SwitchButton.PLUS) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(65, 12, 5, 5, 1, 1, 1, 1));
        DrawPath(g, pen, (report.Button & (ushort)SwitchButton.CAPTURE) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(27, 82, 5, 5, 1, 1, 1, 1));
        DrawPath(g, pen, (report.Button & (ushort)SwitchButton.HOME) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(67, 82, 5, 5, 1, 1, 1, 1));
    }

    void DrawRectangle(Graphics g, Pen pen, Brush brush, int x, int y, int w, int h)
    {
        g.FillRectangle(brush, x, y, w, h);
        g.DrawRectangle(pen, x, y, w, h);
    }

    void DrawEllipse(Graphics g, Pen pen, Brush brush, int x, int y, int w, int h)
    {
        g.FillEllipse(brush, x, y, w, h);
        g.DrawEllipse(pen, x, y, w, h);
    }

    void DrawPath(Graphics g, Pen pen, Brush brush, GraphicsPath path)
    {
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    GraphicsPath RoundedRect(int x, int y, int w, int h, int r1, int r2, int r3, int r4)
    {
        int d1 = r1 * 2, d2 = r2 * 2, d3 = r3 * 2, d4 = r4 * 2;
        var path = new GraphicsPath();

        // top left arc
        path.AddArc(new Rectangle(x, y, d1, d1), 180, 90);

        // top right arc
        path.AddArc(new Rectangle(x + w - d2, y, d2, d2), 270, 90);

        // bottom right arc
        path.AddArc(new Rectangle(x + w - d3, y + h - d3, d3, d3), 0, 90);

        // bottom left arc
        path.AddArc(new Rectangle(x, y + h - d4, d4, d4), 90, 90);

        path.CloseFigure();
        return path;
    }
}
