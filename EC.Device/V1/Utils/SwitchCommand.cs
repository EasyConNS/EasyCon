using System;

namespace EC.Device;

public static class SwitchStick
{
    public const byte STICK_MIN = 0;
    public const byte STICK_CENMIN = 64;
    public const byte STICK_CENTER = 128;
    public const byte STICK_CENMAX = 192;
    public const byte STICK_MAX = 255;
}

[Flags]
public enum SwitchButton
{
    Y = 0x01,
    B = 0x02,
    A = 0x04,
    X = 0x08,
    L = 0x10,
    R = 0x20,
    ZL = 0x40,
    ZR = 0x80,
    MINUS = 0x100,
    PLUS = 0x200,
    LCLICK = 0x400,
    RCLICK = 0x800,
    HOME = 0x1000,
    CAPTURE = 0x2000,
}

public enum SwitchHAT
{
    TOP = 0x00,
    TOP_RIGHT = 0x01,
    RIGHT = 0x02,
    BOTTOM_RIGHT = 0x03,
    BOTTOM = 0x04,
    BOTTOM_LEFT = 0x05,
    LEFT = 0x06,
    TOP_LEFT = 0x07,
    CENTER = 0x08,
}

[Flags]
public enum DirectionKey
{
    None = 0x0,
    Up = 0x1,
    Down = 0x2,
    Left = 0x4,
    Right = 0x8,
}

