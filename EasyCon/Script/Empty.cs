using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class Empty : Statement
    {
        public static Statement TryCompile(string text)
        {
            if (text.Length == 0)
                return new Empty();
            return null;
        }

        protected override string _ToString()
        {
            return string.Empty;
        }

        public override void Exec(Processor processor)
        { }
    }
}
