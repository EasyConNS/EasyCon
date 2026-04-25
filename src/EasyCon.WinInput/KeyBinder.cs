using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;
using System.Runtime.Versioning;

namespace EasyCon.WinInput;

[SupportedOSPlatform("windows")]
public class KeyBinder
{
    private bool _controllerEnabled;

    public bool ControllerEnabled
    {
        get => _controllerEnabled;
        set => _controllerEnabled = value;
    }

    public void RegisterAllKeys(KeyMappingConfig mapping, NintendoSwitch gamepad)
    {
        UnregisterAllKeys();
        var llk = LowLevelKeyboard.GetInstance();

        RegisterKey(llk, mapping.A, ECKeyUtil.Button(SwitchButton.A), gamepad);
        RegisterKey(llk, mapping.B, ECKeyUtil.Button(SwitchButton.B), gamepad);
        RegisterKey(llk, mapping.X, ECKeyUtil.Button(SwitchButton.X), gamepad);
        RegisterKey(llk, mapping.Y, ECKeyUtil.Button(SwitchButton.Y), gamepad);
        RegisterKey(llk, mapping.L, ECKeyUtil.Button(SwitchButton.L), gamepad);
        RegisterKey(llk, mapping.R, ECKeyUtil.Button(SwitchButton.R), gamepad);
        RegisterKey(llk, mapping.ZL, ECKeyUtil.Button(SwitchButton.ZL), gamepad);
        RegisterKey(llk, mapping.ZR, ECKeyUtil.Button(SwitchButton.ZR), gamepad);
        RegisterKey(llk, mapping.Plus, ECKeyUtil.Button(SwitchButton.PLUS), gamepad);
        RegisterKey(llk, mapping.Minus, ECKeyUtil.Button(SwitchButton.MINUS), gamepad);
        RegisterKey(llk, mapping.Capture, ECKeyUtil.Button(SwitchButton.CAPTURE), gamepad);
        RegisterKey(llk, mapping.Home, ECKeyUtil.Button(SwitchButton.HOME), gamepad);
        RegisterKey(llk, mapping.LClick, ECKeyUtil.Button(SwitchButton.LCLICK), gamepad);
        RegisterKey(llk, mapping.RClick, ECKeyUtil.Button(SwitchButton.RCLICK), gamepad);

        RegisterKey(llk, mapping.UpRight, ECKeyUtil.HAT(SwitchHAT.TOP_RIGHT), gamepad);
        RegisterKey(llk, mapping.DownRight, ECKeyUtil.HAT(SwitchHAT.BOTTOM_RIGHT), gamepad);
        RegisterKey(llk, mapping.UpLeft, ECKeyUtil.HAT(SwitchHAT.TOP_LEFT), gamepad);
        RegisterKey(llk, mapping.DownLeft, ECKeyUtil.HAT(SwitchHAT.BOTTOM_LEFT), gamepad);

        RegisterKey(llk, mapping.Up, () => gamepad.HatDirection(DirectionKey.Up, true), () => gamepad.HatDirection(DirectionKey.Up, false));
        RegisterKey(llk, mapping.Down, () => gamepad.HatDirection(DirectionKey.Down, true), () => gamepad.HatDirection(DirectionKey.Down, false));
        RegisterKey(llk, mapping.Left, () => gamepad.HatDirection(DirectionKey.Left, true), () => gamepad.HatDirection(DirectionKey.Left, false));
        RegisterKey(llk, mapping.Right, () => gamepad.HatDirection(DirectionKey.Right, true), () => gamepad.HatDirection(DirectionKey.Right, false));

        RegisterKey(llk, mapping.LSUp, () => gamepad.LeftDirection(DirectionKey.Up, true), () => gamepad.LeftDirection(DirectionKey.Up, false));
        RegisterKey(llk, mapping.LSDown, () => gamepad.LeftDirection(DirectionKey.Down, true), () => gamepad.LeftDirection(DirectionKey.Down, false));
        RegisterKey(llk, mapping.LSLeft, () => gamepad.LeftDirection(DirectionKey.Left, true), () => gamepad.LeftDirection(DirectionKey.Left, false));
        RegisterKey(llk, mapping.LSRight, () => gamepad.LeftDirection(DirectionKey.Right, true), () => gamepad.LeftDirection(DirectionKey.Right, false));
        RegisterKey(llk, mapping.RSUp, () => gamepad.RightDirection(DirectionKey.Up, true), () => gamepad.RightDirection(DirectionKey.Up, false));
        RegisterKey(llk, mapping.RSDown, () => gamepad.RightDirection(DirectionKey.Down, true), () => gamepad.RightDirection(DirectionKey.Down, false));
        RegisterKey(llk, mapping.RSLeft, () => gamepad.RightDirection(DirectionKey.Left, true), () => gamepad.RightDirection(DirectionKey.Left, false));
        RegisterKey(llk, mapping.RSRight, () => gamepad.RightDirection(DirectionKey.Right, true), () => gamepad.RightDirection(DirectionKey.Right, false));
    }

    private void RegisterKey(LowLevelKeyboard llk, int vkCode, ECKey nskey, NintendoSwitch gamepad)
    {
        RegisterKey(llk, vkCode, () => gamepad.Down(nskey), () => gamepad.Up(nskey));
    }

    private void RegisterKey(LowLevelKeyboard llk, int vkCode, Action keydownAction, Action keyupAction)
    {
        bool keydown()
        {
            if (!_controllerEnabled) return false;
            keydownAction?.Invoke();
            return true;
        }
        bool keyup()
        {
            if (!_controllerEnabled) return false;
            keyupAction?.Invoke();
            return true;
        }
        llk.RegisterKeyEvent(vkCode, keydown, keyup);
    }

    public void UnregisterAllKeys()
    {
        LowLevelKeyboard.GetInstance().UnregisterKeyEventAll();
    }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup)
    {
        LowLevelKeyboard.GetInstance().RegisterKeyEvent(0x1B, keydown, keyup);
    }
}