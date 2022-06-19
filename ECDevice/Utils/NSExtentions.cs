using System;
using System.Drawing;

namespace ECDevice;

public static class NXExtensions
{
    public static double Compare(this Color self, Color color)
    {
        return 1 - (Math.Abs(self.R - color.R) + Math.Abs(self.G - color.G) + Math.Abs(self.B - color.B)) / 255.0 / 3;
    }

    public static string GetName(this Enum self)
    {
        return Enum.GetName(self.GetType(), self);
    }

    public static T Clamp<T>(this T self, T min, T max) where T : IComparable<T>
    {
        if (self.CompareTo(min) < 0)
            return min;
        else if (self.CompareTo(max) > 0)
            return max;
        else
            return self;
    }
}

