using System;

namespace ECDevice;

public class KeyStroke
{
    public readonly ECKey Key;
    public int KeyCode => Key.KeyCode;
    public readonly bool Up;
    public readonly int Duration;
    public readonly DateTime Time;

    public KeyStroke(ECKey key, bool up = false, int duration = 0, DateTime time = default)
    {
        Key = key;
        Up = up;
        Duration = duration;
        Time = DateTime.Now;
    }
}
