using EasyCon.Script;
using EasyDevice;
using EasyScript;

namespace EasyCon.Core;

public class GamePadAdapter(NintendoSwitch easyPad, bool highResolution = false) : ICGamePad
{
    private readonly NintendoSwitch NS = easyPad;

    public DelayType DelayMethod => highResolution ? DelayType.HighResolution : DelayType.LowCPU;

    void ICGamePad.ClickButtons(GamePadKey key, int duration, CancellationToken token)
    {
        NS.Down(key.ToECKey());
        switch (DelayMethod)
        {
            case DelayType.Normal: Thread.Sleep(duration); break;
            case DelayType.LowCPU: CustomDelay.AISleep(duration); break;
            case DelayType.HighResolution:
            default:
                CustomDelay.Delay(duration, token);
                break;
        }
        NS.Up(key.ToECKey());
    }

    void ICGamePad.PressButtons(GamePadKey key)
    {
        NS.Down(key.ToECKey());
    }

    void ICGamePad.ReleaseButtons(GamePadKey key)
    {
        NS.Up(key.ToECKey());
    }

    void ICGamePad.ChangeAmiibo(uint index)
    {
        NS.ChangeAmiiboIndex((byte)(index & 0x0F));
    }

    void ICGamePad.ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
    {
        NS.Down(key.ToECKey(x, y));
        switch (DelayMethod)
        {
            case DelayType.Normal: Thread.Sleep(duration); break;
            case DelayType.LowCPU: CustomDelay.AISleep(duration); break;
            case DelayType.HighResolution:
            default:
                CustomDelay.Delay(duration, token);
                break;
        }
        NS.Up(key.ToECKey(x, y));
    }

    void ICGamePad.SetStick(GamePadKey key, byte x, byte y)
    {
        if (x == SwitchStick.STICK_CENTER && y == SwitchStick.STICK_CENTER)
        {
            NS.Up(key.ToECKey(x, y));
        }
        else
        {
            NS.Down(key.ToECKey(x, y));
        }
    }
}