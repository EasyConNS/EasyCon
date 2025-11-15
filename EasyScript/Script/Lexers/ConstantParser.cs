using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers;

internal class ConstantParser : IStatementParser
{
    Statement? IStatementParser.ParseWildcard(ParserArgument args)
    {
        var m = Regex.Match(args.Text, $@"^{Formats.Constant}\s*=\s*{Formats.Instant}$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var val = args.Formatter.GetInstant(m.Groups[2].Value);
            args.Formatter.SetConstantTable(m.Groups[1].Value, val.Val);
            return new Statements.Empty($"{m.Groups[1].Value} = {m.Groups[2].Value}");
        }
        return null;
    }
}
