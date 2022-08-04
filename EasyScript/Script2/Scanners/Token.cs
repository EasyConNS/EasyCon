namespace Compiler.Scanners;

public record Token : IEquatable<Token>
{
    public int Index { get; private set; }
    public string Description { get; private set; }

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