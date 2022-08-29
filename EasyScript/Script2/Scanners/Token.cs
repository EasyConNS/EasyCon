namespace Compiler.Scanners;

public record Token : IEquatable<Token>
{
    public int Index { get; init; }
    public string Description { get; init; }

    public Token(string description, int index)
    {
        Index = index;
        Description = description;
    }

    public override int GetHashCode()
    {
        return Index;
    }

    public override string ToString()
    {
        return Description;
    }
}