using System.Windows.Forms;

namespace PTController
{
    public struct KeyMapping
    {
        public KeyMapping() { }

        public Keys A { get; set; } = Keys.Y;
        public Keys B { get; set; } = Keys.U;
        public Keys X { get; set; } = Keys.I;
        public Keys Y { get; set; } = Keys.H;
        public Keys L { get; set; } = Keys.G;
        public Keys R { get; set; } = Keys.T;
        public Keys ZL { get; set; } = Keys.F;
        public Keys ZR { get; set; } = Keys.R;
        public Keys Plus { get; set; } = Keys.K;
        public Keys Minus { get; set; } = Keys.J;
        public Keys Capture { get; set; } = Keys.Z;
        public Keys Home { get; set; } = Keys.C;
        public Keys LClick { get; set; } = Keys.Q;
        public Keys RClick { get; set; } = Keys.E;
        public Keys Up { get; set; } = Keys.None;
        public Keys Down { get; set; } = Keys.None;
        public Keys Left { get; set; } = Keys.None;
        public Keys Right { get; set; } = Keys.None;
        public Keys UpRight { get; set; } = Keys.None;
        public Keys DownRight { get; set; } = Keys.None;
        public Keys UpLeft { get; set; } = Keys.None;
        public Keys DownLeft { get; set; } = Keys.None;
        public Keys LSUp { get; set; } = Keys.W;
        public Keys LSDown { get; set; } = Keys.S;
        public Keys LSLeft { get; set; } = Keys.A;
        public Keys LSRight { get; set; } = Keys.D;
        public Keys RSUp { get; set; } = Keys.Up;
        public Keys RSDown { get; set; } = Keys.Down;
        public Keys RSLeft { get; set; } = Keys.Left;
        public Keys RSRight { get; set; } = Keys.Right;
    }
}
