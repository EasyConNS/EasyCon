using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers
{
#if DEBUG
    internal class DebugParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, @"^sprint\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new SerialPrint(uint.Parse(m.Groups[1].Value), false);
            m = Regex.Match(args.Text, @"^smem\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new SerialPrint(uint.Parse(m.Groups[1].Value), true);
            return null;
        }
    }
#endif

    internal class ExtParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            if (args.Text.Equals("PUSHALL", StringComparison.OrdinalIgnoreCase))
                return new PushAll();
            if (args.Text.Equals("POPALL", StringComparison.OrdinalIgnoreCase))
                return new PopAll();
            return null;
        }
    }
}
