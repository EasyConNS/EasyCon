using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers;

internal static class AmiiboParser
{
    internal static void Init()
    {
        KeywordLexer.Register("amiibo", ParseAmiibo);
        KeywordLexer.Register("AMIIBO_RESET", ParseAmiiboReset);
    }

    private static Statement ParseAmiibo(ParserArgument args)
    {
        
        var m = Regex.Match(args.Text, $@"amiibo\s*{Formats.Value}$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var amiiboidx = args.Formatter.GetValueEx(m.Groups[1].Value);
            return new AmiiboChanger(amiiboidx);
        }
        throw new ParseException("Invalid amiibo statement", args.Address);
    }

    private static Statement ParseAmiiboReset(ParserArgument args)
    {
        if (args.Text.Equals("AMIIBO_RESET", StringComparison.OrdinalIgnoreCase))
            return new AmiiboReset();
        throw new ParseException("Invalid amiibo reset statement", args.Address);
    }
}