using EasyCon.Script.Parsing.Statements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class Processor
    {
        public const uint RegisterCount = 8;

        public IOutputAdapter Output;
        public int PC = 0;
        public bool CancelLineBreak = false;
        public Stack<For> LoopStack = new Stack<For>();
        public Dictionary<For, int> LoopTime = new Dictionary<For, int>();
        public Dictionary<For, int> LoopCount = new Dictionary<For, int>();
        public short[] Register = new short[RegisterCount];
        public Stack<short> Stack = new Stack<short>();
    }
}
