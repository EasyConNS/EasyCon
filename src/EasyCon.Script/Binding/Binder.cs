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
    private readonly ImmutableHashSet<string>? _libGlobalNames;
    private readonly bool _isLibBinder;
    private readonly Resolution.ResolutionResult? _resolution;

    private readonly ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? _lazyFunctionBodies;
    private readonly DiagnosticBag? _programDiagnostics;
    private readonly HashSet<FunctionSymbol>? _bindingFunctions;

    public DiagnosticBag Diagnostics => _diagnostics;

    private Binder(BoundScope? parent, FunctionSymbol? function,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? lazyFunctionBodies = null,
        DiagnosticBag? programDiagnostics = null,
        HashSet<FunctionSymbol>? bindingFunctions = null,
        ImmutableHashSet<string>? libGlobalNames = null,
        bool isLibBinder = false,
        Resolution.ResolutionResult? resolution = null)
    {
        _scope = new BoundScope(parent);
        _function = function;
        _lazyFunctionBodies = lazyFunctionBodies;
        _programDiagnostics = programDiagnostics;
        _bindingFunctions = bindingFunctions;
        _libGlobalNames = libGlobalNames;
        _isLibBinder = isLibBinder;
        _resolution = resolution;

        if (function != null)
        {
            foreach (var p in function.Parameters)
                _scope.TryDeclareVariable(p);
        }
    }

    public static BoundProgram BindProgram(Resolution.ResolutionResult resolution, ImmutableHashSet<string>? externalVariables = default)
    {
        var parentScope = resolution.GlobalScope ?? CreateRootScope();
        parentScope.SetValidExternalVariables(externalVariables ?? []);

        var diagnostics = new DiagnosticBag();
        foreach (var tree in resolution.Trees)
            diagnostics.AddRange(tree.Diagnostics);
        diagnostics.AddRange(resolution.Diagnostics);
        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        var libTrees = resolution.Trees.Where(t => t.IsLib).ToList();
        var mainTrees = resolution.Trees.Where(t => !t.IsLib).ToList();

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();
        var externFunctions = ImmutableArray.CreateBuilder<FunctionSymbol>();
        var ilNames = new HashSet<string>();

        // 创建 lib-only scope：在 root scope 和 lib scope 之间插入洞函数
        var libOnlyScope = new BoundScope(parentScope);
        foreach (var hole in BuiltinFunctions.GetCaptureHoles())
            libOnlyScope.TryDeclareFunction(hole);

        // --- Phase 1: lib 绑定（每个 lib 文件独立 scope，避免不同文件的函数冲突） ---
        var libUserFunctions = new List<FunctionSymbol>();
        var libGlobalStmts = new List<BoundStmt>();
        var libGlobalNamesBuilder = ImmutableHashSet.CreateBuilder<string>();
        var allLibDiagnostics = new DiagnosticBag();

        foreach (var libTree in libTrees)
        {
            // 每个 lib 文件使用独立的 scope，避免不同文件的同名函数冲突
            var fileScope = new BoundScope(libOnlyScope);
            var fileBinder = new Binder(fileScope, function: null, isLibBinder: true, resolution: resolution);
            var fileFunctions = new List<FunctionSymbol>();

            foreach (var member in libTree.Root.Members)
            {
                switch (member)
                {
                    case FuncDeclBlock func:
                        var fn = fileBinder.BindFuncDeclaration(func);
                        libUserFunctions.Add(fn);
                        fileFunctions.Add(fn);
                        break;
                    case ExternFuncStmt ext:
                        externFunctions.Add(fileBinder.BindExternDeclaration(ext));
                        break;
                        // StructDeclBlock 已在 Resolution 阶段由 DeclarationCollector 处理
                }
            }

            foreach (var member in libTree.Root.Members)
            {
                if (member is not FuncDeclBlock and not ExternFuncStmt and not StructDeclBlock)
                    libGlobalStmts.Add(fileBinder.BindStatement(member));
            }

            // 绑定当前 lib 文件的函数体（使用 fileBinder._scope，因为全局变量声明在该 scope 中）
            foreach (var function in fileFunctions)
            {
                var (body, binderFn) = BindFunctionBody(function, fileBinder._scope);
                if (function.ReturnType != ScriptType.Void && !ControlFlowGraph.AllPathsReturn(body))
                    binderFn.Diagnostics.ReportAllPathsMustReturn(function.Declaration!.Declare.Location);
                functionBodies.Add(function, body);
                ilNames.UnionWith(binderFn._ilNames);
                diagnostics.AddRange(binderFn.Diagnostics);
            }

            libGlobalNamesBuilder.UnionWith(fileBinder._scope.GetDeclaredVariableNames());
            allLibDiagnostics.AddRange(fileBinder.Diagnostics);

            // 将当前 lib 文件的函数导入到 libOnlyScope，使后续 lib 文件可以跨文件调用
            foreach (var function in fileFunctions)
                libOnlyScope.TryDeclareFunction(function);
        }

        var libModule = new ModuleSymbol("lib", isLib: true);
        var mainModule = new ModuleSymbol("main", isLib: false);

        // 收集 lib 全局变量名（用于 main 声明冲突检测）
        var libGlobalNames = libGlobalNamesBuilder.ToImmutable();

        // --- Phase 2: 主脚本绑定（惰性绑定函数体） ---
        var bindingFunctions = new HashSet<FunctionSymbol>();

        // 将 lib 函数导入到主 scope（使 main 能调用 lib 函数）
        var mainBindingScope = new BoundScope(parentScope);
        foreach (var function in libUserFunctions)
            mainBindingScope.TryDeclareFunction(function);

        var mainBinder = new Binder(mainBindingScope, function: null,
            functionBodies, diagnostics, bindingFunctions,
            libGlobalNames: libGlobalNames, resolution: resolution);

        var mainUserFunctions = new List<FunctionSymbol>();
        var mainGlobalStmts = new List<BoundStmt>();
        var mainMembers = mainTrees.SelectMany(t => t.Root.Members);

        foreach (var member in mainMembers)
        {
            switch (member)
            {
                case FuncDeclBlock func:
                    mainUserFunctions.Add(mainBinder.BindFuncDeclaration(func));
                    break;
                case ExternFuncStmt ext:
                    externFunctions.Add(mainBinder.BindExternDeclaration(ext));
                    break;
                    // StructDeclBlock 已在 Resolution 阶段由 DeclarationCollector 处理
            }
        }

        // 多文件全局语句检测（仅检查 mainTrees）
        var firstGlobalPerTree = mainTrees
            .Select(t => t.Root.Members.FirstOrDefault(m => m is not FuncDeclBlock and not EmptyStmt
                and not ImportStmt and not ExternFuncStmt and not StructDeclBlock))
            .Where(g => g != null)
            .ToArray();
        if (firstGlobalPerTree.Length > 1)
            foreach (var g in firstGlobalPerTree)
                diagnostics.ReportOnlyOneFileCanHaveGlobalStatements(g!.Location);

        foreach (var member in mainMembers)
        {
            if (member is not FuncDeclBlock and not ExternFuncStmt and not StructDeclBlock)
                mainGlobalStmts.Add(mainBinder.BindStatement(member));
        }

        ilNames.UnionWith(mainBinder._ilNames);
        diagnostics.AddRange(allLibDiagnostics);
        diagnostics.AddRange(mainBinder.Diagnostics);

        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        // --- Phase 3: 构建 $eval 主函数 ---
        var allGlobalStmts = libGlobalStmts.Concat(mainGlobalStmts);
        var main = new FunctionSymbol("$eval", [], ScriptType.Void);
        var evalBody = new BoundBlockStatement(main.Declaration!, [.. allGlobalStmts]);
        functionBodies.Add(main, evalBody);

        var allStructDefs = mainBinder._scope.CollectAllStructDefs();

        return new BoundProgram(main, [.. diagnostics], functionBodies.ToImmutable(), externFunctions.ToImmutable(), [.. ilNames], allStructDefs)
        {
            Modules = [libModule, mainModule]
        };
    }

    /// <summary>
    /// 兼容旧 API：无 ResolutionResult 时回退到原有逻辑。
    /// </summary>
    public static BoundProgram BindProgram(ImmutableArray<SyntaxTree> syntaxTrees, ImmutableHashSet<string>? externalVariables = default)
    {
        var resolution = new Resolution.ResolutionResult(syntaxTrees, null,
            System.Collections.Immutable.ImmutableDictionary<string, BoundScope>.Empty, new DiagnosticBag());
        return BindProgram(resolution, externalVariables);
    }

    private static (BoundBlockStatement Body, Binder Binder) BindFunctionBody(
        FunctionSymbol function, BoundScope scope,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? lazyFunctionBodies = null,
        DiagnosticBag? programDiagnostics = null,
        HashSet<FunctionSymbol>? bindingFunctions = null)
    {
        var binderFn = new Binder(scope, function, lazyFunctionBodies, programDiagnostics, bindingFunctions);
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

        result.TryDeclareStruct("Pixel", BuiltinFunctions.PixelStructDef);

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