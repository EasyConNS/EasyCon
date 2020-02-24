using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon
{
    class Board
    {
        public readonly string DisplayName;
        public readonly string CoreName;
        public readonly int Version;
        public readonly int DataSize;

        public Board(string displayname, string corename, int version, int datasize)
        {
            DisplayName = displayname;
            CoreName = corename;
            Version = version;
            DataSize = datasize;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
