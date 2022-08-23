using Compiler.Parsers.Generator;

namespace Compiler.Parsers;

public class Production<T> : ProductionBase<T>
{
    private ProductionBase<T> m_rule;
    public ProductionBase<T> Rule
    {
        get
        {
            if (m_rule == null)
            {
                throw new InvalidOperationException("The Rule property of this Production is not set. Please set before parsing.");
            }

            return m_rule;
        }
        set
        {
            CodeContract.RequiresArgumentNotNull(value, "value");
            m_rule = value;
        }
    }

    internal override ProductionInfo Info
    {
        get
        {
            return Rule.Info;
        }
        set
        {
            Rule.Info = value;
        }
    }

    public override bool IsTerminal
    {
        get
        {
            return Rule.IsTerminal;
        }
    }

    public override bool IsEos
    {
        get
        {
            return Rule.IsEos;
        }
    }

    public override string DebugName
    {
        get
        {
            return Rule.DebugName;
        }
    }

    public override Func<T, T, T> AmbiguityAggregator
    {
        get
        {
            return Rule.AmbiguityAggregator;
        }
        set
        {
            Rule.AmbiguityAggregator = value;
        }
    }

    public override TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument)
    {
        return Rule.Accept(visitor, argument);
    }

    public override bool Equals(object obj)
    {
        return Rule.Equals(obj);
    }

    public override int GetHashCode()
    {
        return Rule.GetHashCode();
    }

    public override string ToString()
    {
        return Rule.ToString();
    }
}