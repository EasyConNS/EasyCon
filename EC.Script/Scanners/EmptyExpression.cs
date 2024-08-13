using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Compiler.Scanners;

internal class EmptyExpression : RegExpression
{
    public override string ToString()
    {
        return "ε";
    }

    public override Match Match(string input)
    {
        throw new NotImplementedException();
    }

    public override string[] Split(string input)
    {
        throw new Exception("EmptyExpression not support split");
    }
}
