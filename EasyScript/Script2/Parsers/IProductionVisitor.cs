namespace Compiler.Parsers;

public interface IProductionVisitor<TArg, TResult>
{
    TResult Visit();
}