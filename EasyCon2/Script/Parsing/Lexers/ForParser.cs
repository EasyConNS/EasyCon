using EasyCon2.Script.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Lexers
{
    internal class ForParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            if (args.Text.Equals("for", StringComparison.OrdinalIgnoreCase))
                return new For_Infinite();
            var m = Regex.Match(args.Text, $@"^for\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new For_Static(args.Formatter.GetValueEx(m.Groups[1].Value));
            m = Regex.Match(args.Text, $@"^for\s+{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}\s*to\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new For_Full(FormatterUtil.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value), args.Formatter.GetValueEx(m.Groups[3].Value));
            // next
            return NextParse(args) ?? LoopControlParse(args);
        }

        private static Statement NextParse(ParserArgument args)
        {
            if (args.Text.Equals("next", StringComparison.OrdinalIgnoreCase))
                return new Next();
            return null;
        }

        private static Statement LoopControlParse(ParserArgument args)
        {
            // break
            if (args.Text.Equals("break", StringComparison.OrdinalIgnoreCase))
                return new Break();
            var m = Regex.Match(args.Text, $@"^break\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Break(args.Formatter.GetInstant(m.Groups[1].Value, true));

            // continue
            if (args.Text.Equals("continue", StringComparison.OrdinalIgnoreCase))
                return new Continue();
            m = Regex.Match(args.Text, $@"^continue\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Continue(args.Formatter.GetInstant(m.Groups[1].Value, true));
            return null;
        }
    }
}
