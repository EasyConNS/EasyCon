using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundProgram(FunctionSymbol main, ImmutableDictionary<FunctionSymbol, BoundBlockStatement> functions)
{
    public readonly FunctionSymbol MainFunction = main;
    public ImmutableDictionary<FunctionSymbol, BoundBlockStatement> Functions = functions;
} 

internal abstract class BoundStmt(Statement stmt)
{
    public abstract BoundNodeKind Kind { get; }
    public Statement Syntax = stmt;
}

internal abstract class BoundExpr
{
}

internal sealed class BoundLabel(string name)
{
    public readonly string Name = name;

    public override string ToString() => Name;
}