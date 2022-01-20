using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Lexers
{
    internal class ConstantParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.Constant}\s*=\s*(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                args.Formatter.Constants[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                return new Statements.Empty($"{m.Groups[1].Value} = {m.Groups[2].Value}");
            }
            return null;
        }
    }
}
