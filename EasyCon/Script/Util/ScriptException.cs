using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    public class ScriptException : Exception
    {
        public int Address { get; private set; }

        public ScriptException(string message, int address)
            : base(message)
        {
            Address = address;
        }
    }
}
