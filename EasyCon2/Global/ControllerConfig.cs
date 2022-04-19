using PTController;

namespace EasyCon2.Global
{
    public class VControllerConfig
    {
        public KeyMapping KeyMapping { get; set; }
        public bool ShowControllerHelp { get; set; } = true;
        public string CaptureType { get; set; } = "ANY";
        public string AlertToken { get; set; } = string.Empty;
        public string ChannelToken { get; set; } = string.Empty;

        public bool ChannelControl { get; set; } = true;

        public void SetDefault()
        {
            KeyMapping = new KeyMapping
            {
                A = Keys.Y,
                B = Keys.U,
                X = Keys.I,
                Y = Keys.H,
                L = Keys.G,
                R = Keys.T,
                ZL = Keys.F,
                ZR = Keys.R,
                Plus = Keys.K,
                Minus = Keys.J,
                Capture = Keys.Z,
                Home = Keys.C,
                LClick = Keys.Q,
                RClick = Keys.E,
                Up = Keys.None,
                Down = Keys.None,
                Left = Keys.None,
                Right = Keys.None,
                UpLeft = Keys.None,
                UpRight = Keys.None,
                DownLeft = Keys.None,
                DownRight = Keys.None,
                LSUp = Keys.W,
                LSDown = Keys.S,
                LSLeft = Keys.A,
                LSRight = Keys.D,
                RSUp = Keys.Up,
                RSDown = Keys.Down,
                RSLeft = Keys.Left,
                RSRight = Keys.Right
            };
        }
    }
}
