using System;
using System.Diagnostics;
using System.IO.Ports;

namespace ECDevice
{
    public partial class NintendoSwitch
    {
        private static NintendoSwitch _instance;

        public static string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }

        public static NintendoSwitch GetInstance()
        {
            if (_instance == null)
                _instance = new NintendoSwitch();
            return _instance;
        }

        private static void PrintKey(string str, Key key = null)
        {
            str = str + " " + key?.Name ?? "";
            Debug.WriteLine(str);
        }

        public static DirectionKey GetDirectionFromHAT(SwitchHAT hat)
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

        private static void GetXYFromDegree(double degree, out byte x, out byte y)
        {
            double radian = degree * Math.PI / 180;
            double dy = Math.Round((Math.Tan(radian) * Math.Sign(Math.Cos(radian))).Clamp(-1, 1), 4);
            double dx = radian == 0 ? 1 : Math.Round((1 / Math.Tan(radian) * Math.Sign(Math.Sin(radian))).Clamp(-1, 1), 4);
            x = (byte)((dx + 1) / 2 * (NSwitchUtil.STICK_MAX - NSwitchUtil.STICK_MIN) + NSwitchUtil.STICK_MIN);
            y = (byte)((-dy + 1) / 2 * (NSwitchUtil.STICK_MAX - NSwitchUtil.STICK_MIN) + NSwitchUtil.STICK_MIN);
        }

        private static void GetXYFromDirection(DirectionKey dkey, out byte x, out byte y, bool slow = false)
        {
            if (dkey.HasFlag(DirectionKey.Left) && !dkey.HasFlag(DirectionKey.Right))
                x = slow? NSwitchUtil.STICK_CENMIN : NSwitchUtil.STICK_MIN;
            else if (!dkey.HasFlag(DirectionKey.Left) && dkey.HasFlag(DirectionKey.Right))
                x = slow? NSwitchUtil.STICK_CENMAX : NSwitchUtil.STICK_MAX;
            else
                x = NSwitchUtil.STICK_CENTER;
            if (dkey.HasFlag(DirectionKey.Up) && !dkey.HasFlag(DirectionKey.Down))
                y = slow ? NSwitchUtil.STICK_CENMIN : NSwitchUtil.STICK_MIN;
            else if (!dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Down))
                y = slow ? NSwitchUtil.STICK_CENMAX : NSwitchUtil.STICK_MAX;
            else
                y = NSwitchUtil.STICK_CENTER;
        }

        private static SwitchHAT GetHATFromDirection(DirectionKey dkey)
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
}
