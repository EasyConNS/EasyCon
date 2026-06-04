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

    /// <summary>模块符号列表（lib 模块 + 主模块），由 BindProgram 填充</summary>
    public ImmutableArray<ModuleSymbol>? Modules { get; init; }

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