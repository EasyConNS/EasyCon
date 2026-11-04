namespace EasyCon.Script.Text;

public record TextLocation
{
    public TextLocation(SourceText text, SourceSpan span)
    {
        Text = text;
        Span = span;
    }

    public SourceText Text { get; }
    public SourceSpan Span { get; }

    public string FileName => Text.FileName;
    public int StartLine => Text.GetLineIndex(Span.Start);
    public int EndLine => Text.GetLineIndex(Span.End);

    /// <summary>起始列（0 基；LSP/JSON 诊断定位用，M1 补齐）。</summary>
    public int StartCharacter
    {
        get
        {
            var line = Text.Lines[StartLine];
            return Math.Max(0, Span.Start - line.Start);
        }
    }

    /// <summary>结束列（0 基，不含）。</summary>
    public int EndCharacter
    {
        get
        {
            var line = Text.Lines[EndLine];
            return Math.Max(0, Span.End - line.Start);
        }
    }
}