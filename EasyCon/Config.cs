using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyCon
{
    public class Config
    {
        public KeyMapping KeyMapping;
        public bool ShowControllerHelp = true;

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

    public struct KeyMapping
    {
        public Keys A;
        public Keys B;
        public Keys X;
        public Keys Y;
        public Keys L;
        public Keys R;
        public Keys ZL;
        public Keys ZR;
        public Keys Plus;
        public Keys Minus;
        public Keys Capture;
        public Keys Home;
        public Keys LClick;
        public Keys RClick;
        public Keys Up;
        public Keys Down;
        public Keys Left;
        public Keys Right;
        public Keys LSUp;
        public Keys LSDown;
        public Keys LSLeft;
        public Keys LSRight;
        public Keys RSUp;
        public Keys RSDown;
        public Keys RSLeft;
        public Keys RSRight;
    }
}