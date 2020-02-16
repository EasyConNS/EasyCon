using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class SerialPrint : Statement
    {
        public readonly uint Value;
        public readonly bool Mem;

        public SerialPrint(uint value, bool mem)
        {
            Value = value;
            Mem = mem;
        }

#if DEBUG
        public static Statement TryCompile(string text)
        {
            var m = Regex.Match(text, @"^sprint\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new SerialPrint(uint.Parse(m.Groups[1].Value), false);
            m = Regex.Match(text, @"^smem\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new SerialPrint(uint.Parse(m.Groups[1].Value), true);
            return null;
        }
#endif

        protected override string _ToString()
        {
            return Mem ? $"SMEM {Value}" : $"SPRINT {Value}";
        }

        public override void Exec(Processor processor)
        {
            //processor.Output.Print(Value.ToString());
        }
    }
}
