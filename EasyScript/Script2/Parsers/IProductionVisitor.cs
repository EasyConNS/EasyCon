namespace Compiler.Parsers;

public interface IProductionVisitor<TArg, TResult>
{
    TResult VisitTerminal(Terminal terminal, TArg argument);

    TResult VisitMapping<TSource, TReturn>(MappingProduction<TSource, TReturn> mappingProduction, TArg argument);

    TResult VisitEmpty<T>(EmptyProduction<T> emptyProduction, TArg argument);

    TResult VisitAlternation<T>(AlternationProduction<T> alternationProduction, TArg argument);

    TResult VisitConcatenation<T1, T2, TR>(ConcatenationProduction<T1, T2, TR> concatenationProduction, TArg argument);
}