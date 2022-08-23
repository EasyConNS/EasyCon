using System.Collections.Generic;

namespace Compiler.Parsers.Generator;

public class ProductionInfoManager
{
    private IProduction[] m_productions;

    public ProductionInfoManager(IProduction root)
    {
        CodeContract.RequiresArgumentNotNull(root, "root");

        var aggregator = new ProductionAggregationVisitor();
        var productions = root.Accept(aggregator, new List<IProduction>());

        m_productions = productions.ToArray();
        RootProduction = root;

        var ffVisitor = new FirstFollowVisitor();

        bool isChanged;

        do
        {
            isChanged = false;

            foreach (var p in productions)
            {
                isChanged = p.Accept(ffVisitor, isChanged);
            }

        } while (isChanged);
    }

    public IProduction RootProduction { get; private set; }

    public IReadOnlyList<IProduction> Productions
    {
        get
        {
            return m_productions;
        }
    }

    public ProductionInfo GetInfo(IProduction production)
    {
        return (production as ProductionBase).Info;
    }
}