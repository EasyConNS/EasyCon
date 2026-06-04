using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    #region 多态内置函数类型解析

    private (ScriptType[] ParamTypes, ScriptType ReturnType) ResolveCallTypes(FunctionSymbol function, ImmutableArray<BoundExpr> arguments)
    {
        var paramTypes = new ScriptType[arguments.Length];
        for (int i = 0; i < arguments.Length && i < function.Parameters.Length; i++)
        {
            paramTypes[i] = function.Parameters[i].Type.Equals(ScriptType.Any)
                ? arguments[i].Type
                : function.Parameters[i].Type;
        }

        if (function.Name == "APPEND" && arguments.Length >= 2 && arguments[0].Type is ArrayType at)
        {
            paramTypes[0] = arguments[0].Type;
            paramTypes[1] = at.ElementType;
            return (paramTypes, arguments[0].Type);
        }

        if (function.Name == "JQ")
            return (paramTypes, ScriptType.String);

        var returnType = function.ReturnType.Equals(ScriptType.Any)
            ? arguments[0].Type
            : function.ReturnType;

        return (paramTypes, returnType);
    }

    #endregion

    #region 调用表达式绑定

    private BoundExpr BindCallExpressionInternal(AstNode syntax, FunctionSymbol function, ImmutableArray<BaseExpr> Arguments)
    {
        var boundArgs = Arguments.Select(BindExpression).ToImmutableArray();

        int minArgs = function.Parameters.Count(p => !p.HasDefaultValue);
        int maxArgs = function.Parameters.Length;

        if (boundArgs.Length < minArgs || boundArgs.Length > maxArgs)
        {
            _diagnostics.ReportFunctionArgumentCountMismatch(syntax.Syntax.Location, function);
            return new BoundErrorExpression(syntax);
        }

        return BuildCallWithTypeConversion(syntax, function, boundArgs);
    }

    private BoundExpr BuildCallWithTypeConversion(AstNode syntax, FunctionSymbol function, ImmutableArray<BoundExpr> boundArgs)
    {
        if (BuiltinFunctions.RequiresCapture(function))
            _ilNames.Add(BuiltinFunctions.CapturePlaceholder);

        var (instParams, instReturn) = ResolveCallTypes(function, boundArgs);

        var finalArgs = ImmutableArray.CreateBuilder<BoundExpr>();
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            var param = function.Parameters[i];

            if (i < boundArgs.Length)
            {
                finalArgs.Add(BindConversion(boundArgs[i], instParams[i]));
            }
            else if (param.HasDefaultValue)
            {
                finalArgs.Add(new BoundLiteralExpression(syntax!, param.DefaultValue, param.Type));
            }
        }

        return new BoundCallExpression(syntax, function, finalArgs.ToImmutable(), instReturn);
    }

    private FunctionSymbol? ResolveOverload(AstNode syntax, string name, ImmutableArray<FunctionSymbol> candidates, ImmutableArray<BoundExpr> boundArgs)
    {
        if (candidates.Length == 1)
        {
            var c = candidates[0];
            if (!MatchesArgCount(c, boundArgs.Length))
            {
                _diagnostics.ReportFunctionArgumentCountMismatch(syntax.Syntax.Location, c);
                return null;
            }
            return c;
        }

        FunctionSymbol? best = null;
        int bestScore = -1;
        bool ambiguous = false;

        foreach (var candidate in candidates)
        {
            int minArgs = candidate.Parameters.Count(p => !p.HasDefaultValue);
            int maxArgs = candidate.Parameters.Length;

            if (boundArgs.Length < minArgs || boundArgs.Length > maxArgs)
                continue;

            int score = 0;
            bool eliminated = false;
            for (int i = 0; i < boundArgs.Length && i < candidate.Parameters.Length; i++)
            {
                var paramType = candidate.Parameters[i].Type;
                var argType = boundArgs[i].Type;

                if (paramType.Equals(ScriptType.Any))
                {
                    score += 1;
                }
                else if (paramType.IsAssignableFrom(argType))
                {
                    score += 2;
                }
                else if (argType != ScriptType.String && paramType == ScriptType.String)
                {
                    score += 0;
                }
                else
                {
                    eliminated = true;
                    break;
                }
            }

            if (eliminated) continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                ambiguous = false;
            }
            else if (score == bestScore)
            {
                ambiguous = true;
            }
        }

        if (best == null)
        {
            _diagnostics.ReportNoMatchingOverload(syntax.Syntax.Location, name,
                [.. boundArgs.Select(a => a.Type)]);
            return null;
        }

        if (ambiguous)
        {
            _diagnostics.ReportAmbiguousCall(syntax.Syntax.Location, name,
                [.. candidates.Where(c => MatchesArgCount(c, boundArgs.Length))]);
            return null;
        }

        return best;
    }

    private static bool MatchesArgCount(FunctionSymbol fn, int argCount)
    {
        int minArgs = fn.Parameters.Count(p => !p.HasDefaultValue);
        int maxArgs = fn.Parameters.Length;
        return argCount >= minArgs && argCount <= maxArgs;
    }

    private BoundExpr BindCallExpression(Callv1Expression syntax)
    {
        // 命名空间限定调用：lib.func()
        if (syntax is Syntax.NamespaceCallExpr nsCall)
            return BindNamespaceCallExpression(nsCall);

        var candidates = _scope.TryLookupFuncs(syntax.Identifier.Value);
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportFunctionNotFound(syntax.Identifier.Location, syntax.Identifier.Value);
            return new BoundErrorExpression(syntax);
        }

        if (candidates.Length == 1)
        {
            EnsureFunctionBodyBound(candidates[0]);
            return BindCallExpressionInternal(syntax, candidates[0], syntax.Arguments);
        }

        var boundArgs = syntax.Arguments.Select(BindExpression).ToImmutableArray();
        var function = ResolveOverload(syntax, syntax.Identifier.Value, candidates, boundArgs);
        if (function == null)
            return new BoundErrorExpression(syntax);

        EnsureFunctionBodyBound(function);

        return BuildCallWithTypeConversion(syntax, function, boundArgs);
    }

    private BoundExpr BindNamespaceCallExpression(Syntax.NamespaceCallExpr syntax)
    {
        var nsName = syntax.Namespace.Value;
        if (_resolution?.ModuleScopes == null || !_resolution.ModuleScopes.TryGetValue(nsName, out var moduleScope))
        {
            _diagnostics.ReportNamespaceNotFound(syntax.Namespace.Location, nsName);
            return new BoundErrorExpression(syntax);
        }

        var candidates = moduleScope.TryLookupFuncs(syntax.Identifier.Value);
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportFunctionNotFoundInNamespace(syntax.Identifier.Location, syntax.Identifier.Value, nsName);
            return new BoundErrorExpression(syntax);
        }

        var boundArgs = syntax.Arguments.Select(BindExpression).ToImmutableArray();
        var function = ResolveOverload(syntax, syntax.Identifier.Value, candidates, boundArgs);
        if (function == null)
            return new BoundErrorExpression(syntax);

        EnsureFunctionBodyBound(function);
        return BuildCallWithTypeConversion(syntax, function, boundArgs);
    }

    private BoundStmt BindCallStatement(CallStmt syntax)
    {
        // 命名空间限定调用：lib.func(args)
        if (syntax.Namespace != null)
            return BindNamespaceCallStatement(syntax);

        var name = syntax.FnName;
        if (BuiltinFunctions.GetAll().Select(f => f.Name).Contains(syntax.FnName.ToUpper()))
        {
            name = syntax.FnName.ToUpper();
        }

        var candidates = _scope.TryLookupFuncs(name);
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportFunctionNotFound(syntax.Location, name);
            return BindErrorStatement(syntax);
        }

        if (SyntaxTree.LegacyCompat && syntax.Args.Length == 1 && syntax.Args[0] is VariableExpr legacyVar)
        {
            var builtinFunc = candidates.FirstOrDefault(c => BuiltinFunctions.GetAll().Contains(c));
            // TIME 支持库函数匹配（builtin 或 stdlib 均可）
            var timeFunc = builtinFunc == null
                ? candidates.FirstOrDefault(c => c.Name == "TIME" && c.Parameters.Length == 0)
                : null;
            if (BuiltinFunctions.Rand == builtinFunc)
            {
                var randCallExpr = BindCallExpressionInternal(syntax, builtinFunc, [legacyVar]);
                var variable = BindVariableDeclaration(legacyVar, false, ScriptType.Int);
                return new BoundVariableDeclaration(syntax, variable, randCallExpr);
            }
            var timeTarget = builtinFunc?.Name == "TIME" ? builtinFunc : timeFunc;
            if (timeTarget != null)
            {
                var timeCallExpr = BindCallExpressionInternal(syntax, timeTarget, []);
                var variable = BindVariableDeclaration(legacyVar, false, ScriptType.Int);
                return new BoundVariableDeclaration(syntax, variable, timeCallExpr);
            }
        }

        var boundArgs = syntax.Args.Select(BindExpression).ToImmutableArray();
        var function = ResolveOverload(syntax, name, candidates, boundArgs);
        if (function == null)
            return BindErrorStatement(syntax);

        EnsureFunctionBodyBound(function);

        var expr = BuildCallWithTypeConversion(syntax, function, boundArgs);
        return new BoundExprStatement(syntax, expr);
    }

    private BoundStmt BindNamespaceCallStatement(CallStmt syntax)
    {
        var nsName = syntax.Namespace!.Value;
        if (_resolution?.ModuleScopes == null || !_resolution.ModuleScopes.TryGetValue(nsName, out var moduleScope))
        {
            _diagnostics.ReportNamespaceNotFound(syntax.Namespace.Location, nsName);
            return BindErrorStatement(syntax);
        }

        var candidates = moduleScope.TryLookupFuncs(syntax.FnName);
        if (candidates.IsEmpty)
        {
            _diagnostics.ReportFunctionNotFoundInNamespace(syntax.Location, syntax.FnName, nsName);
            return BindErrorStatement(syntax);
        }

        var boundArgs = syntax.Args.Select(BindExpression).ToImmutableArray();
        var function = ResolveOverload(syntax, syntax.FnName, candidates, boundArgs);
        if (function == null)
            return BindErrorStatement(syntax);

        EnsureFunctionBodyBound(function);
        var expr = BuildCallWithTypeConversion(syntax, function, boundArgs);
        return new BoundExprStatement(syntax, expr);
    }

    #endregion
}