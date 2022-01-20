using EasyCon2.Script.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Lexers
{
    internal class IfElseParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            foreach (var op in CompareOperator.All)
            {
                var m = Regex.Match(args.Text, $@"^if\s+{Formats.VariableEx}\s*{op.Operator}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return new If(op, args.Formatter.GetVar(m.Groups[1].Value), args.Formatter.GetValueEx(m.Groups[2].Value));
            }
            return ElseParse(args);
        }

        private static Statement ElseParse(ParserArgument args)
        {
            if (args.Text.Equals("else", StringComparison.OrdinalIgnoreCase))
                return new Else();
            return EndParse(args);
        }

        private static Statement EndParse(ParserArgument args)
        {
            if (args.Text.Equals("endif", StringComparison.OrdinalIgnoreCase))
                return new EndIf();
            return null;
        }
    }
}
