using EasyCon.Core;
using EasyDevice;
using EasyScript;

namespace EasyCon2.Helper;

internal class GamePadAdapter(NintendoSwitch easyPad) : ICGamePad
{
    private NintendoSwitch NS = easyPad;

    void ICGamePad.ClickButtons(GamePadKey key, int duration)
    {
        NS.Down(key.ToECKey());
        CustomDelay.Delay(duration);
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

    void ICGamePad.ClickStick(GamePadKey key, byte x, byte y, int duration)
    {
        NS.Down(key.ToECKey(x, y));
        CustomDelay.Delay(duration);
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
