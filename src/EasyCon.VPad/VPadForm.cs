using EasyDevice;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyVPad
{
    public partial class VPadForm : Form
    {
        readonly JCPainter painter;

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
        public bool ControllerEnabled
        {
            get => ControllerEnabledLevel == 2;
            set
            {
                if (value)
                    ControllerEnabledLevel = 2;
                else if (ControllerEnabledLevel == 2)
                    ControllerEnabledLevel = 1;
            }
        }

        public VPadForm(IControllerAdapter script, IReporter gamepad)
        {
            InitializeComponent();
            this.painter = new(gamepad, script, GetJCImg);
        }

        private void FormController_Load(object sender, EventArgs e)
        {
            var transparent = Color.FromArgb(254, 253, 252);
            BackColor = transparent;
            TransparencyKey = transparent;
            Width = 100;
            Height = 100;
            ResetLocation();

            this.Paint += (_, e) =>
            {
                painter.OnPaint(e.Graphics, e.ClipRectangle);
            };

            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        Invalidate();
                        this.BeginInvoke(new Action(() => {
                            Opacity = ControllerEnabled ? 1 : 0.5;
                        }));
                        
                    }
                    catch (Exception e)
                    {

                    }
                    Thread.Sleep(100);
                }
            });
        }

        public void UnregisterAllKeys()
        {
            LowLevelKeyboard.GetInstance().UnregisterKeyEventAll();
        }

        public void RegisterKey(Keys key, Action keydownAction, Action? keyupAction = null)
        {
            bool keydown()
            {
                if (!ControllerEnabled)
                    return false;
                keydownAction?.Invoke();
                return true;
            }
            bool keyup()
            {
                if (!ControllerEnabled)
                    return false;
                keyupAction?.Invoke();
                return true;
            }
            LowLevelKeyboard.GetInstance().RegisterKeyEvent((int)key, keydown, keyup);
        }

        static Bitmap GetJCImg(string name)
        {
            return name switch
            {
                "JoyCon" => Properties.Resources.JoyCon,
                "JoyCon_L_0" => Properties.Resources.JoyCon_L_0,
                "JoyCon_L_1" => Properties.Resources.JoyCon_L_1,
                "JoyCon_R_0" => Properties.Resources.JoyCon_R_0,
                "JoyCon_R_1" => Properties.Resources.JoyCon_R_1,
                "JoyCon_ZL_0" => Properties.Resources.JoyCon_ZL_0,
                "JoyCon_ZL_1" => Properties.Resources.JoyCon_ZL_1,
                "JoyCon_ZR_0" => Properties.Resources.JoyCon_ZR_0,
                "JoyCon_ZR_1" => Properties.Resources.JoyCon_ZR_1,
                _ => throw new NotImplementedException(),
            };
        }

        private bool mouseDown = false;
        private bool mouseMoved = false;
        private Point lastLocation;

        void ResetLocation()
        {
            Left = Screen.FromControl(this).WorkingArea.Width / 2;
            Top = (Screen.FromControl(this).WorkingArea.Height - Height) / 2;
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
