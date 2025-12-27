using EasyDevice;

namespace EasyVPad
{
    public partial class VPadForm : Form
    {
        readonly JCPainter painter;

        private NintendoSwitch _gamepad;

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

        public VPadForm(IControllerAdapter script, NintendoSwitch gamepad)
        {
            InitializeComponent();

            _gamepad = gamepad;
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

        private void UnregisterAllKeys()
        {
            LowLevelKeyboard.GetInstance().UnregisterKeyEventAll();
        }

        public void RegisterAllKeys(KeyMapping mapping)
        {
            UnregisterAllKeys();

            RegisterKey(mapping.A, ECKeyUtil.Button(SwitchButton.A));
            RegisterKey(mapping.B, ECKeyUtil.Button(SwitchButton.B));
            RegisterKey(mapping.X, ECKeyUtil.Button(SwitchButton.X));
            RegisterKey(mapping.Y, ECKeyUtil.Button(SwitchButton.Y));
            RegisterKey(mapping.L, ECKeyUtil.Button(SwitchButton.L));
            RegisterKey(mapping.R, ECKeyUtil.Button(SwitchButton.R));
            RegisterKey(mapping.ZL, ECKeyUtil.Button(SwitchButton.ZL));
            RegisterKey(mapping.ZR, ECKeyUtil.Button(SwitchButton.ZR));
            RegisterKey(mapping.Plus, ECKeyUtil.Button(SwitchButton.PLUS));
            RegisterKey(mapping.Minus, ECKeyUtil.Button(SwitchButton.MINUS));
            RegisterKey(mapping.Capture, ECKeyUtil.Button(SwitchButton.CAPTURE));
            RegisterKey(mapping.Home, ECKeyUtil.Button(SwitchButton.HOME));
            RegisterKey(mapping.LClick, ECKeyUtil.Button(SwitchButton.LCLICK));
            RegisterKey(mapping.RClick, ECKeyUtil.Button(SwitchButton.RCLICK));

            RegisterKey(mapping.UpRight, ECKeyUtil.HAT(SwitchHAT.TOP_RIGHT));
            RegisterKey(mapping.DownRight, ECKeyUtil.HAT(SwitchHAT.BOTTOM_RIGHT));
            RegisterKey(mapping.UpLeft, ECKeyUtil.HAT(SwitchHAT.TOP_LEFT));
            RegisterKey(mapping.DownLeft, ECKeyUtil.HAT(SwitchHAT.BOTTOM_LEFT));

            RegisterKey(mapping.Up, () => _gamepad.HatDirection(DirectionKey.Up, true), () => _gamepad.HatDirection(DirectionKey.Up, false));
            RegisterKey(mapping.Down, () => _gamepad.HatDirection(DirectionKey.Down, true), () => _gamepad.HatDirection(DirectionKey.Down, false));
            RegisterKey(mapping.Left, () => _gamepad.HatDirection(DirectionKey.Left, true), () => _gamepad.HatDirection(DirectionKey.Left, false));
            RegisterKey(mapping.Right, () => _gamepad.HatDirection(DirectionKey.Right, true), () => _gamepad.HatDirection(DirectionKey.Right, false));

            RegisterKey(mapping.LSUp, () => _gamepad.LeftDirection(DirectionKey.Up, true), () => _gamepad.LeftDirection(DirectionKey.Up, false));
            RegisterKey(mapping.LSDown, () => _gamepad.LeftDirection(DirectionKey.Down, true), () => _gamepad.LeftDirection(DirectionKey.Down, false));
            RegisterKey(mapping.LSLeft, () => _gamepad.LeftDirection(DirectionKey.Left, true), () => _gamepad.LeftDirection(DirectionKey.Left, false));
            RegisterKey(mapping.LSRight, () => _gamepad.LeftDirection(DirectionKey.Right, true), () => _gamepad.LeftDirection(DirectionKey.Right, false));
            RegisterKey(mapping.RSUp, () => _gamepad.RightDirection(DirectionKey.Up, true), () => _gamepad.RightDirection(DirectionKey.Up, false));
            RegisterKey(mapping.RSDown, () => _gamepad.RightDirection(DirectionKey.Down, true), () => _gamepad.RightDirection(DirectionKey.Down, false));
            RegisterKey(mapping.RSLeft, () => _gamepad.RightDirection(DirectionKey.Left, true), () => _gamepad.RightDirection(DirectionKey.Left, false));
            RegisterKey(mapping.RSRight, () => _gamepad.RightDirection(DirectionKey.Right, true), () => _gamepad.RightDirection(DirectionKey.Right, false));
        }


        private void RegisterKey(Keys key, ECKey nskey)
        {
            RegisterKey(key, () => _gamepad.Down(nskey), () => _gamepad.Up(nskey));
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

        private void VPadForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mouseDown = true;
                mouseMoved = false;
                lastLocation = e.Location;
            }
        }

        private void VPadForm_MouseUp(object sender, MouseEventArgs e)
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

        private void VPadForm_MouseMove(object sender, MouseEventArgs e)
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
