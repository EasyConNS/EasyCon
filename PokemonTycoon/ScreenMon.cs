using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    static class ScreenMon
    {
        static Bitmap _unitImg = new Bitmap(1, 1);

        public static Color GetColor()
        {
            Rectangle bounds = new Rectangle(Cursor.Position.X, Cursor.Position.Y, 1, 1);
            using (Graphics g = Graphics.FromImage(_unitImg))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return _unitImg.GetPixel(0, 0);
        }
    }
}
