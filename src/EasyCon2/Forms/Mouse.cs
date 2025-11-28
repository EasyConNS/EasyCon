using EasyCon2.Helper;
using EasyDevice;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mouse = System.Windows.Forms.Cursor;

namespace EasyCon2.Forms
{
    public partial class Mouse : Form
    {
        internal NintendoSwitch NS;
        public MouseTranslation mouseTranslation;
        public bool firstTime = true;
        private Point center;
        private Point last;
        private Point mousePosition;

        public Mouse(NintendoSwitch gamepad)
        {
            NS = gamepad;
            InitializeComponent();
        }

        //this will need to be rewritten to use raw input instead of hacky WinForms input...
        public void MouseUpdate()
        {
            //SwitchInputSink sink, Panel panel1
            if (!firstTime)
            {
                Point m_translation = mouseTranslation.Translate(Mouse.MousePosition, center);
                var x = m_translation.X;
                var y = m_translation.Y;

                if (x > 255)
                {
                    mousePosition = new Point(Mouse.MousePosition.X - (x - 255), Mouse.MousePosition.Y);
                    x = 255;
                }
                else if (x < 0)
                {
                    mousePosition = new Point(Mouse.MousePosition.X - x, Mouse.MousePosition.Y);
                    x = 0;
                }
                if (y > 255)
                {
                    mousePosition = new Point(Mouse.MousePosition.X, Mouse.MousePosition.Y - (y - 255));
                    y = 255;
                }
                else if (y < 0)
                {
                    mousePosition = new Point(Mouse.MousePosition.X, Mouse.MousePosition.Y - y);
                    y = 0;
                }

                if (last.X != x)
                {
                    Console.WriteLine(x + ", " + y);
                    //sink.Update(InputFrame.ParseInputString("RX=" + x));
                }

                if (last.Y != y)
                {
                    Console.WriteLine(x + ", " + y);
                    //sink.Update(InputFrame.ParseInputString("RY=" + y));
                }
                mousePosition = center;
                last = new Point(x, y);
            }
            else
            {
                firstTime = false;
                //mousePosition = new Point(panel1.PointToScreen(Point.Empty).X + 128, panel1.PointToScreen(Point.Empty).Y + 128);
                center = Mouse.MousePosition;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            MouseUpdate();
        }
    }
}
