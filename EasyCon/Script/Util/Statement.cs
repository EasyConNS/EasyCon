using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace EasyCon.Script
{
    abstract class Statement
    {
        public int Address = -1;
        public string Indent;
        public string Comment;
        public delegate Statement Generator(string text);

        public abstract void Exec(Processor processor);

        public override sealed string ToString()
        {
            return $"{Indent}{_ToString()}{Comment}";
        }

        protected abstract string _ToString();
    }
}
