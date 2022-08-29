using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class AlternationExpression : RegExpression
{
    public AlternationExpression(RegExpression expression1, RegExpression expression2)
    {
        Expression1 = expression1;
        Expression2 = expression2;
    }

    public RegExpression Expression1 { get; init; }
    public RegExpression Expression2 { get; init; }

    public override string ToString()
    {
        return '(' + Expression1.ToString() + '|' + Expression2.ToString() +')';
    }

    public override Match Match(string input)
    {
        var m1 = Expression1.Match(input);
        var m2 = Expression2.Match(input);
        if(m1.Success)
        {
            if(m2.Success)
            {
                return (m2.Value.Length > m1.Value.Length) ? m2 : m1;
            }
            return m1;
        }
        return m2;
    }

    public override string[] Split(string input)
    {
        throw new Exception("AlternationExpression not support split");
    }
}