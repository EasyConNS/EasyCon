using System.Collections.Generic;

namespace Compiler.Parsers.Generator;

public class ProductionInfo
{
    public ProductionInfo()
    {
        First = new HashSet<IProduction>();
        Follow = new HashSet<IProduction>();
        IsNullable = false;
    }

    public ISet<IProduction> First { get; private set; }
    public ISet<IProduction> Follow { get; private set; }
    public bool IsNullable { get; internal set; }

    internal int Index { get; set; }
    internal int SymbolCount { get; set; }
    internal int NonTerminalIndex { get; set; }
}