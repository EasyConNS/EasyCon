using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class ConcatenationExpression : RegExpression
{
    public ConcatenationExpression(RegExpression first, RegExpression follow)
    {
        First = first;
        Follow = follow;
    }

    public RegExpression First { get; init; }
    public RegExpression Follow { get; init; }

    public override string ToString()
    {
        return First.ToString() + Follow.ToString();
    }

    public override Match Match(string input)
    {
        var expression = new SimpleExpression(ToString());
        return expression.Match(input);
    }

    public override string[] Split(string input)
    {
        throw new Exception("ConcatenationExpression not support split");
    }
}