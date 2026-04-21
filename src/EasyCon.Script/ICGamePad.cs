namespace EasyScript;

public interface ICGamePad
{
    abstract DelayType DelayMethod { get; }
    void ClickButtons(GamePadKey key, int duration, CancellationToken token);
    void PressButtons(GamePadKey key);
    void ReleaseButtons(GamePadKey key);
    void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token);
    void SetStick(GamePadKey key, byte x, byte y);
    void ChangeAmiibo(uint index);
}

public enum GamePadKey : uint
{
    None = 0,
    Y = 1,
    B = 2,
    A = 3,
    X = 4,
    L = 5,
    R = 6,
    ZL = 7,
    ZR = 8,
    MINUS = 9,
    PLUS = 10,
    LCLICK = 11,
    RCLICK = 12,
    HOME = 13,
    CAPTURE = 14,
    TOP = 16,
    TOP_RIGHT = 17,
    RIGHT = 18,
    DOWN_RIGHT = 19,
    DOWN = 20,
    DOWN_LEFT = 21,
    LEFT = 22,
    TOP_LEFT = 23,
    LS = 32,
    RS = 33,
}

public enum DelayType : byte
{
    Normal,
    LowCPU,
    HighResolution,
}