namespace EasyCon.Core.Config;

public record KeyMappingConfig
{
    public int A { get; set; } = 76;       // Keys.L
    public int B { get; set; } = 75;       // Keys.K
    public int X { get; set; } = 73;       // Keys.I
    public int Y { get; set; } = 74;       // Keys.J
    public int L { get; set; } = 71;       // Keys.G
    public int R { get; set; } = 84;       // Keys.T
    public int ZL { get; set; } = 70;      // Keys.F
    public int ZR { get; set; } = 82;      // Keys.R
    public int Plus { get; set; } = 107;   // Numpad Add
    public int Minus { get; set; } = 109;  // Numpad Subtract
    public int Capture { get; set; } = 90; // Keys.Z
    public int Home { get; set; } = 67;    // Keys.C
    public int LClick { get; set; } = 81;  // Keys.Q
    public int RClick { get; set; } = 69;  // Keys.E
    public int Up { get; set; } = 0;
    public int Down { get; set; } = 0;
    public int Left { get; set; } = 0;
    public int Right { get; set; } = 0;
    public int UpRight { get; set; } = 0;
    public int DownRight { get; set; } = 0;
    public int UpLeft { get; set; } = 0;
    public int DownLeft { get; set; } = 0;
    public int LSUp { get; set; } = 87;    // Keys.W
    public int LSDown { get; set; } = 83;  // Keys.S
    public int LSLeft { get; set; } = 65;  // Keys.A
    public int LSRight { get; set; } = 68; // Keys.D
    public int RSUp { get; set; } = 38;    // Keys.Up
    public int RSDown { get; set; } = 40;  // Keys.Down
    public int RSLeft { get; set; } = 37;  // Keys.Left
    public int RSRight { get; set; } = 39; // Keys.Right
}