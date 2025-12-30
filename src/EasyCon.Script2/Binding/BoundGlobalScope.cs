using EasyCon.Script2.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script2.Binding;

internal sealed class BoundGlobalScope(BoundGlobalScope? previous,
                        ImmutableArray<Diagnostic> diagnostics,
                        FunctionSymbol? mainFunction,
                        FunctionSymbol? scriptFunction,
                        ImmutableArray<FunctionSymbol> functions,
                        ImmutableArray<VariableSymbol> variables,
                        ImmutableArray<BoundStatement> statements)
{
    public BoundGlobalScope? Previous { get; } = previous;
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public FunctionSymbol? MainFunction { get; } = mainFunction;
    public FunctionSymbol? ScriptFunction { get; } = scriptFunction;
    public ImmutableArray<FunctionSymbol> Functions { get; } = functions;
    public ImmutableArray<VariableSymbol> Variables { get; } = variables;
    public ImmutableArray<BoundStatement> Statements { get; } = statements;
}