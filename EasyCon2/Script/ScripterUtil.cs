﻿using ECDevice;

namespace EasyCon2.Script;

internal static class ScripterUtil
{
    public static int GetDirectionIndex(ECKey key)
    {
        int x = key.StickX;
        int y = key.StickY;
        if (x == NSwitchUtil.STICK_CENTER && y == NSwitchUtil.STICK_CENTER)
            return -1;
        x = (int)Math.Round(x / 32d);
        y = (int)Math.Round(y / 32d);
        return x >= y ? x + y : 32 - x - y;
    }

    public static ECKey GetKey(string keyname, string direction = "0")
    {
        var isSlow = keyname.EndsWith("SS", StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(direction, out int degree))
        {
            if (keyname.StartsWith("LS", StringComparison.OrdinalIgnoreCase))
                return ECKeyUtil.LStick(degree);
            else
                return ECKeyUtil.RStick(degree);
        }
        else
        {
            var dk = NSKeys.GetDirection(direction);
            if (dk == DirectionKey.None)
                return null;
            if (keyname.StartsWith("LS", StringComparison.OrdinalIgnoreCase))
                return ECKeyUtil.LStick(dk, isSlow);
            else
                return ECKeyUtil.RStick(dk, isSlow);
        }
    }
}
