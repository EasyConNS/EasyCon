namespace Compiler.Parsers;

public abstract class ProductionBase : IProduction
{
    public abstract TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument);
}

public abstract class ProductionBase<T> : ProductionBase
{
    protected ProductionBase(){}
}

public interface IProduction
{
    TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument);
}