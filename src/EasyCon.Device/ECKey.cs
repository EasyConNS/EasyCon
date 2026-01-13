namespace EasyDevice;

public sealed class ECKey(string name, int keyCode, Action<SwitchReport> down, Action<SwitchReport> up)
{
    public readonly string Name = name;
    public readonly int KeyCode = keyCode;
    public readonly Action<SwitchReport> Down = down;
    public readonly Action<SwitchReport> Up = up;
}


public class KeyStroke(ECKey key, bool up = false, int duration = 0, DateTime time = default)
{
    public readonly ECKey Key = key;
    public int KeyCode => Key.KeyCode;
    public readonly bool Up = up;
    public readonly int Duration = duration;
    public readonly DateTime Time = DateTime.Now;
}
