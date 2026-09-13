using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;
using System.Linq;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    private readonly DiagnosticBag _diagnostics = [];
    private readonly FunctionSymbol? _function;

    private readonly Stack<(BoundLabel BreakLabel, BoundLabel ContinueLabel)> _loopStack = new();
    private int _labelCounter = 0;
    const int _max_allow_level = 3;
    private BoundScope _scope;
    private readonly HashSet<string> _ilNames = [];

    private readonly ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? _lazyFunctionBodies;
    private readonly DiagnosticBag? _programDiagnostics;
    private readonly Resolution.ResolutionResult? _resolution;
    private readonly HashSet<FunctionSymbol>? _bindingFunctions;
    private readonly bool _legacySyntax;

    public DiagnosticBag Diagnostics => _diagnostics;

    private Binder(BoundScope? parent, FunctionSymbol? function,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? lazyFunctionBodies = null,
        DiagnosticBag? programDiagnostics = null,
        HashSet<FunctionSymbol>? bindingFunctions = null,
        Resolution.ResolutionResult? resolution = null,
        bool legacySyntax = true)
    {
        _scope = new BoundScope(parent);
        _function = function;
        _lazyFunctionBodies = lazyFunctionBodies;
        _programDiagnostics = programDiagnostics;
        _resolution = resolution;
        _bindingFunctions = bindingFunctions;
        _legacySyntax = legacySyntax;

        if (function != null)
        {
            foreach (var p in function.Parameters)
                _scope.TryDeclareVariable(p);
        }
    }

    /// <summary>
    /// 模块单树绑定（统一链路，docs/Pipeline.md）：每模块只绑定自己的语法树——
    /// 函数/extern 声明 + 顶层语句（$eval）+（可选）急切绑定全部函数体（导出对外可见）。
    /// 依赖以接口合成符号提供（InterfaceScopeSynthesizer），Declaration=null 自动跳过绑体。
    /// v1 的 lib/main 双阶段合并绑定（libOnlyScope、$eval 语句拼接、aliased 源码树比对）
    /// 随源码级合并管线一并退役。
    /// </summary>
    public static BoundProgram BindProgram(Resolution.ResolutionResult resolution, ImmutableHashSet<string>? externalVariables = default,
        bool eagerBindMainFunctions = false, bool legacySyntax = true)
    {
        var parentScope = resolution.GlobalScope ?? CreateRootScope();
        parentScope.SetValidExternalVariables(externalVariables ?? []);

        var diagnostics = new DiagnosticBag();
        foreach (var tree in resolution.Trees)
            diagnostics.AddRange(tree.Diagnostics);
        diagnostics.AddRange(resolution.Diagnostics);
        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();
        var externFunctions = ImmutableArray.CreateBuilder<FunctionSymbol>();
        var ilNames = new HashSet<string>();

        // --- 模块声明绑定（函数/extern）---
        var bindingFunctions = new HashSet<FunctionSymbol>();
        var moduleBinder = new Binder(new BoundScope(parentScope), function: null,
            functionBodies, diagnostics, bindingFunctions, resolution: resolution, legacySyntax: legacySyntax);

        var moduleFunctions = new List<FunctionSymbol>();
        foreach (var member in resolution.Trees.SelectMany(t => t.Root.Members))
        {
            switch (member)
            {
                case FuncDeclBlock func:
                    moduleFunctions.Add(moduleBinder.BindFuncDeclaration(func));
                    break;
                case ExternFuncStmt ext:
                    externFunctions.Add(moduleBinder.BindExternDeclaration(ext));
                    break;
                    // StructDeclBlock 已在解析作用域合成阶段声明（InterfaceScopeSynthesizer）
            }
        }

        // --- 顶层语句（$eval：模块初始化 + 主脚本主体）---
        var moduleGlobalStmts = new List<BoundStmt>();
        foreach (var member in resolution.Trees.SelectMany(t => t.Root.Members))
        {
            if (member is not FuncDeclBlock and not ExternFuncStmt and not StructDeclBlock)
                moduleGlobalStmts.Add(moduleBinder.BindStatement(member));
        }

        // 导出函数对外可见，必须全体编码：急切绑定全部函数体
        //（惰性绑定只覆盖被调用者，会丢未被调用的导出）
        if (eagerBindMainFunctions)
        {
            foreach (var function in moduleFunctions)
            {
                if (functionBodies.ContainsKey(function)) continue;
                var (body, binderFn) = BindFunctionBody(function, moduleBinder._scope, legacySyntax: legacySyntax);
                if (function.ReturnType != ScriptType.Void && !ControlFlowGraph.AllPathsReturn(body))
                    binderFn.Diagnostics.ReportAllPathsMustReturn(function.Declaration!.Declare.Location);
                functionBodies.Add(function, body);
                ilNames.UnionWith(binderFn._ilNames);
                diagnostics.AddRange(binderFn.Diagnostics);
            }
        }
        ilNames.UnionWith(moduleBinder._ilNames);
        diagnostics.AddRange(moduleBinder.Diagnostics);

        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        // --- $eval：模块顶层语句（main 模块即主脚本主体；lib 模块即 &lt;init:module&gt; 语义）---
        var main = new FunctionSymbol("$eval", [], ScriptType.Void);
        var evalBody = new BoundBlockStatement(main.Declaration!, [.. moduleGlobalStmts]);
        functionBodies.Add(main, evalBody);

        var allStructDefs = moduleBinder._scope.CollectAllStructDefs();

        return new BoundProgram(main, [.. diagnostics], functionBodies.ToImmutable(), externFunctions.ToImmutable(), [.. ilNames], allStructDefs);
    }

    private static (BoundBlockStatement Body, Binder Binder) BindFunctionBody(
        FunctionSymbol function, BoundScope scope,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? lazyFunctionBodies = null,
        DiagnosticBag? programDiagnostics = null,
        HashSet<FunctionSymbol>? bindingFunctions = null,
        bool legacySyntax = true)
    {
        var binderFn = new Binder(scope, function, lazyFunctionBodies, programDiagnostics, bindingFunctions, legacySyntax: legacySyntax);
        var stmts = ImmutableArray.CreateBuilder<BoundStmt>();
        foreach (var stmt in function.Declaration!.Statements)
            stmts.Add(binderFn.BindStatement(stmt));
        var body = new BoundBlockStatement(function.Declaration!, stmts.ToImmutable());
        return (body, binderFn);
    }

    private void EnsureFunctionBodyBound(FunctionSymbol function)
    {
        if (function.Declaration == null) return;
        if (_lazyFunctionBodies == null) return;
        if (_lazyFunctionBodies.ContainsKey(function)) return;
        if (_bindingFunctions != null && !_bindingFunctions.Add(function)) return;

        var (body, binderFn) = BindFunctionBody(function, _scope, _lazyFunctionBodies, _programDiagnostics, _bindingFunctions);
        if (function.ReturnType != ScriptType.Void && !ControlFlowGraph.AllPathsReturn(body))
            binderFn.Diagnostics.ReportAllPathsMustReturn(function.Declaration!.Declare.Location);
        _lazyFunctionBodies.Add(function, body);
        _ilNames.UnionWith(binderFn._ilNames);
        _programDiagnostics?.AddRange(binderFn.Diagnostics);
    }

    private static BoundProgram ErrorProgram(DiagnosticBag diagnostics)
    {
        return new BoundProgram(new("$error", [], ScriptType.Void), [.. diagnostics], [], [], [], []);
    }

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null);

        foreach (var f in BuiltinFunctions.GetAll())
            result.TryDeclareFunction(f);

        return result;
    }

    #region 共用辅助方法

    private (StructType structType, EcsFieldDef field)? TryResolveStructField(BoundExpr target, string fieldName, TextLocation loc)
    {
        if (target.Type is not StructType structType)
        {
            _diagnostics.ReportBadStruct(loc, $"类型 {target.Type} 不支持字段访问");
            return null;
        }

        var field = structType.Definition.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null)
        {
            _diagnostics.ReportBadStruct(loc, $"结构体 {structType.Name} 没有字段 {fieldName}");
            return null;
        }

        return (structType, field);
    }

    private static (bool isString, bool isArray) CheckIndexSupport(ScriptType type)
    {
        bool isString = type.Equals(ScriptType.String);
        bool isArray = type is ArrayType;
        return (isString, isArray);
    }

    private BoundExpr ReportUnknownExprAndError(BaseExpr syntax)
    {
        _diagnostics.ReportUnknownExpressionType(syntax.Syntax.Location);
        return new BoundErrorExpression(syntax);
    }

    #endregion
}