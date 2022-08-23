using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers
{
    internal class MsgParser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            return AlertParse(args) ?? PrintParse(args);
        }

        private static Statement AlertParse(ParserArgument args)
        {
            if (args.Text.Equals("alert", StringComparison.OrdinalIgnoreCase))
                return new Alert(Array.Empty<Content>());
            var m = Regex.Match(args.Text, @"^alert\s+(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var contents = ParseContents(m.Groups[1].Value, args.Formatter, out _);
                return new Alert(contents.ToArray());
            }
            return null;
        }

        private static Statement PrintParse(ParserArgument args)
        {
            if (args.Text.Equals("print", StringComparison.OrdinalIgnoreCase))
                return new Print(Array.Empty<Content>(), false);
            var m = Regex.Match(args.Text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var contents = ParseContents(m.Groups[1].Value, args.Formatter, out bool cancellinebreak);
                return new Print(contents.ToArray(), cancellinebreak);
            }
            return null;
        }

        private static List<Content> ParseContents(string text, Formatter formatter, out bool cancellinebreak)
        {
            var contents = new List<Content>();
            cancellinebreak = false;

            var strs = text.Split('&');
            for (var i = 0; i < strs.Length; i++)
            {
                var s = strs[i].Trim();
                if (i == strs.Length - 1 && s == "\\")
                {
                    contents.Add(new TextContent("", "\\"));
                    cancellinebreak = true;
                    continue;
                }
                if (s.Length == 0 && strs.Length > 1)
                {
                    contents.Add(new TextContent(" ", ""));
                    continue;
                }
                var m = Regex.Match(s, Formats.RegisterEx_F);
                if (m.Success)
                {
                    contents.Add(new RegContent(FormatterUtil.GetRegEx(s)));
                    continue;
                }
                m = Regex.Match(s, Formats.Constant_F);
                if (m.Success)
                {
                    var v = formatter.GetConstant(s);
                    contents.Add(new TextContent(v.Val.ToString(), s));
                    continue;
                }
                contents.Add(new TextContent(s));
            }

            return contents;
        }
    }
}
