using EasyDevice;
using EasyScript;

namespace EasyCon.Core;

public static class KeyExt
{
    public static ECKey ToECKey(this GamePadKey key, byte x = 0, byte y = 0)
    {
        return key switch
        {
            GamePadKey.A => ECKeyUtil.Button(SwitchButton.A),
            GamePadKey.B => ECKeyUtil.Button(SwitchButton.B),
            GamePadKey.X => ECKeyUtil.Button(SwitchButton.X),
            GamePadKey.Y => ECKeyUtil.Button(SwitchButton.Y),
            GamePadKey.L => ECKeyUtil.Button(SwitchButton.L),
            GamePadKey.R => ECKeyUtil.Button(SwitchButton.R),
            GamePadKey.ZL => ECKeyUtil.Button(SwitchButton.ZL),
            GamePadKey.ZR => ECKeyUtil.Button(SwitchButton.ZR),
            GamePadKey.MINUS => ECKeyUtil.Button(SwitchButton.MINUS),
            GamePadKey.PLUS => ECKeyUtil.Button(SwitchButton.PLUS),
            GamePadKey.LCLICK => ECKeyUtil.Button(SwitchButton.LCLICK),
            GamePadKey.RCLICK => ECKeyUtil.Button(SwitchButton.RCLICK),
            GamePadKey.HOME => ECKeyUtil.Button(SwitchButton.HOME),
            GamePadKey.CAPTURE => ECKeyUtil.Button(SwitchButton.CAPTURE),
            GamePadKey.TOP => ECKeyUtil.HAT(SwitchHAT.TOP),
            GamePadKey.TOP_RIGHT => ECKeyUtil.HAT(SwitchHAT.TOP_RIGHT),
            GamePadKey.RIGHT => ECKeyUtil.HAT(SwitchHAT.RIGHT),
            GamePadKey.DOWN_RIGHT => ECKeyUtil.HAT(SwitchHAT.BOTTOM_RIGHT),
            GamePadKey.DOWN => ECKeyUtil.HAT(SwitchHAT.BOTTOM),
            GamePadKey.DOWN_LEFT => ECKeyUtil.HAT(SwitchHAT.BOTTOM_LEFT),
            GamePadKey.LEFT => ECKeyUtil.HAT(SwitchHAT.LEFT),
            GamePadKey.TOP_LEFT => ECKeyUtil.HAT(SwitchHAT.TOP_LEFT),
            GamePadKey.LS => ECKeyUtil.LStick(x, y),
            GamePadKey.RS => ECKeyUtil.RStick(x, y),
            _ => throw new Exception("按键出错了"),
        };
    }
}