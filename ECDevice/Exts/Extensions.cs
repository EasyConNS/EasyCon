using System;
using System.Drawing;

namespace ECDevice.Exts
{
    public static class Extensions
    {
        public static double Compare(this Color self, Color color)
        {
            return 1 - (Math.Abs(self.R - color.R) + Math.Abs(self.G - color.G) + Math.Abs(self.B - color.B)) / 255.0 / 3;
        }

        public static string GetName(this Enum self)
        {
            return Enum.GetName(self.GetType(), self);
        }
    }
}
