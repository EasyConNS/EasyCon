using System.Text.RegularExpressions;
using ECScript.Text;

namespace ECScript.Syntax;

internal sealed partial class Lexer
{
    private readonly List<string> _diagnostics = [];
    private readonly string _text;

    public Lexer(string text)
    {
        _text = text;
    }

    public IEnumerable<TextLine> LexLine()
    {
        var lines = Regex.Split(_text, @"[\u000D\u000A\u0085\u2028\u2029]|\r\n");
        foreach (var line in lines)
        {
            uint linecount = 0;
            var curline = line.TrimStart();
            var comment = string.Empty;
            var m = LineRegex().Match(curline);
            if (m.Success)
            {
                comment = m.Groups[1].Value;
                curline = curline[..^comment.Length];
            }
            else
            {
                curline = curline.Trim();
            }
            yield return new TextLine
            {
                Text = curline,
                Comment = comment,
                Line = linecount++,
            };
        }
    }

    [GeneratedRegex(@"(\s*#.*)$")]
    private static partial Regex LineRegex();
}
 