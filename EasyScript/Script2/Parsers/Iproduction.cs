namespace Compiler.Parsers;

public interface IProduction
{
    bool IsTerminal { get; }
    bool IsEos { get; }
    TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument);
}