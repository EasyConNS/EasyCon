using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using EasyCon.Script.Assembly;

namespace EasyCon.Script.Parsing
{
    abstract class Statement
    {
        public int Address = -1;
        public string Indent;
        public string Comment;

        public abstract void Exec(Processor processor);

        public abstract void Assemble(Assembler assembler);

        public string GetString(Formats.Formatter formatter)
        {
            return $"{Indent}{_GetString(formatter)}{Comment}";
        }

        protected abstract string _GetString(Formats.Formatter formatter);
    }
}
