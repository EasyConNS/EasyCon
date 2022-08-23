using System.Collections.Generic;

namespace Compiler.Parsers.Generator;

internal class ProductionAggregationVisitor : IProductionVisitor<List<IProduction>, List<IProduction>>
{
    List<IProduction> IProductionVisitor<List<IProduction>, List<IProduction>>.VisitTerminal(Terminal terminal, List<IProduction> Productions)
    {
        if (terminal.Info != null)
        {
            return Productions;
        }

        terminal.Info = new ProductionInfo();
        terminal.Info.First.Add(terminal);

        terminal.Info.Index = Productions.Count;
        terminal.Info.SymbolCount = 1;

        Productions.Add(terminal);

        return Productions;
    }

    List<IProduction> IProductionVisitor<List<IProduction>, List<IProduction>>.VisitMapping<TSource, TReturn>(MappingProduction<TSource, TReturn> mappingProduction, List<IProduction> Productions)
    {
        if (mappingProduction.Info != null)
        {
            return Productions;
        }

        mappingProduction.Info = new ProductionInfo();
        mappingProduction.SourceProduction.Accept(this, Productions);

        mappingProduction.Info.Index = Productions.Count;
        mappingProduction.Info.SymbolCount = 1;
        Productions.Add(mappingProduction);

        return Productions;
    }

    List<IProduction> IProductionVisitor<List<IProduction>, List<IProduction>>.VisitEmpty<T>(EmptyProduction<T> emptyProduction, List<IProduction> Productions)
    {
        if (emptyProduction.Info != null)
        {
            return Productions;
        }

        emptyProduction.Info = new ProductionInfo();
        emptyProduction.Info.IsNullable = true;

        emptyProduction.Info.Index = Productions.Count;
        emptyProduction.Info.SymbolCount = 0;
        Productions.Add(emptyProduction);

        return Productions;
    }

    List<IProduction> IProductionVisitor<List<IProduction>, List<IProduction>>.VisitAlternation<T>(AlternationProduction<T> alternationProduction, List<IProduction> Productions)
    {
        if (alternationProduction.Info != null)
        {
            return Productions;
        }

        alternationProduction.Info = new ProductionInfo();

        alternationProduction.Production1.Accept(this, Productions);
        alternationProduction.Production2.Accept(this, Productions);

        alternationProduction.Info.Index = Productions.Count;
        alternationProduction.Info.SymbolCount = 1;
        Productions.Add(alternationProduction);

        return Productions;
    }

    List<IProduction> IProductionVisitor<List<IProduction>, List<IProduction>>.VisitConcatenation<T1, T2, TR>(ConcatenationProduction<T1, T2, TR> concatenationProduction, List<IProduction> Productions)
    {
        if (concatenationProduction.Info != null)
        {
            return Productions;
        }

        concatenationProduction.Info = new ProductionInfo();

        concatenationProduction.ProductionLeft.Accept(this, Productions);
        concatenationProduction.ProductionRight.Accept(this, Productions);

        concatenationProduction.Info.Index = Productions.Count;
        concatenationProduction.Info.SymbolCount = 2;
        Productions.Add(concatenationProduction);

        return Productions;
    }
}

internal class FirstFollowVisitor : IProductionVisitor<bool, bool>
{
    bool IProductionVisitor<bool, bool>.VisitTerminal(Terminal terminal, bool IsChanged)
    {
        return IsChanged;
    }

    bool IProductionVisitor<bool, bool>.VisitMapping<TSource, TReturn>(MappingProduction<TSource, TReturn> mappingProduction, bool IsChanged)
    {
        var source = mappingProduction.SourceProduction;

        IsChanged = mappingProduction.Info.First.UnionCheck(source.Info.First) || IsChanged;
        IsChanged = source.Info.Follow.UnionCheck(mappingProduction.Info.Follow) || IsChanged;

        if (mappingProduction.Info.IsNullable != source.Info.IsNullable)
        {
            mappingProduction.Info.IsNullable = source.Info.IsNullable;
            IsChanged = true;
        }

        return IsChanged;
    }

    //bool IProductionVisitor<bool, bool>.VisitEndOfStream(EndOfStream endOfStream, bool IsChanged)
    //{
    //    return IsChanged;
    //}

    bool IProductionVisitor<bool, bool>.VisitEmpty<T>(EmptyProduction<T> emptyProduction, bool IsChanged)
    {
        return IsChanged;
    }

    bool IProductionVisitor<bool, bool>.VisitAlternation<T>(AlternationProduction<T> alternationProduction, bool IsChanged)
    {
        var info = alternationProduction.Info;

        var info1 = alternationProduction.Production1.Info;
        var info2 = alternationProduction.Production2.Info;


        IsChanged = info.First.UnionCheck(info1.First) || IsChanged;
        IsChanged = info.First.UnionCheck(info2.First) || IsChanged;

        IsChanged = info1.Follow.UnionCheck(info.Follow) || IsChanged;
        IsChanged = info2.Follow.UnionCheck(info.Follow) || IsChanged;

        bool isNullable = info1.IsNullable || info2.IsNullable;

        if (info.IsNullable != isNullable)
        {
            IsChanged = true;
            info.IsNullable = isNullable;
        }

        return IsChanged;
    }

    bool IProductionVisitor<bool, bool>.VisitConcatenation<T1, T2, TR>(ConcatenationProduction<T1, T2, TR> concatenationProduction, bool IsChanged)
    {
        var p1 = concatenationProduction.ProductionLeft;
        var p2 = concatenationProduction.ProductionRight;

        var info = concatenationProduction.Info;
        var info1 = p1.Info;
        var info2 = p2.Info;

        IsChanged = info.First.UnionCheck(info1.First) || IsChanged;

        if (info1.IsNullable)
        {
            IsChanged = info.First.UnionCheck(info2.First) || IsChanged;
        }

        if (info2.IsNullable)
        {
            IsChanged = info1.Follow.UnionCheck(info.Follow) || IsChanged;
        }

        IsChanged = info2.Follow.UnionCheck(info.Follow) || IsChanged;
        IsChanged = info1.Follow.UnionCheck(info2.First) || IsChanged;

        bool isNullable = info1.IsNullable && info2.IsNullable;

        if (info.IsNullable != isNullable)
        {
            IsChanged = true;
            info.IsNullable = isNullable;
        }

        return IsChanged;

    }


}
