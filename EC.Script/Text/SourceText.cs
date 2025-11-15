using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EC.Script.Text;

public sealed partial class SourceText
{
    private readonly string _text;
    public ImmutableArray<TextLine> Lines { get; init; }
    public string FileName { get; init; }
    public override string ToString() => _text;

    private SourceText(string text, string fileName)
    {
        _text = text;
        FileName = fileName;
        Lines = ParseLines(this, text);
    }

    public static SourceText From(string text, string fileName = "")
    {
        return new SourceText(text, fileName);
    }

    private static ImmutableArray<TextLine> ParseLines(SourceText sourceText, string text)
    {
        var result = ImmutableArray.CreateBuilder<TextLine>();
        var lines = Regex.Split(text, @"[\u000D\u000A\u0085\u2028\u2029]|\r\n");
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
            AddLine(result, sourceText, curline, comment, linecount++);
        }
        return result.ToImmutable();
    }
    private static void AddLine(ImmutableArray<TextLine>.Builder result, SourceText sourceText, string text, string comment, uint index)
    {
        var line = new TextLine {Text = text,Comment = comment, Line = index };
        result.Add(line);
    }

    [GeneratedRegex(@"(\s*#.*)$")]
    private static partial Regex LineRegex();
}
