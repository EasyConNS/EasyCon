using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class Processor
    {
        public IOutputAdapter Output;
        public int PC = 0;
        public Stack<ForLoop> LoopStack = new Stack<ForLoop>();
        public Dictionary<ForLoop, int> LoopVar = new Dictionary<ForLoop, int>();
        public Dictionary<string, ECValue> Variables = new Dictionary<string, ECValue>();
    }
}
