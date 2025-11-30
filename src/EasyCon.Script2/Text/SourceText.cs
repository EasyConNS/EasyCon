using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon.Script2.Text;

public sealed class SourceText
{
    private readonly string _text;

    public ImmutableArray<TextLine> Lines { get; init; }
    public char this[int index] => _text[index];
    public int Length => _text.Length;
    public string FileName { get; init; }
    public override string ToString() => _text;
    public string ToString(int start, int length) => _text.Substring(start, length);
    public string ToString(SourceSpan span) => ToString(span.Start, span.Length);

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
            var curline = line.Trim();
            AddLine(result, sourceText, curline, linecount++);
        }
        return result.ToImmutable();
    }
    private static void AddLine(ImmutableArray<TextLine>.Builder result, SourceText sourceText, string text, uint index)
    {
        var line = new TextLine {Text = text, Line = index };
        result.Add(line);
    }
}
