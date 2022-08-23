using System.Globalization;
using Compiler.Parsers.Generator;

namespace Compiler.Parsers;

public abstract class ProductionBase : IProduction
{
    protected ProductionBase(){}

    internal virtual ProductionInfo Info { get; set; }

    public virtual string DebugName
    {
        get
        {
            return "P";
        }
    }

    protected string DebugNameSuffix
    {
        get
        {
            if (Info != null)
            {
                return Info.Index.ToString(CultureInfo.InvariantCulture);
            }
            return String.Empty;
        }
    }

    public abstract TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument);

    public virtual bool IsTerminal
    {
        get { return false; }
    }

    public virtual bool IsEos
    {
        get { return false; }
    }

    public abstract bool AggregatesAmbiguities { get; }
    internal abstract AmbiguityAggregator CreateAggregator();

    
}

public abstract class ProductionBase<T> : ProductionBase
{
    protected ProductionBase(){}

    public virtual Func<T, T, T> AmbiguityAggregator { get; set; }

    public sealed override bool AggregatesAmbiguities
    {
        get { return AmbiguityAggregator != null; }
    }

    public static ProductionBase<T> operator |(ProductionBase<T> p1, ProductionBase<T> p2)
    {
        return new AlternationProduction<T>(p1, p2);
    }

    internal sealed override AmbiguityAggregator CreateAggregator()
    {
        return new AmbiguityAggregator<T>(Info.NonTerminalIndex, AmbiguityAggregator);
    }
}
