namespace EasyCon2.Global
{
    class Board
    {
        public readonly string DisplayName;
        public readonly string CoreName;
        public readonly int Version = 0x45;
        public readonly int DataSize;

        public static Board[] SupportedBoards = new Board[] {
            new ("Arduino UNO R3", "UNO", 400),
            new ("Beetle", "Beetle", 900),
            new ("Leonardo", "Leonardo", 900),
            new("Teensy 2.0", "Teensy2", 900),
            new("Teensy 2.0++", "Teensy2pp", 900),
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
