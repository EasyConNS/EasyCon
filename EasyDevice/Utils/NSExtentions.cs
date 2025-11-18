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

    public static DirectionKey GetDirectionFromHAT(this SwitchHAT hat)
    {
        return hat switch
        {
            SwitchHAT.TOP => DirectionKey.Up,
            SwitchHAT.TOP_RIGHT => DirectionKey.Up | DirectionKey.Right,
            SwitchHAT.RIGHT => DirectionKey.Right,
            SwitchHAT.BOTTOM_RIGHT => DirectionKey.Down | DirectionKey.Right,
            SwitchHAT.BOTTOM => DirectionKey.Down,
            SwitchHAT.BOTTOM_LEFT => DirectionKey.Down | DirectionKey.Left,
            SwitchHAT.LEFT => DirectionKey.Left,
            SwitchHAT.TOP_LEFT => DirectionKey.Up | DirectionKey.Left,
            _ => DirectionKey.None,
        };
    }

    public static SwitchHAT GetHATFromDirection(this DirectionKey dkey)
    {
        if (dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Down))
            dkey &= ~DirectionKey.Up & ~DirectionKey.Down;
        if (dkey.HasFlag(DirectionKey.Left) && dkey.HasFlag(DirectionKey.Right))
            dkey &= ~DirectionKey.Left & ~DirectionKey.Right;
        if (dkey == DirectionKey.Up)
            return SwitchHAT.TOP;
        if (dkey == DirectionKey.Down)
            return SwitchHAT.BOTTOM;
        if (dkey == DirectionKey.Left)
            return SwitchHAT.LEFT;
        if (dkey == DirectionKey.Right)
            return SwitchHAT.RIGHT;
        if (dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Left))
            return SwitchHAT.TOP_LEFT;
        if (dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Right))
            return SwitchHAT.TOP_RIGHT;
        if (dkey.HasFlag(DirectionKey.Down) && dkey.HasFlag(DirectionKey.Left))
            return SwitchHAT.BOTTOM_LEFT;
        if (dkey.HasFlag(DirectionKey.Down) && dkey.HasFlag(DirectionKey.Right))
            return SwitchHAT.BOTTOM_RIGHT;
        return SwitchHAT.CENTER;
    }
}
