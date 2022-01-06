namespace EasyCon2.Global
{
    class Board
    {
        public readonly string DisplayName;
        public readonly string CoreName;
        public readonly int Version = 0x45;
        public readonly int DataSize;

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
