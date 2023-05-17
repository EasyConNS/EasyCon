using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers;

internal class AmiiboParser : IStatementParser
{
    Statement? IStatementParser.Parse(ParserArgument args)
    {
        if (args.Text.Equals("AMIIBO_RESET", StringComparison.OrdinalIgnoreCase))
            return new AmiiboReset();
        var m = Regex.Match(args.Text, $@"amiibo\s*{Formats.Value}$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var amiiboidx = args.Formatter.GetValueEx(m.Groups[1].Value);
            return new AmiiboChanger(amiiboidx);
        }
        return null;
    }
}