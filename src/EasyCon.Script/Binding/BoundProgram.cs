using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class BoundProgram(FunctionSymbol main,
ImmutableArray<Diagnostic> diagnostics,
ImmutableDictionary<FunctionSymbol, BoundBlockStatement> functions,
ImmutableArray<FunctionSymbol> externFunctions,
ImmutableArray<string> imglabels,
ImmutableDictionary<string, EcsStructDef> structDefinitions)
{
    public readonly FunctionSymbol MainFunction = main;
    public ImmutableDictionary<FunctionSymbol, BoundBlockStatement> Functions = functions;
    public ImmutableArray<FunctionSymbol> ExternFunctions = externFunctions;
    public ImmutableArray<Diagnostic> Diagnostics = diagnostics;
    public ImmutableArray<string> ILNames = imglabels;
    public readonly ImmutableDictionary<string, EcsStructDef> StructDefinitions = structDefinitions;

    public bool KeyAction => Functions.Values.SelectMany(s => s.Statements).OfType<BoundKeyActStatement>().ToList().Count != 0;
    public bool NeedIL => ILNames.Any();
}

internal abstract class BoundNode
{
    public abstract BoundNodeKind Kind { get; }
}

internal abstract class BoundStmt(AstNode stmt) : BoundNode
{
    public AstNode Syntax = stmt;
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

internal sealed class BoundIfStatement(
    AstNode syntax,
    BoundExpr condition,
    BoundBlockStatement body,
    ImmutableArray<(BoundExpr Condition, BoundBlockStatement Body)> elseIfs,
    BoundBlockStatement? elseBody) : BoundStmt(syntax)
{
    public override BoundNodeKind Kind => BoundNodeKind.IfStatement;
    public readonly BoundExpr Condition = condition;
    public readonly BoundBlockStatement Body = body;
    public readonly ImmutableArray<(BoundExpr Condition, BoundBlockStatement Body)> ElseIfs = elseIfs;
    public readonly BoundBlockStatement? ElseBody = elseBody;
}