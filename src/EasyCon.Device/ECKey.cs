namespace EasyDevice;

public sealed class ECKey(string name, int keyCode, 
    Action<SwitchReport> down, Action<SwitchReport> up, 
    int x = -1, int y = -1)
{
    public readonly string Name = name;
    public readonly int KeyCode = keyCode;
    public readonly Action<SwitchReport> Down = down;
    public readonly Action<SwitchReport> Up = up;
    public readonly int StickX = x;
    public readonly int StickY = y;
}
