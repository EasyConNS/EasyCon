using PTController;

namespace EasyCon2.Global
{
    public class VControllerConfig
    {
        public KeyMapping KeyMapping;
        public bool ShowControllerHelp = true;
        public string CaptureType = "ANY";
        public string AlertToken = "";

        public void SetDefault()
        {
            KeyMapping.A = Keys.Y;
            KeyMapping.B = Keys.U;
            KeyMapping.X = Keys.I;
            KeyMapping.Y = Keys.H;
            KeyMapping.L = Keys.G;
            KeyMapping.R = Keys.T;
            KeyMapping.ZL = Keys.F;
            KeyMapping.ZR = Keys.R;
            KeyMapping.Plus = Keys.K;
            KeyMapping.Minus = Keys.J;
            KeyMapping.Capture = Keys.Z;
            KeyMapping.Home = Keys.C;
            KeyMapping.LClick = Keys.Q;
            KeyMapping.RClick = Keys.E;
            KeyMapping.Up = Keys.None;
            KeyMapping.Down = Keys.None;
            KeyMapping.Left = Keys.None;
            KeyMapping.Right = Keys.None;
            KeyMapping.UpLeft = Keys.None;
            KeyMapping.UpRight = Keys.None;
            KeyMapping.DownLeft = Keys.None;
            KeyMapping.DownRight = Keys.None;
            KeyMapping.LSUp = Keys.W;
            KeyMapping.LSDown = Keys.S;
            KeyMapping.LSLeft = Keys.A;
            KeyMapping.LSRight = Keys.D;
            KeyMapping.RSUp = Keys.Up;
            KeyMapping.RSDown = Keys.Down;
            KeyMapping.RSLeft = Keys.Left;
            KeyMapping.RSRight = Keys.Right;
        }
    }
}
