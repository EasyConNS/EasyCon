namespace EasyCon2.Global
{
    class Board
    {
        public readonly string DisplayName;
        public readonly string CoreName;
        public readonly int Version = 0x46;
        public readonly int DataSize;

        public static Board[] SupportedBoards = new Board[] {
            new ("Arduino UNO R3", "UNO", 412),
            new ("Beetle", "Beetle", 924),
            new ("Leonardo", "Leonardo", 924),
            new("Teensy 2.0", "Teensy2", 924),
            new("Teensy 2.0++", "Teensy2pp", 3996),
            };

        public Board(string displayname, string corename, int datasize)
        {
            DisplayName = displayname;
            CoreName = corename;
            DataSize = datasize;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
