namespace Compiler.Parsers;

public class Production<T> : ProductionBase<T>
{
    private ProductionBase<T> m_rule;
    public ProductionBase<T> Rule => m_rule;

    public override TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument)
    {
        return Rule.Accept(visitor, argument);
    }
}