using EasyCon.Script2.Ast;
using EasyCon.Script2.Symbols;
using EasyCon.Script2.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script2.Binding;

internal sealed class Binder
{
    private readonly DiagnosticBag _diagnostics = new DiagnosticBag();

    private readonly FunctionSymbol? _function;
    private BoundScope _scope;

    private Binder(BoundScope? parent, FunctionSymbol? function)
    {
        _scope = new BoundScope(parent);
        _function = function;

        if (function != null)
        {
            foreach (var p in function.Parameters)
                _scope.TryDeclareVariable(p);
        }
    }

    public static BoundGlobalScope BindGlobalScope(BoundGlobalScope? previous, ImmutableArray<SyntaxTree> syntaxTrees)
    {
        var parentScope = CreateParentScope(previous);
        var binder = new Binder(parentScope, function: null);
        var functionDeclarations = syntaxTrees.SelectMany(st => st.Root.Members)
                                                  .OfType<FunctionDeclarationStatement>();

        foreach (var function in functionDeclarations)
            binder.BindFuncDeclaration(function);
        var globalStatements = syntaxTrees.SelectMany(st => st.Root.Members)
                                              .OfType<GlobalStatement>();

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        foreach (var globalStatement in globalStatements)
        {
            var statement = binder.BindGlobalStatement(globalStatement.Statement);
            statements.Add(statement);
        }
        var functions = binder._scope.GetDeclaredFunctions();

        return new BoundGlobalScope(previous, [], null, null, functions, [], statements.ToImmutable());
    }

    public static void BindGrogram() { }

    private void BindFuncDeclaration(FunctionDeclarationStatement syntax)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>();
        foreach (var parameterSyntax in syntax.Parameters)
        {
            //
        }
    }

    private static BoundScope CreateParentScope(BoundGlobalScope? previous)
    {
        var stack = new Stack<BoundGlobalScope>();
        while (previous != null)
        {
            stack.Push(previous);
            previous = previous.Previous;
        }
        var parent = CreateRootScope();
        while (stack.Count > 0)
        {
            previous = stack.Pop();
            var scope = new BoundScope(parent);

            foreach (var f in previous.Functions)
                scope.TryDeclareFunction(f);

            foreach (var v in previous.Variables)
                scope.TryDeclareVariable(v);

            parent = scope;
        }

        return parent;
    }

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null);

        foreach (var f in BuiltinFunctions.GetAll())
            result.TryDeclareFunction(f);

        return result;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private BoundStatement BindGlobalStatement(Statement syntax)
    {
        return BindStatement(syntax, isGlobal: true);
    }

    private BoundStatement BindStatement(Statement syntax, bool isGlobal = false)
    {
        var result = BindStatementInternal(syntax);

        //if (!_isScript || !isGlobal)
        //{
        //    if (result is BoundExpressionStatement es)
        //    {
        //        var isAllowedExpression = es.Expression.Kind == BoundNodeKind.ErrorExpression ||
        //                                  es.Expression.Kind == BoundNodeKind.AssignmentExpression ||
        //                                  es.Expression.Kind == BoundNodeKind.CallExpression ||
        //                                  es.Expression.Kind == BoundNodeKind.CompoundAssignmentExpression;
        //        if (!isAllowedExpression)
        //            _diagnostics.ReportInvalidExpressionStatement(syntax.Location);
        //    }
        //}

        return result;
    }

    private BoundStatement BindStatementInternal(Statement syntax)
    {
        switch (syntax)
        {
            default:
                throw new Exception($"Unexpected syntax {syntax}");
        }
    }
}
