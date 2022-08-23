namespace Compiler.Parsers;

public class MappingProduction<TSource, TReturn> : ProductionBase<TReturn>
{
    public MappingProduction(ProductionBase<TSource> sourceProduction, Func<TSource, TReturn> selector, Func<TReturn, bool> validationRule, int? errorId, Func<TReturn, SourceSpan> positionGetter)
    {
        CodeContract.RequiresArgumentNotNull(sourceProduction, "sourceProduction");
        CodeContract.RequiresArgumentNotNull(selector, "selector");

        SourceProduction = sourceProduction;
        m_selector = selector;
        Selector = SafeSelector;
        ValidationRule = validationRule;
        ValidationErrorId = errorId;
        PositionGetter = positionGetter;
    }

    public MappingProduction(ProductionBase<TSource> sourceProduction, Func<TSource, TReturn> selector) : this(sourceProduction, selector, null, null, null) { }
    public ProductionBase<TSource> SourceProduction { get; private set; }
    public Func<TSource, TReturn> Selector { get; private set; }
    public Func<TReturn, bool> ValidationRule { get; private set; }
    public int? ValidationErrorId { get; private set; }
    public Func<TReturn, SourceSpan> PositionGetter { get; private set; }

    private Func<TSource, TReturn> m_selector;
    private TReturn SafeSelector(TSource source)
    {
        try
        {
            return m_selector(source);
        }
        catch (NullReferenceException)
        {
            return DefaultValueContainer<TReturn>.DefaultValue;
        }
    }

    public override string DebugName
    {
        get
        {
            return "M" + DebugNameSuffix;
        }
    }

    public override TResult Accept<TArg, TResult>(IProductionVisitor<TArg, TResult> visitor, TArg argument)
    {
        return visitor.VisitMapping(this, argument);
    }

    public override string ToString()
    {
        return String.Format("{0} ::= {1}", DebugName, SourceProduction.DebugName);
    }
}