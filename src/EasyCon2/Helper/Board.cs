namespace EasyCon2.Helper;

class Board(string displayname, string corename, int datasize)
{
    public readonly string DisplayName = displayname;
    public readonly string CoreName = corename;
    public readonly int Version = 0x45;
    public readonly int DataSize = datasize;

    public static Board[] SupportedBoards = [
        new ("Leonardo", "Leonardo", 924),
        new("Teensy 2.0", "Teensy2", 924),
        new("Teensy 2.0++", "Teensy2pp", 3996),
        new ("Beetle", "Beetle", 924),
        new ("Arduino UNO R3", "UNO", 412),
        new ("STM32F103C6T6", "STM32F103C6T6", 924),
        ];

    public override string ToString()
    {
        return DisplayName;
    }
}
