using EasyDevice;
using EasyScript;

namespace EasyCon2.Helper;

internal class GamePadAdapter(NintendoSwitch easyPad) : ICGamePad
{
    private NintendoSwitch NS = easyPad;

    void ICGamePad.ClickButtons(ECKey key, int duration)
    {
        NS.Press(key, duration);
    }

    void ICGamePad.PressButtons(ECKey key)
    {
        NS.Down(key);
    }

    void ICGamePad.ReleaseButtons(ECKey key)
    {
        NS.Up(key);
    }

    void ICGamePad.ChangeAmiibo(uint index)
    {
        NS.ChangeAmiiboIndex((byte)(index & 0x0F));
    }
}
