namespace EasyCon.Script.Text;

public sealed class TextLine
{
    public string Text { get; init; } = string.Empty;
    public int Start { get; init; }
    public int Length => Text.Length;
    public int End => Start + Length;
    public SourceSpan Span => new (Start, Length);
}