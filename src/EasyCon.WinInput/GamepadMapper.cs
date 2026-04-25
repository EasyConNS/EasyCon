using EasyDevice;
using GamepadApi;
using System.Numerics;

namespace EasyCon.WinInput;

public static class GamepadMapper
{
    public static SwitchReport Map(GamepadState state, GamepadMappingConfig config)
    {
        var report = new SwitchReport();

        MapButtons(state.Buttons, config.IsSwitchXY, report);
        MapTriggers(state.LeftTrigger, state.RightTrigger, config.TriggerThreshold, report);
        MapHat(state.Buttons, report);
        (report.LX, report.LY) = MapStick(state.LeftStick);
        (report.RX, report.RY) = MapStick(state.RightStick);

        return report;
    }

    static void MapButtons(GamepadButtons buttons, bool isSwitchXY, SwitchReport report)
    {
        if (isSwitchXY)
        {
            if (buttons.HasFlag(GamepadButtons.A)) report.Button |= (ushort)SwitchButton.B;
            if (buttons.HasFlag(GamepadButtons.B)) report.Button |= (ushort)SwitchButton.A;
            if (buttons.HasFlag(GamepadButtons.X)) report.Button |= (ushort)SwitchButton.Y;
            if (buttons.HasFlag(GamepadButtons.Y)) report.Button |= (ushort)SwitchButton.X;
        }
        else
        {
            if (buttons.HasFlag(GamepadButtons.A)) report.Button |= (ushort)SwitchButton.A;
            if (buttons.HasFlag(GamepadButtons.B)) report.Button |= (ushort)SwitchButton.B;
            if (buttons.HasFlag(GamepadButtons.X)) report.Button |= (ushort)SwitchButton.X;
            if (buttons.HasFlag(GamepadButtons.Y)) report.Button |= (ushort)SwitchButton.Y;
        }

        if (buttons.HasFlag(GamepadButtons.LeftShoulder)) report.Button |= (ushort)SwitchButton.L;
        if (buttons.HasFlag(GamepadButtons.RightShoulder)) report.Button |= (ushort)SwitchButton.R;
        if (buttons.HasFlag(GamepadButtons.LeftThumb)) report.Button |= (ushort)SwitchButton.LCLICK;
        if (buttons.HasFlag(GamepadButtons.RightThumb)) report.Button |= (ushort)SwitchButton.RCLICK;
        if (buttons.HasFlag(GamepadButtons.Start)) report.Button |= (ushort)SwitchButton.PLUS;
        if (buttons.HasFlag(GamepadButtons.Back)) report.Button |= (ushort)SwitchButton.MINUS;
    }

    static void MapTriggers(float left, float right, float threshold, SwitchReport report)
    {
        if (left > threshold) report.Button |= (ushort)SwitchButton.ZL;
        if (right > threshold) report.Button |= (ushort)SwitchButton.ZR;
    }

    static void MapHat(GamepadButtons buttons, SwitchReport report)
    {
        var up = buttons.HasFlag(GamepadButtons.DPadUp);
        var down = buttons.HasFlag(GamepadButtons.DPadDown);
        var left = buttons.HasFlag(GamepadButtons.DPadLeft);
        var right = buttons.HasFlag(GamepadButtons.DPadRight);

        report.HAT = (up, down, left, right) switch
        {
            (true, false, false, false) => (byte)SwitchHAT.TOP,
            (false, true, false, false) => (byte)SwitchHAT.BOTTOM,
            (false, false, true, false) => (byte)SwitchHAT.LEFT,
            (false, false, false, true) => (byte)SwitchHAT.RIGHT,
            (true, false, true, false) => (byte)SwitchHAT.TOP_LEFT,
            (true, false, false, true) => (byte)SwitchHAT.TOP_RIGHT,
            (false, true, true, false) => (byte)SwitchHAT.BOTTOM_LEFT,
            (false, true, false, true) => (byte)SwitchHAT.BOTTOM_RIGHT,
            _ => (byte)SwitchHAT.CENTER,
        };
    }

    static (byte x, byte y) MapStick(Vector2 stick)
    {
        return (
            (byte)Math.Clamp((int)(stick.X * 127) + 128, 0, 255),
            (byte)Math.Clamp((int)(-stick.Y * 127) + 128, 0, 255)
        );
    }
}