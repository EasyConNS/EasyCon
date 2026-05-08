using EasyDevice;
using SDL;

namespace EasyCon.SDLInput;

public static unsafe class SdlGamepadMapper
{
    public static SwitchReport Map(SDL_Gamepad* gamepad, bool isSwitchXY = true, float triggerThreshold = 0.1f)
    {
        var report = new SwitchReport();
        MapButtons(gamepad, isSwitchXY, report);
        MapTriggers(gamepad, triggerThreshold, report);
        MapHat(gamepad, report);
        (report.LX, report.LY) = MapStick(
            SDL3.SDL_GetGamepadAxis(gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX),
            SDL3.SDL_GetGamepadAxis(gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY));
        (report.RX, report.RY) = MapStick(
            SDL3.SDL_GetGamepadAxis(gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX),
            SDL3.SDL_GetGamepadAxis(gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY));
        return report;
    }

    static void MapButtons(SDL_Gamepad* gamepad, bool isSwitchXY, SwitchReport report)
    {
        var south = SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);
        var east = SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST);
        var west = SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST);
        var north = SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH);

        if (isSwitchXY)
        {
            if ((bool)south) report.Button |= (ushort)SwitchButton.B;
            if ((bool)east) report.Button |= (ushort)SwitchButton.A;
            if ((bool)west) report.Button |= (ushort)SwitchButton.Y;
            if ((bool)north) report.Button |= (ushort)SwitchButton.X;
        }
        else
        {
            if ((bool)south) report.Button |= (ushort)SwitchButton.A;
            if ((bool)east) report.Button |= (ushort)SwitchButton.B;
            if ((bool)west) report.Button |= (ushort)SwitchButton.X;
            if ((bool)north) report.Button |= (ushort)SwitchButton.Y;
        }

        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER))
            report.Button |= (ushort)SwitchButton.L;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER))
            report.Button |= (ushort)SwitchButton.R;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK))
            report.Button |= (ushort)SwitchButton.LCLICK;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK))
            report.Button |= (ushort)SwitchButton.RCLICK;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START))
            report.Button |= (ushort)SwitchButton.PLUS;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK))
            report.Button |= (ushort)SwitchButton.MINUS;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC1))
            report.Button |= (ushort)SwitchButton.CAPTURE;
        if ((bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE))
            report.Button |= (ushort)SwitchButton.HOME;
    }

    static void MapTriggers(SDL_Gamepad* gamepad, float threshold, SwitchReport report)
    {
        var leftAxis = SDL3.SDL_GetGamepadAxis(gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
        var rightAxis = SDL3.SDL_GetGamepadAxis(gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
        // SDL3 axis: -32768..32767, trigger is 0..32767 in practice
        float left = (float)leftAxis / 32767f;
        float right = (float)rightAxis / 32767f;
        if (left > threshold) report.Button |= (ushort)SwitchButton.ZL;
        if (right > threshold) report.Button |= (ushort)SwitchButton.ZR;
    }

    static void MapHat(SDL_Gamepad* gamepad, SwitchReport report)
    {
        var up = (bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP);
        var down = (bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN);
        var left = (bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT);
        var right = (bool)SDL3.SDL_GetGamepadButton(gamepad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT);

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

    static (byte x, byte y) MapStick(short axisX, short axisY)
    {
        return (
            (byte)Math.Clamp((int)(axisX * 127L / 32767) + 128, 0, 255),
            (byte)Math.Clamp((int)(axisY * 127L / 32767) + 128, 0, 255)
        );
    }
}