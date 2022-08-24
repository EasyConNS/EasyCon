using Compiler.Scanners;

namespace Compiler.Parsers;

public class EndOfStream : ProductionBase<Lexeme>
{
    private static EndOfStream s_instance = new EndOfStream();

    private EndOfStream(){}

    public static EndOfStream Instance => s_instance;

    public override bool IsTerminal => true;

    public override bool IsEos => true;

    public override string DebugName => "$";

    public override TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument)
    {
        return visitor.VisitEndOfStream(this, argument);
    }

    public override bool Equals(object obj)
    {
        var otherEos = obj as EndOfStream;

        if (otherEos != null)
        {
            return true;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return -3177;
    }

    public override string ToString()
    {
        return "$";
    }
}