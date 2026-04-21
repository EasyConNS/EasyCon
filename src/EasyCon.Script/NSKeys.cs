using EasyScript;

namespace EasyCon.Script;

static class NSKeys
{
    const byte STICK_MIN = 0;
    const byte STICK_CENTER = 128;
    const byte STICK_MAX = 255;
    public static GamePadKey GetKey(string keyname)
    {
        return keyname.ToUpper() switch
        {
            "A" => GamePadKey.A,
            "B" => GamePadKey.B,
            "X" => GamePadKey.X,
            "Y" => GamePadKey.Y,
            "L" => GamePadKey.L,
            "R" => GamePadKey.R,
            "ZL" => GamePadKey.ZL,
            "ZR" => GamePadKey.ZR,
            "PLUS" => GamePadKey.PLUS,
            "MINUS" => GamePadKey.MINUS,
            "LCLICK" => GamePadKey.LCLICK,
            "RCLICK" => GamePadKey.RCLICK,
            "HOME" => GamePadKey.HOME,
            "CAPTURE" => GamePadKey.CAPTURE,
            "LEFT" => GamePadKey.LEFT,
            "RIGHT" => GamePadKey.RIGHT,
            "UP" => GamePadKey.TOP,
            "DOWN" => GamePadKey.DOWN,
            "DOWNLEFT" => GamePadKey.DOWN_LEFT,
            "DOWNRIGHT" => GamePadKey.DOWN_RIGHT,
            "UPLEFT" => GamePadKey.TOP_LEFT,
            "UPRIGHT" => GamePadKey.TOP_RIGHT,
            "LS" => GamePadKey.LS,
            "RS" => GamePadKey.RS,
            _ => GamePadKey.None,
        };
    }

    public static bool CheckDirection(string direction, out int degree)
    {
        if (int.TryParse(direction, out degree))
        {
            return degree >= 0;
        }
        switch (direction.ToUpper())
        {
            case "UP": degree = 90; return true;
            case "DOWN": degree = 270; return true;
            case "LEFT": degree = 180; return true;
            case "RIGHT": degree = 0; return true;
            case "UPLEFT": degree = 135; return true;
            case "UPRIGHT": degree = 45; return true;
            case "DOWNLEFT": degree = 225; return true;
            case "DOWNRIGHT": degree = 315; return true;
            default: degree = -1; return false;
        }
    }

    public static void GetXYFromDegree(double rdegree, out byte x, out byte y, double degree = 1)
    {
        // -1 means RESET 
        if (rdegree == -1)
        {
            x = STICK_CENTER;
            y = STICK_CENTER;
            return;
        }
        degree = Math.Clamp(degree, 0, 1);
        double radian = rdegree * Math.PI / 180;
        double dy = Math.Round(
            (Math.Tan(radian) * Math.Sign(Math.Cos(radian))).Clamp(-1, 1)
            , 4);
        double dx = radian == 0 ? 1 :
        Math.Round(
            (1 / Math.Tan(radian) * Math.Sign(Math.Sin(radian))).Clamp(-1, 1)
        , 4);
        dx *= degree;
        dy *= degree;
        x = (byte)Math.Clamp((dx + 1) * STICK_CENTER, STICK_MIN, STICK_MAX);
        y = (byte)Math.Clamp((-dy + 1) * STICK_CENTER, STICK_MIN, STICK_MAX);
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