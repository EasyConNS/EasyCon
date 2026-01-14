using EasyCon.Script.Parsing;
using EasyCon.Script2.Binding;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundProgram(FunctionSymbol main, ImmutableDictionary<FunctionSymbol, BoundBlockStatement> functions)
{
    public readonly FunctionSymbol MainFunction = main;
    public ImmutableDictionary<FunctionSymbol, BoundBlockStatement> Functions = functions;

    public bool KeyAction => Functions.Values.SelectMany(s=>s.Statements).OfType<BoundKeyActStatement>().ToList().Count != 0;
}

internal abstract class BoundNode
{
    public abstract BoundNodeKind Kind { get; }
}

internal abstract class BoundStmt(Statement stmt) : BoundNode
{
    public Statement Syntax = stmt;
}

internal sealed class BoundLabel(string name)
{
    public readonly string Name = name;

    public override string ToString() => Name;
}
internal sealed class BoundWhileStatement(Statement syntax, BoundExpr condition, BoundBlockStatement body, BoundLabel breakLabel, BoundLabel continueLabel) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.While;
    public readonly BoundExpr Condition = condition;
    public BoundBlockStatement Body = body;
    public readonly BoundLabel BreakLabel = breakLabel;
    public readonly BoundLabel ContinueLabel = continueLabel;
}