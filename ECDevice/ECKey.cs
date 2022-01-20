using System;
using System.Collections.Generic;

namespace ECDevice
{
    public partial class NintendoSwitch
    {
        public partial class ECKey
        {
            public readonly string Name;
            public readonly int KeyCode;
            public readonly Action<SwitchReport> Down;
            public readonly Action<SwitchReport> Up;
            public readonly int StickX;
            public readonly int StickY;

            public ECKey(string name, int keyCode, Action<SwitchReport> down, Action<SwitchReport> up, int x = -1, int y = -1)
            {
                Name = name;
                KeyCode = keyCode;
                Down = down;
                Up = up;
                StickX = x;
                StickY = y;
            }


            public static implicit operator ECKey(SwitchButton button)
            {
                return ECKeyUtil.Button(button);
            }

            public static implicit operator ECKey(SwitchHAT hat)
            {
                return ECKeyUtil.HAT(hat);
            }

            public static implicit operator ECKey(int keyCode)
            {
                return ECKeyUtil.FromKeyCode(keyCode);
            }
        }
    }
}
