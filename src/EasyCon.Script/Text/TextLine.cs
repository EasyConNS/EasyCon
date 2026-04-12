namespace EasyCon.Script.Text;

public sealed class TextLine
{
    public required SourceText SrcText { get; init; }
    public string Text => SrcText.ToString(Span);
    public int Start { get; init; }
    public int Length { get; init; }
    public int End => Start + Length;
    public int LengthIncludingLineBreak { get; init; }
    public SourceSpan Span => new(Start, Length);
    public SourceSpan SpanIncludingLineBreak => new(Start, LengthIncludingLineBreak);
}