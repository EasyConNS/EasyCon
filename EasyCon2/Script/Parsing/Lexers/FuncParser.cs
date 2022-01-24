using EasyCon2.Script.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Lexers
{
    internal class FuncParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            return WaitParse(args);
        }

        private static Statement WaitParse(ParserArgument args)
        {
            if (int.TryParse(args.Text, out int duration))
                return new Wait(duration, true);
            var m = Regex.Match(args.Text, $@"^wait\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Wait(args.Formatter.GetValueEx(m.Groups[1].Value));
            return FuncParse(args);
        }

        private static Statement FuncParse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, @"^func\s+(\D[\d\p{L}_]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Function(m.Groups[1].Value);
            m = Regex.Match(args.Text, @"^call\s+(\D[\d\p{L}_]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new CallStat(m.Groups[1].Value);
            if (args.Text.Equals("ret", StringComparison.OrdinalIgnoreCase))
                return new ReturnStat();
            return TimestampParse(args);
        }

        private static Statement TimestampParse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^TIME\s+{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return new TimeStamp(FormatterUtil.GetRegEx(m.Groups[1].Value));
            }
            return null;
        }
    }
}
