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

    private readonly ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? _lazyFunctionBodies;
    private readonly DiagnosticBag? _programDiagnostics;
    private readonly HashSet<FunctionSymbol>? _bindingFunctions;

    public DiagnosticBag Diagnostics => _diagnostics;

    private Binder(BoundScope? parent, FunctionSymbol? function,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? lazyFunctionBodies = null,
        DiagnosticBag? programDiagnostics = null,
        HashSet<FunctionSymbol>? bindingFunctions = null,
        ImmutableHashSet<string>? libGlobalNames = null,
        bool isLibBinder = false)
    {
        _scope = new BoundScope(parent);
        _function = function;
        _lazyFunctionBodies = lazyFunctionBodies;
        _programDiagnostics = programDiagnostics;
        _bindingFunctions = bindingFunctions;
        _libGlobalNames = libGlobalNames;
        _isLibBinder = isLibBinder;

        if (function != null)
        {
            foreach (var p in function.Parameters)
                _scope.TryDeclareVariable(p);
        }
    }

    public static BoundProgram BindProgram(ImmutableArray<SyntaxTree> syntaxTrees, ImmutableHashSet<string>? externalVariables = default, Dictionary<string, LibLoadType>? libLoadInfo = null)
    {
        var parentScope = CreateRootScope();
        parentScope.SetValidExternalVariables(externalVariables ?? []);

        var diagnostics = new DiagnosticBag();
        foreach (var tree in syntaxTrees)
            diagnostics.AddRange(tree.Diagnostics);
        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        var libTrees = syntaxTrees.Where(t => t.IsLib).ToList();
        var mainTrees = syntaxTrees.Where(t => !t.IsLib).ToList();

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();
        var externFunctions = ImmutableArray.CreateBuilder<FunctionSymbol>();
        var ilNames = new HashSet<string>();
        var namespaces = ImmutableDictionary.CreateBuilder<string, NamespaceSymbol>();

        // --- Phase 1: lib 绑定（每个 lib 文件独立 scope，避免不同文件的函数冲突） ---
        // 创建 lib-only scope：在 root scope 和 lib scope 之间插入洞函数
        var libOnlyScope = new BoundScope(parentScope);
        foreach (var hole in BuiltinFunctions.GetCaptureHoles())
            libOnlyScope.TryDeclareFunction(hole);

        var libUserFunctions = new List<FunctionSymbol>();
        var libGlobalStmts = new List<BoundStmt>();
        var libGlobalNamesBuilder = ImmutableHashSet.CreateBuilder<string>();
        var allLibDiagnostics = new DiagnosticBag();
        // 记录每个 SyntaxTree 对应的函数列表，用于 Phase 2 按 tree 引用分发
        var treeFunctions = new Dictionary<SyntaxTree, List<FunctionSymbol>>();

        foreach (var libTree in libTrees)
        {
            // 每个 lib 文件使用独立的 scope，避免不同文件的同名函数冲突
            var fileScope = new BoundScope(libOnlyScope);
            var fileBinder = new Binder(fileScope, function: null, isLibBinder: true);
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
                    case StructDeclBlock structDecl:
                        fileBinder.BindStructDeclaration(structDecl);
                        break;
                }
            }

            foreach (var member in libTree.Root.Members)
            {
                if (member is not FuncDeclBlock and not ExternFuncStmt and not StructDeclBlock)
                    libGlobalStmts.Add(fileBinder.BindStatement(member));
            }

            // 绑定当前 lib 文件的函数体（使用该文件自己的 scope，仅绑定本次新增的函数）
            foreach (var function in fileFunctions)
            {
                var (body, binderFn) = BindFunctionBody(function, fileScope);
                if (function.ReturnType != ScriptType.Void && !ControlFlowGraph.AllPathsReturn(body))
                    binderFn.Diagnostics.ReportAllPathsMustReturn(function.Declaration!.Declare.Location);
                functionBodies.Add(function, body);
                ilNames.UnionWith(binderFn._ilNames);
                diagnostics.AddRange(binderFn.Diagnostics);
            }

            treeFunctions[libTree] = fileFunctions;
            libGlobalNamesBuilder.UnionWith(fileBinder._scope.GetDeclaredVariableNames());
            allLibDiagnostics.AddRange(fileBinder.Diagnostics);
        }

        var libModule = new ModuleSymbol("lib", isLib: true);
        var mainModule = new ModuleSymbol("main", isLib: false);

        // 收集 lib 全局变量名（用于 main 声明冲突检测）
        var libGlobalNames = libGlobalNamesBuilder.ToImmutable();

        // --- Phase 2: 处理 import 语句，创建命名空间映射 ---
        var declaredFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in BuiltinFunctions.GetAll())
            declaredFunctionNames.Add(fn.Name);

        // 创建命名空间到lib路径的映射（路径使用 GetFullPath 规范化，确保与 tree.Text.FileName 一致）
        var namespaceToLibPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 跟踪显式全局导入的lib路径
        var globalImportLibPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tree in mainTrees)
        {
            foreach (var member in tree.Root.Members)
            {
                if (member is ImportStmt import)
                {
                    // 规范化路径，避免混合斜杠导致匹配失败
                    var libPath = Path.GetFullPath(import.FullFileName);

                    if (import.Alias != null)
                    {
                        // 命名空间导入: IMPORT "file" AS namespace
                        var nsName = import.Alias.Value;

                        // 检查命名空间名是否与已声明的函数名冲突
                        if (declaredFunctionNames.Contains(nsName))
                        {
                            diagnostics.ReportNamespaceConflictsWithFunction(
                                import.Alias.Location, nsName);
                        }

                        // 检查命名空间名是否重复声明
                        if (namespaces.ContainsKey(nsName))
                        {
                            diagnostics.ReportNamespaceAlreadyDeclared(
                                import.Alias.Location, nsName);
                        }
                        else
                        {
                            namespaces[nsName] = new NamespaceSymbol(nsName);
                            namespaceToLibPath[nsName] = libPath;
                        }
                    }
                    else
                    {
                        // 全局导入: IMPORT "file"
                        globalImportLibPaths.Add(libPath);
                    }
                }
            }
        }

        // 使用传入的libLoadInfo，如果没有则使用空字典
        var effectiveLibLoadInfo = libLoadInfo ?? new Dictionary<string, LibLoadType>(StringComparer.OrdinalIgnoreCase);

        // 将lib函数分配到对应的命名空间中，并确定哪些函数应该在全局作用域中可用
        var libPathToNamespace = namespaceToLibPath.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        var namespaceSymbols = namespaces.ToImmutable();
        var globalLibFunctions = new List<FunctionSymbol>();

        foreach (var tree in libTrees)
        {
            var libPath = tree.Text.FileName;
            var funcs = treeFunctions[tree];

            // 根据lib文件的加载方式决定函数的作用域
            if (effectiveLibLoadInfo.TryGetValue(libPath, out var loadType))
            {
                switch (loadType)
                {
                    case LibLoadType.NamespaceImport:
                        // 命名空间导入：函数只在对应命名空间中可用
                        if (libPathToNamespace.TryGetValue(libPath, out var nsName) && namespaceSymbols.TryGetValue(nsName, out var ns))
                        {
                            foreach (var function in funcs)
                                ns.TryDeclareFunction(function);
                        }
                        break;

                    case LibLoadType.GlobalImport:
                        // 全局导入：函数只在全局作用域中可用
                        globalLibFunctions.AddRange(funcs);
                        break;

                    case LibLoadType.AutoLoad:
                        // 自动加载：函数默认在全局作用域中可用（保持向后兼容性）
                        globalLibFunctions.AddRange(funcs);
                        break;
                }
            }
            else
            {
                // 内置标准库（无 libLoadInfo 条目）：标记为内置，不参与冲突检测
                foreach (var function in funcs)
                    globalLibFunctions.Add(function);
            }
        }

        // --- Phase 3: 主脚本绑定 ---
        var bindingFunctions = new HashSet<FunctionSymbol>();
        var mainBinder = new Binder(new BoundScope(parentScope), function: null,
            functionBodies, diagnostics, bindingFunctions, libGlobalNames);

        // 将命名空间导入到主作用域
        foreach (var ns in namespaceSymbols.Values)
        {
            mainBinder._scope.TryDeclareNamespace(ns);
        }

        // 将全局导入的lib函数导入到主作用域，检测重名冲突
        foreach (var function in globalLibFunctions)
        {
            if (!mainBinder._scope.TryDeclareFunction(function))
            {
                // 内置标准库（无文件名）不报冲突，仅检测用户 lib 文件之间的冲突
                var srcFile = function.Declaration?.Syntax.Text.FileName;
                if (!string.IsNullOrEmpty(srcFile))
                    diagnostics.ReportFunctionAlreadyDeclared(
                        function.Declaration!.Declare.Location, function.Name);
            }
        }

        // 将洞函数导入到主作用域（这些总是可用的）
        foreach (var hole in BuiltinFunctions.GetCaptureHoles())
            mainBinder._scope.TryDeclareFunction(hole);

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
                case StructDeclBlock structDecl:
                    mainBinder.BindStructDeclaration(structDecl);
                    break;
            }
        }

        var firstGlobalPerTree = mainTrees
            .Select(t => t.Root.Members.FirstOrDefault(m => m is not FuncDeclBlock && m is not EmptyStmt && m is not ImportStmt && m is not ExternFuncStmt && m is not StructDeclBlock))
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

        // --- Phase 4: 构建 $eval 主函数 ---
        var allGlobalStmts = libGlobalStmts.Concat(mainGlobalStmts);
        var main = new FunctionSymbol("$eval", [], ScriptType.Void);
        var evalBody = new BoundBlockStatement(main.Declaration!, [.. allGlobalStmts]);
        functionBodies.Add(main, evalBody);

        var allStructDefs = mainBinder._scope.CollectAllStructDefs();

        return new BoundProgram(main, [.. diagnostics], functionBodies.ToImmutable(), externFunctions.ToImmutable(), [.. ilNames], allStructDefs, namespaceSymbols)
        {
            Modules = [libModule, mainModule]
        };
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
        return new BoundProgram(new("$error", [], ScriptType.Void), [.. diagnostics], [], [], [], [], ImmutableDictionary<string, NamespaceSymbol>.Empty);
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