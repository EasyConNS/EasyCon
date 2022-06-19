using System;

namespace ECDevice;

public class ECKey
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
}
