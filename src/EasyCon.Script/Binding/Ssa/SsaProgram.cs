using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding.Ssa;

/// <summary>
/// SSA 形式的函数体。
/// </summary>
public sealed class SsaFunction
{
    public readonly FunctionSymbol Symbol;
    public readonly List<SsaBlock> Blocks = new();
    public FrameLayout Layout;

    public SsaBlock Entry => Blocks[0];

    public SsaFunction(FunctionSymbol symbol) { Symbol = symbol; }
}

/// <summary>
/// SSA 形式的完整程序。
/// </summary>
public sealed class SsaProgram
{
    public required SsaFunction? MainFunction { get; init; }
    public required ImmutableDictionary<FunctionSymbol, SsaFunction> Functions { get; init; }
    public required ImmutableArray<FunctionSymbol> ExternFunctions { get; init; }
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }
    public required ImmutableDictionary<string, EcsStructDef> StructDefinitions { get; init; }
    public required ImmutableArray<string> ILNames { get; init; }
    public bool KeyAction { get; init; }
    public bool NeedIL { get; init; }
}