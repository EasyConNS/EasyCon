using System;

namespace EasyDevice;

public static class ECKeyUtil
{
    const int HatMask = 0b00010000;

    static int Keycode(SwitchButton key)
    {
        int n = (int)key;
        int k = -1;
        while (n != 0)
        {
            n >>= 1;
            k++;
        }
        return k;
    }

    public static ECKey Button(SwitchButton button)
    {
        return new ECKey(button.GetName(),
            Keycode(button),
            r => r.Button |= (ushort)button,
            r => r.Button &= (ushort)~button
        );
    }

    public static ECKey HAT(SwitchHAT hat)
    {
        return new ECKey("HAT." + hat.GetName(),
            (int)hat | HatMask,
            r => r.HAT = (byte)hat,
            r => r.HAT = (byte)SwitchHAT.CENTER
        );
    }

    public static ECKey HAT(DirectionKey dhat)
    {
        return HAT(dhat.GetHATFromDirection());
    }

    static ECKey LStick(byte x, byte y)
    {
        return new ECKey($"LStick({x},{y})",
            32,
            r => { r.LX = x; r.LY = y; },
            r => { r.LX = SwitchStick.STICK_CENTER; r.LY = SwitchStick.STICK_CENTER; },
            x,
            y
        );
    }

    static ECKey RStick(byte x, byte y)
    {
        return new ECKey($"RStick({x},{y})",
            33,
            r => { r.RX = x; r.RY = y; },
            r => { r.RX = SwitchStick.STICK_CENTER; r.RY = SwitchStick.STICK_CENTER; },
            x,
            y
        );
    }

    static void GetXYFromDirection(DirectionKey dkey, out byte x, out byte y, bool slow = false)
    {
        if (dkey.HasFlag(DirectionKey.Left) && !dkey.HasFlag(DirectionKey.Right))
            x = slow ? SwitchStick.STICK_CENMIN : SwitchStick.STICK_MIN;
        else if (!dkey.HasFlag(DirectionKey.Left) && dkey.HasFlag(DirectionKey.Right))
            x = slow ? SwitchStick.STICK_CENMAX : SwitchStick.STICK_MAX;
        else
            x = SwitchStick.STICK_CENTER;
        if (dkey.HasFlag(DirectionKey.Up) && !dkey.HasFlag(DirectionKey.Down))
            y = slow ? SwitchStick.STICK_CENMIN : SwitchStick.STICK_MIN;
        else if (!dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Down))
            y = slow ? SwitchStick.STICK_CENMAX : SwitchStick.STICK_MAX;
        else
            y = SwitchStick.STICK_CENTER;
    }

    public static ECKey LStick(DirectionKey dkey, bool slow = false)
    {
        GetXYFromDirection(dkey, out byte x, out byte y, slow);
        return LStick(x, y);
    }

    public static ECKey RStick(DirectionKey dkey, bool slow = false)
    {
        GetXYFromDirection(dkey, out byte x, out byte y, slow);
        return RStick(x, y);
    }

    static void GetXYFromDegree(double degree, out byte x, out byte y)
    {
        double radian = degree * Math.PI / 180;
        double dy = Math.Round((Math.Tan(radian) * Math.Sign(Math.Cos(radian))).Clamp(-1, 1), 4);
        double dx = radian == 0 ? 1 : Math.Round((1 / Math.Tan(radian) * Math.Sign(Math.Sin(radian))).Clamp(-1, 1), 4);
        x = (byte)((dx + 1) / 2 * (SwitchStick.STICK_MAX - SwitchStick.STICK_MIN) + SwitchStick.STICK_MIN);
        y = (byte)((-dy + 1) / 2 * (SwitchStick.STICK_MAX - SwitchStick.STICK_MIN) + SwitchStick.STICK_MIN);
    }

    public static ECKey LStick(double degree)
    {
        GetXYFromDegree(degree, out byte x, out byte y);
        return LStick(x, y);
    }

    public static ECKey RStick(double degree)
    {
        GetXYFromDegree(degree, out byte x, out byte y);
        return RStick(x, y);
    }
}
