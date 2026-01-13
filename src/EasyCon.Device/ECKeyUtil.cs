namespace EasyDevice;

public static class ECKeyUtil
{
    const int HatMask = 0b00010000; // 0x10

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
            r => { r.LX = SwitchStick.STICK_CENTER; r.LY = SwitchStick.STICK_CENTER; }
        );
    }

    static ECKey RStick(byte x, byte y)
    {
        return new ECKey($"RStick({x},{y})",
            33,
            r => { r.RX = x; r.RY = y; },
            r => { r.RX = SwitchStick.STICK_CENTER; r.RY = SwitchStick.STICK_CENTER; }
        );
    }

    public static void GetXYFromDirection(DirectionKey dkey, out byte x, out byte y, double degree = 1)
    {
        degree = Math.Clamp(degree,0, 1);
        double dx = 0, dy = 0;
        if (dkey.HasFlag(DirectionKey.Left) && !dkey.HasFlag(DirectionKey.Right))
            dx=-1;
        else if (!dkey.HasFlag(DirectionKey.Left) && dkey.HasFlag(DirectionKey.Right))
            dx=1;
        if (dkey.HasFlag(DirectionKey.Up) && !dkey.HasFlag(DirectionKey.Down))
            dy = 1;
        else if (!dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Down))
            dy=-1;

        dx *= degree;
        dy *= degree;
        x = (byte)((dx + 1) * SwitchStick.STICK_CENTER).Clamp(0, 255);
        y = (byte)((-dy + 1) * SwitchStick.STICK_CENTER).Clamp(0, 255);
    }

    public static ECKey LStick(DirectionKey dkey, bool slow = false)
    {
        GetXYFromDirection(dkey, out byte x, out byte y, 1);
        return LStick(x, y);
    }

    public static ECKey RStick(DirectionKey dkey, bool slow = false)
    {
        GetXYFromDirection(dkey, out byte x, out byte y, 1);
        return RStick(x, y);
    }

    public static void GetXYFromDegree(double rdegree, out byte x, out byte y, double degree = 1)
    {
        degree = Math.Clamp(degree,0, 1);
        double radian = rdegree * Math.PI / 180;
        double dy = Math.Round(
            (Math.Tan(radian) * Math.Sign(Math.Cos(radian)) ).Clamp(-1, 1)
            , 4);
        double dx = radian == 0 ? 1 : 
        Math.Round(
            (1 / Math.Tan(radian) * Math.Sign(Math.Sin(radian)) ).Clamp(-1, 1)
        , 4);
        dx *= degree;
        dy *= degree;
        x = (byte)((dx + 1) * SwitchStick.STICK_CENTER).Clamp(0, 255);
        y = (byte)((-dy + 1) * SwitchStick.STICK_CENTER).Clamp(0, 255);
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
