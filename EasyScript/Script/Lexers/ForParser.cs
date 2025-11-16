using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers;

internal static class ForParser
{
    public static void Init()
    {
        KeywordLexer.Register("for", ParseFor);
        KeywordLexer.Register("next", NextParse);
        KeywordLexer.Register("break", BreakParse);
        KeywordLexer.Register("continue", ContinueParse);
    }



    private static Statement ParseFor(ParserArgument args)
    {
        if (args.Text.Equals("for", StringComparison.OrdinalIgnoreCase))
            return new For_Infinite();
        var m = Regex.Match(args.Text, $@"^for\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new For_Static(args.Formatter.GetValueEx(m.Groups[1].Value));
        m = Regex.Match(args.Text, $@"^for\s+{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}\s*to\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new For_Full(FormatterUtil.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value), args.Formatter.GetValueEx(m.Groups[3].Value));
        
        throw new ParseException("不合法的for语句", args.Address);
    }

    private static Statement NextParse(ParserArgument args)
    {
        return new Next();
    }

    private static Statement BreakParse(ParserArgument args)
    {
        // break
        if (args.Text.Equals("break", StringComparison.OrdinalIgnoreCase))
            return new Break();
        var m = Regex.Match(args.Text, $@"^break\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Break(args.Formatter.GetInstant(m.Groups[1].Value, true));
        throw new ParseException("不合法的break语句", args.Address);
    }
    private static Statement ContinueParse(ParserArgument args)
    { 
        // continue
        if (args.Text.Equals("continue", StringComparison.OrdinalIgnoreCase))
            return new Continue();
        var m = Regex.Match(args.Text, $@"^continue\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Continue(args.Formatter.GetInstant(m.Groups[1].Value, true));
        throw new ParseException("不合法的continue语句", args.Address);
    }
}
