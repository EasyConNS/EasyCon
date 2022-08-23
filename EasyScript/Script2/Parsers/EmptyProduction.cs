namespace Compiler.Parsers;

public class EmptyProduction<T> : ProductionBase<T>
{
    public EmptyProduction(T value)
    {
        Value = value;
    }

    public T Value { get; private set; }

    public override string DebugName
    {
        get
        {
            return "E" + DebugNameSuffix;
        }
    }

    public override TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument)
    {
        return visitor.VisitEmpty(this, argument);
    }

    public override string ToString()
    {
        return DebugName + " ::= ε";
    }
}