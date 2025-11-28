namespace EC.Script.Text;

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
    // public int StartLine => Text.GetLineIndex(Span.Start);
    // public int StartCharacter => Span.Start - Text.Lines[StartLine].Start;
    // public int EndLine => Text.GetLineIndex(Span.End);
    // public int EndCharacter => Span.End - Text.Lines[EndLine].Start;
}