using EasyScript;

namespace EasyCon2.Avalonia.Services;

public class NullGamePad : ICGamePad
{
    public DelayType DelayMethod => DelayType.HighResolution;
    public void ClickButtons(GamePadKey key, int duration, CancellationToken token) { }
    public void PressButtons(GamePadKey key) { }
    public void ReleaseButtons(GamePadKey key) { }
    public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }
    public void SetStick(GamePadKey key, byte x, byte y) { }
    public void ChangeAmiibo(uint index) { }
}
