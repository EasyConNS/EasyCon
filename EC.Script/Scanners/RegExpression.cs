using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public abstract class RegExpression
{
    public abstract Match Match(string input);
    public abstract string[] Split(string input);

    public static RegExpression Simple(string literal)
    {
        return new SimpleExpression(literal);
    }

    public static RegExpression IgnoreCase(string literal)
    {
        return Simple(literal.ToLower()) | Simple(literal.ToUpper());
    }

    public static RegExpression operator|(RegExpression left, RegExpression right)
    {
        return new AlternationExpression(left, right);
    }

    public static RegExpression operator >>(RegExpression left, RegExpression right)
    {
        return new ConcatenationExpression(left, right);
    }

    public RegExpression PackBy(string a)
    {
        return Simple(a + ToString() + a);
    }

    public RegExpression Concat(RegExpression follow)
    {
        return new ConcatenationExpression(this, follow);
    }

    [SpecialName]
    public static RegExpression op_Concatenate(RegExpression first, RegExpression follow)
    {
        return new ConcatenationExpression(first, follow);
    }
}