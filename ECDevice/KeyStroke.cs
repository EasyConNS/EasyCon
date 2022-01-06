using System;
using static ECDevice.NintendoSwitch;

namespace ECDevice
{
    public class KeyStroke
    {
        public readonly Key Key;
        public readonly bool Up;
        public readonly int Duration;
        public readonly DateTime Time;
        public int KeyCode => Key.KeyCode;

        public KeyStroke(Key key, bool up = false, int duration = 0, DateTime time = default)
        {
            Key = key;
            Up = up;
            Duration = duration;
            Time = DateTime.Now;
        }
    }
}
