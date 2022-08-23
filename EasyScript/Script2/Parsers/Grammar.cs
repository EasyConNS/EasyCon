using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Compiler.Scanners;

namespace Compiler.Parsers;

public static class Grammar
{
    public static ProductionBase<Lexeme> AsTerminal(this Token token)
    {
        CodeContract.RequiresArgumentNotNull(token, "token");

        return Terminal.GetTerminal(token);
    }

    public static ProductionBase<T> Empty<T>(T value)
    {
        return new EmptyProduction<T>(value);
    }

    public static ProductionBase<TResult> Select<TSource, TResult>(this ProductionBase<TSource> production, Func<TSource, TResult> selector)
    {
        CodeContract.RequiresArgumentNotNull(production, "production");
        CodeContract.RequiresArgumentNotNull(selector, "selector");

        return new MappingProduction<TSource, TResult>(production, selector);
    }

    public static ProductionBase<TResult> Select<TResult>(this Token token, Func<Lexeme, TResult> selector)
    {
        CodeContract.RequiresArgumentNotNull(token, "token");
        CodeContract.RequiresArgumentNotNull(selector, "selector");

        return new MappingProduction<Lexeme, TResult>(token.AsTerminal(), selector);
    }

    public static ProductionBase<TResult> SelectMany<T1, T2, TResult>(this ProductionBase<T1> production, Func<T1, ProductionBase<T2>> productionSelector, Func<T1, T2, TResult> resultSelector)
    {
        CodeContract.RequiresArgumentNotNull(production, "production");
        CodeContract.RequiresArgumentNotNull(productionSelector, "productionSelector");
        CodeContract.RequiresArgumentNotNull(resultSelector, "resultSelector");

        return new ConcatenationProduction<T1, T2, TResult>(production, productionSelector, resultSelector);
    }

    public static ProductionBase<TResult> SelectMany<T2, TResult>(this Token token, Func<Lexeme, ProductionBase<T2>> productionSelector, Func<Lexeme, T2, TResult> resultSelector)
    {
        CodeContract.RequiresArgumentNotNull(token, "token");
        CodeContract.RequiresArgumentNotNull(productionSelector, "productionSelector");
        CodeContract.RequiresArgumentNotNull(resultSelector, "resultSelector");

        return new ConcatenationProduction<Lexeme, T2, TResult>(token.AsTerminal(), productionSelector, resultSelector);
    }

    public static ProductionBase<TResult> SelectMany<T1, TResult>(this ProductionBase<T1> production, Func<T1, Token> productionSelector, Func<T1, Lexeme, TResult> resultSelector)
    {
        CodeContract.RequiresArgumentNotNull(production, "production");
        CodeContract.RequiresArgumentNotNull(productionSelector, "productionSelector");
        CodeContract.RequiresArgumentNotNull(resultSelector, "resultSelector");

        return new ConcatenationProduction<T1, Lexeme, TResult>(production, v => productionSelector(v).AsTerminal(), resultSelector);
    }

    public static ProductionBase<TResult> SelectMany<TResult>(this Token token, Func<Lexeme, Token> productionSelector, Func<Lexeme, Lexeme, TResult> resultSelector)
    {
        CodeContract.RequiresArgumentNotNull(token, "token");
        CodeContract.RequiresArgumentNotNull(productionSelector, "productionSelector");
        CodeContract.RequiresArgumentNotNull(resultSelector, "resultSelector");

        return new ConcatenationProduction<Lexeme, Lexeme, TResult>(token.AsTerminal(), v => productionSelector(v).AsTerminal(), resultSelector);
    }
}