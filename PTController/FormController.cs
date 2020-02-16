using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PTDevice;

namespace PTController
{
    public partial class FormController : Form
    {
        IControllerAdapter script;

        int _controllerEnabledLevel = 0;
        public int ControllerEnabledLevel
        {
            get => _controllerEnabledLevel;
            set
            {
                _controllerEnabledLevel = value;
                Visible = _controllerEnabledLevel > 0;
            }
        }
        public bool ControllerEnabled {
            get => ControllerEnabledLevel == 2;
            set
            {
                if (value)
                    ControllerEnabledLevel = 2;
                else if (ControllerEnabledLevel == 2)
                    ControllerEnabledLevel = 1;
            }
        }

        public FormController(IControllerAdapter script)
        {
            this.script = script;
            InitializeComponent();
        }

        private void FormController_Load(object sender, EventArgs e)
        {
            var transparent = Color.FromArgb(254, 253, 252);
            BackColor = transparent;
            TransparencyKey = transparent;
            Width = 100;
            Height = 100;
            ResetLocation();
            Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        Invalidate();
                        Thread.Sleep(20);
                    }
                }
                catch
                { }
            });
        }

        public void UnregisterAllKeys()
        {
            LowLevelKeyboard.GetInstance().UnregisterKeyEventAll();
        }

        public void RegisterKey(Keys key, Action keydownAction, Action keyupAction = null)
        {
            Func<bool> keydown = () =>
            {
                if (!ControllerEnabled)
                    return false;
                keydownAction?.Invoke();
                return true;
            };
            Func<bool> keyup = () =>
            {
                if (!ControllerEnabled)
                    return false;
                keyupAction?.Invoke();
                return true;
            };
            LowLevelKeyboard.GetInstance().RegisterKeyEvent((int)key, keydown, keyup);
        }

        public void RegisterKey(Keys key, NintendoSwitch.Key nskey)
        {
            RegisterKey(key, () => NintendoSwitch.GetInstance().Down(nskey), () => NintendoSwitch.GetInstance().Up(nskey));
        }

        static class Images
        {
            static Dictionary<string, Bitmap> _dict = new Dictionary<string, Bitmap>();

            static Images()
            {
                _dict["JoyCon"] = Properties.Resources.JoyCon;
                _dict["JoyCon_L_0"] = Properties.Resources.JoyCon_L_0;
                _dict["JoyCon_L_1"] = Properties.Resources.JoyCon_L_1;
                _dict["JoyCon_R_0"] = Properties.Resources.JoyCon_R_0;
                _dict["JoyCon_R_1"] = Properties.Resources.JoyCon_R_1;
                _dict["JoyCon_ZL_0"] = Properties.Resources.JoyCon_ZL_0;
                _dict["JoyCon_ZL_1"] = Properties.Resources.JoyCon_ZL_1;
                _dict["JoyCon_ZR_0"] = Properties.Resources.JoyCon_ZR_0;
                _dict["JoyCon_ZR_1"] = Properties.Resources.JoyCon_ZR_1;
            }

            public static Bitmap Get(string name)
            {
                return _dict[name];
            }
        }

        DateTime _starttime = DateTime.MinValue;
        readonly Brush _brushLightDefault = new SolidBrush(Color.FromArgb(50, Color.Black));
        readonly Brush _brushStickBG = new SolidBrush(Color.FromArgb(50, 50, 50));
        readonly Brush _brushStickBGDown = new SolidBrush(Color.Lime);
        readonly Brush _brushStickAreaBG = new SolidBrush(Color.FromArgb(50, Color.Black));
        readonly Brush _brushStickAreaBGDown = new SolidBrush(Color.FromArgb(200, Color.Lime));
        readonly Brush _brushButtonDown = new SolidBrush(Color.Lime);
        readonly Brush _brushButtonUp = new SolidBrush(Color.FromArgb(50, 50, 50));

        private void FormController_Paint(object sender, PaintEventArgs e)
        {
            const int Padding = 5;
            var g = e.Graphics;
            var rect = new Rectangle(e.ClipRectangle.X + Padding, e.ClipRectangle.Y + Padding, e.ClipRectangle.Width - Padding * 2, e.ClipRectangle.Height - Padding * 2);
            var report = NintendoSwitch.GetInstance().GetReport();
            int x0, y0, w0, h0;
            int x, y, w, h;
            int p;
            Pen pen = Pens.Black;

            Opacity = ControllerEnabled ? 1 : 0.5;

            // background
            g.DrawImage(Images.Get("JoyCon"), e.ClipRectangle);

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
            x = x0 + (w0 - w) * report.LX / NintendoSwitch.STICK_MAX;
            y = y0 + (h0 - h) * report.LY / NintendoSwitch.STICK_MAX;
            DrawEllipse(g, pen, report.LX == NintendoSwitch.STICK_CENTER && report.LY == NintendoSwitch.STICK_CENTER ? _brushStickAreaBG : _brushStickAreaBGDown, x0 + p, y0 + p, w0 - p * 2, h0 - p * 2);
            DrawEllipse(g, pen, (report.Button & (ushort)NintendoSwitch.Button.LCLICK) != 0 ? _brushStickBGDown : _brushStickBG, x, y, w, h);

            // right stick
            x0 = 63;
            y0 = 52;
            w0 = 25;
            h0 = 25;
            p = 2;
            w = 15;
            h = 15;
            x = x0 + (w0 - w) * report.RX / NintendoSwitch.STICK_MAX;
            y = y0 + (h0 - h) * report.RY / NintendoSwitch.STICK_MAX;
            DrawEllipse(g, pen, report.RX == NintendoSwitch.STICK_CENTER && report.RY == NintendoSwitch.STICK_CENTER ? _brushStickAreaBG : _brushStickAreaBGDown, x0 + p, y0 + p, w0 - p * 2, h0 - p * 2);
            DrawEllipse(g, pen, (report.Button & (ushort)NintendoSwitch.Button.RCLICK) != 0 ? _brushStickBGDown : _brushStickBG, x, y, w, h);

            // HAT
            var dkey = NintendoSwitch.GetDirectionFromHAT((NintendoSwitch.HAT)report.HAT);
            DrawPath(g, pen, dkey.HasFlag(NintendoSwitch.DirectionKey.Up) ? _brushButtonDown : _brushButtonUp, RoundedRect(21, 55, 6, 6, 2, 2, 2, 2));
            DrawPath(g, pen, dkey.HasFlag(NintendoSwitch.DirectionKey.Down) ? _brushButtonDown : _brushButtonUp, RoundedRect(21, 67, 6, 6, 2, 2, 2, 2));
            DrawPath(g, pen, dkey.HasFlag(NintendoSwitch.DirectionKey.Left) ? _brushButtonDown : _brushButtonUp, RoundedRect(15, 61, 6, 6, 2, 2, 2, 2));
            DrawPath(g, pen, dkey.HasFlag(NintendoSwitch.DirectionKey.Right) ? _brushButtonDown : _brushButtonUp, RoundedRect(27, 61, 6, 6, 2, 2, 2, 2));

            // buttons
            g.DrawImage(Images.Get($"JoyCon_ZL_{Math.Sign((report.Button & (ushort)NintendoSwitch.Button.ZL))}"), e.ClipRectangle);
            g.DrawImage(Images.Get($"JoyCon_ZR_{Math.Sign((report.Button & (ushort)NintendoSwitch.Button.ZR))}"), e.ClipRectangle);
            g.DrawImage(Images.Get($"JoyCon_L_{Math.Sign((report.Button & (ushort)NintendoSwitch.Button.L))}"), e.ClipRectangle);
            g.DrawImage(Images.Get($"JoyCon_R_{Math.Sign((report.Button & (ushort)NintendoSwitch.Button.R))}"), e.ClipRectangle);
            DrawEllipse(g, pen, (report.Button & (ushort)NintendoSwitch.Button.A) != 0 ? _brushButtonDown : _brushButtonUp, 79, 29, 9, 9);
            DrawEllipse(g, pen, (report.Button & (ushort)NintendoSwitch.Button.B) != 0 ? _brushButtonDown : _brushButtonUp, 71, 37, 9, 9);
            DrawEllipse(g, pen, (report.Button & (ushort)NintendoSwitch.Button.X) != 0 ? _brushButtonDown : _brushButtonUp, 71, 21, 9, 9);
            DrawEllipse(g, pen, (report.Button & (ushort)NintendoSwitch.Button.Y) != 0 ? _brushButtonDown : _brushButtonUp, 63, 29, 9, 9);
            DrawPath(g, pen, (report.Button & (ushort)NintendoSwitch.Button.MINUS) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(29, 12, 5, 5, 1, 1, 1, 1));
            DrawPath(g, pen, (report.Button & (ushort)NintendoSwitch.Button.PLUS) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(65, 12, 5, 5, 1, 1, 1, 1));
            DrawPath(g, pen, (report.Button & (ushort)NintendoSwitch.Button.CAPTURE) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(27, 82, 5, 5, 1, 1, 1, 1));
            DrawPath(g, pen, (report.Button & (ushort)NintendoSwitch.Button.HOME) != 0 ? _brushButtonDown : _brushButtonUp, RoundedRect(67, 82, 5, 5, 1, 1, 1, 1));

            //if (report.Button != 0)
            //    g.FillPath(Brushes.Lime, RoundedRect(rect, 20, 1, 1, 20));
            //else
            //    g.FillPath(Brushes.Lime, RoundedRect(rect, 20, 1, 1, 20));
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
            GraphicsPath path = new GraphicsPath();

            // top left arc
            path.AddArc(new Rectangle(x, y, d1, d1), 180, 90);

            // top right arc
            path.AddArc(new Rectangle(x + w -d2, y, d2, d2), 270, 90);

            // bottom right arc
            path.AddArc(new Rectangle(x + w - d3, y + h - d3, d3, d3), 0, 90);

            // bottom left arc
            path.AddArc(new Rectangle(x, y + h - d4, d4, d4), 90, 90);

            path.CloseFigure();
            return path;
        }

        private bool mouseDown = false;
        private bool mouseMoved = false;
        private Point lastLocation;

        void ResetLocation()
        {
            Left = 0;
            Top = Screen.FromControl(this).WorkingArea.Height - Height;
        }

        private void FormController_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mouseDown = true;
                mouseMoved = false;
                lastLocation = e.Location;
            }
        }

        private void FormController_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mouseDown = false;
                if (!mouseMoved)
                    ResetLocation();
            }
            else if (e.Button == MouseButtons.Left)
                ControllerEnabled = !ControllerEnabled;
            else if (e.Button == MouseButtons.Middle)
                ControllerEnabledLevel = 0;
        }

        private void FormController_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseDown)
            {
                if (e.X == lastLocation.X && e.Y == lastLocation.Y)
                    return;
                mouseMoved = true;
                Location = new Point(Location.X + (e.X - lastLocation.X), Location.Y + (e.Y - lastLocation.Y));
                Update();
            }
        }
    }
}
