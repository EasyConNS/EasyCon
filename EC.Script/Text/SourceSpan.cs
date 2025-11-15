namespace EC.Script.Text;

public record SourceSpan : IEquatable<SourceSpan>
{
    public SourceSpan(int start, int len, int row)
    {
        Start = start;
        Length = len;
        Line = row;
    }

    public int Start { get; internal set; }
    public int Line { get; internal set; }
    public int End => Start + Length;
    public int Length { get; internal set; }

    public static SourceSpan FromBounds(int start, int end, int row)
    {
        var length = end - start;
        return new SourceSpan(start, length, row);
    }
}