using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers
{
    internal static class IfElseParser
    {
        public static void Init()
        {
            KeywordLexer.Register("if", ParseIf);
            KeywordLexer.Register("elif", ParseElif);
            KeywordLexer.Register("else", ElseParse);
            KeywordLexer.Register("endif", EndParse);
        }

        readonly static string OP_REGEX = "(" + CompareOperator.All.Select(op => op.Operator).Aggregate((a, b) => $"{a}|{b}") + ")";
        private readonly static Regex IF_STRING = new($@"^if\s+{Formats.VariableEx}\s*{OP_REGEX}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        private readonly static Regex ELIF_STRING = new($@"^elif\s+{Formats.VariableEx}\s*{OP_REGEX}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);

        private static Statement ParseIf(ParserArgument args)
        {
            var m = IF_STRING.Match(args.Text);
            if (m.Success)
            {
                return new If(CompareOperator.AllDict[m.Groups[2].Value], args.Formatter.GetVar(m.Groups[1].Value), args.Formatter.GetValueEx(m.Groups[3].Value));
            }
            throw new ParseException("格式错误", args.Address);
        }

        private static Statement ParseElif(ParserArgument args)
        {
            var m = ELIF_STRING.Match(args.Text);
            if (m.Success)
            {
                return new ElseIf(CompareOperator.AllDict[m.Groups[2].Value], args.Formatter.GetVar(m.Groups[1].Value), args.Formatter.GetValueEx(m.Groups[3].Value));
            }
            throw new ParseException("格式错误", args.Address);
        }

        //static Statement? ParseIf2(ParserArgument args)
        //{
        //    foreach (var op in CompareOperator.All)
        //    {
        //        var m = Regex.Match(args.Text, $@"^if\s+{Formats.VariableEx}\s*{op.Operator}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        //        if (m.Success)
        //            return new If(op, args.Formatter.GetVar(m.Groups[1].Value), args.Formatter.GetValueEx(m.Groups[2].Value));
        //        // else if
        //        m = Regex.Match(args.Text, $@"^elif\s+{Formats.VariableEx}\s*{op.Operator}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        //        if (m.Success)
        //            return new ElseIf(op, args.Formatter.GetVar(m.Groups[1].Value), args.Formatter.GetValueEx(m.Groups[2].Value));
        //    }
        //    return ElseParse(args) ?? EndParse(args);
        //}

        private static Statement ElseParse(ParserArgument args)
        {
            return new Else();
        }

        private static Statement EndParse(ParserArgument args)
        {
            return new EndIf();
        }
    }
}
