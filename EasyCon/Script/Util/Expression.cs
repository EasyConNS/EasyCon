using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    abstract class Expression
    {
        public const string Pattern = @"[\s\d\p{L}_+\-*/%&]*";

        public abstract ECValue Eval(Processor processor);

        public static Expression Parse(string text)
        {
            throw new NotImplementedException();
        }
    }
}
