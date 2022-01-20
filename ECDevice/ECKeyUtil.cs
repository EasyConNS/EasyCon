using System;
using System.Collections.Generic;
using static ECDevice.NintendoSwitch;

namespace ECDevice
{
    public static class ECKeyUtil
    {
        const int HatMask = 0b00010000;

        enum StickKeyCode
        {
            LS = 32,
            RS = 33
        }

        static readonly Dictionary<SwitchButton, int> _buttonKeyCodes = new();

        static ECKeyUtil()
        {
            foreach (SwitchButton b in Enum.GetValues(typeof(SwitchButton)))
            {
                int n = (int)b;
                int k = -1;
                while (n != 0)
                {
                    n >>= 1;
                    k++;
                }
                _buttonKeyCodes[b] = k;
            }
        }

        public static ECKey Button(SwitchButton button)
        {
            return new ECKey(button.GetName(),
                _buttonKeyCodes[button],
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

        public static ECKey LStick(byte x, byte y)
        {
            return new ECKey($"LStick({x},{y})",
                (int)StickKeyCode.LS,
                r => { r.LX = x; r.LY = y; },
                r => { r.LX = NSwitchUtil.STICK_CENTER; r.LY = NSwitchUtil.STICK_CENTER; },
                x,
                y
            );
        }

        public static ECKey RStick(byte x, byte y)
        {
            return new ECKey($"RStick({x},{y})",
                (int)StickKeyCode.RS,
                r => { r.RX = x; r.RY = y; },
                r => { r.RX = NSwitchUtil.STICK_CENTER; r.RY = NSwitchUtil.STICK_CENTER; },
                x,
                y
            );
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

        public static ECKey FromKeyCode(int keyCode)
        {
            if (keyCode < HatMask)
                return Button((SwitchButton)(1 << keyCode));
            else if (keyCode == (int)StickKeyCode.LS)
                return LStick(NSwitchUtil.STICK_CENTER, NSwitchUtil.STICK_CENTER);
            else if (keyCode == (int)StickKeyCode.RS)
                return RStick(NSwitchUtil.STICK_CENTER, NSwitchUtil.STICK_CENTER);
            else
                return HAT((SwitchHAT)(keyCode ^ HatMask));
        }
    }
}
