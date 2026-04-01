namespace EasyCon.Script.Text;

public record SourceSpan : IEquatable<SourceSpan>
{
    public SourceSpan(int start, int len)
    {
        Start = start;
        Length = len;
    }

    public int Start { get; internal set; }
    public int Length { get; internal set; }
    public int End => Start + Length;

    public static SourceSpan FromBounds(int start, int end)
    {
        var length = end - start;
        return new SourceSpan(start, length);
    }
    public bool OverlapsWith(SourceSpan span)
    {
        return Start < span.End &&
                End > span.Start;
    }
    public override string ToString() => $"{Start}..{End}";
}