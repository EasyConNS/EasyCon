namespace EasyCon.Script2.Text;

public sealed class TextLine
{
    public string Text { get; init; } = string.Empty;
    public int Start { get; init; }
    public int Length { get; init; }
    public int End => Start + Length;
    public uint Line { get; init; } = 0;
}