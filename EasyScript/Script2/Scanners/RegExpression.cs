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
}