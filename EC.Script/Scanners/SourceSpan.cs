namespace Compiler;

public record SourceSpan : IEquatable<SourceSpan>
{
    public int Line { get; internal set; }
    public int Column { get; internal set; }
    public int CharIndex { get; internal set; }

    public SourceSpan(int pos, int col, int row)
    {
        CharIndex = pos;
        Column = col;
        Line = row;
    }
}