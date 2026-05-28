using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

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

    private readonly Dictionary<string, int> _arrayLengths = [];

    private readonly ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? _lazyFunctionBodies;
    private readonly DiagnosticBag? _programDiagnostics;
    private readonly HashSet<FunctionSymbol>? _bindingFunctions;

    public DiagnosticBag Diagnostics => _diagnostics;

    private Binder(BoundScope? parent, FunctionSymbol? function,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement>.Builder? lazyFunctionBodies = null,
        DiagnosticBag? programDiagnostics = null,
        HashSet<FunctionSymbol>? bindingFunctions = null)
    {
        _scope = new BoundScope(parent);
        _function = function;
        _lazyFunctionBodies = lazyFunctionBodies;
        _programDiagnostics = programDiagnostics;
        _bindingFunctions = bindingFunctions;

        if (function != null)
        {
            foreach (var p in function.Parameters)
                _scope.TryDeclareVariable(p);
        }
    }

    public static BoundProgram BindProgram(ImmutableArray<SyntaxTree> syntaxTrees, ImmutableHashSet<string>? externalVariables = default)
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

        // --- Phase 1: lib 绑定 ---
        var libBinder = new Binder(new BoundScope(parentScope), function: null);
        var libUserFunctions = new List<FunctionSymbol>();
        var libGlobalStmts = new List<BoundStmt>();
        var libMembers = libTrees.SelectMany(t => t.Root.Members);

        foreach (var member in libMembers)
        {
            switch (member)
            {
                case FuncDeclBlock func:
                    libUserFunctions.Add(libBinder.BindFuncDeclaration(func));
                    break;
                case ExternFuncStmt ext:
                    externFunctions.Add(libBinder.BindExternDeclaration(ext));
                    break;
                case StructDeclBlock structDecl:
                    libBinder.BindStructDeclaration(structDecl);
                    break;
            }
        }

        foreach (var member in libMembers)
        {
            if (member is not FuncDeclBlock and not ExternFuncStmt and not StructDeclBlock)
                libGlobalStmts.Add(libBinder.BindStatement(member));
        }

        foreach (var function in libUserFunctions)
        {
            var (body, binderFn) = BindFunctionBody(function, libBinder._scope);
            if (function.ReturnType != ScriptType.Void && !ControlFlowGraph.AllPathsReturn(body))
                binderFn.Diagnostics.ReportAllPathsMustReturn(function.Declaration!.Declare.Location);
            functionBodies.Add(function, body);
            ilNames.UnionWith(binderFn._ilNames);
            diagnostics.AddRange(binderFn.Diagnostics);
        }

        var libModule = new ModuleSymbol("lib", isLib: true);
        var mainModule = new ModuleSymbol("main", isLib: false);

        // --- Phase 2: 主脚本绑定 ---
        var bindingFunctions = new HashSet<FunctionSymbol>();
        var mainBinder = new Binder(new BoundScope(parentScope), function: null,
            functionBodies, diagnostics, bindingFunctions);

        // 将 lib 符号导入到主作用域
        mainBinder._scope.ImportFrom(libBinder._scope);

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

        diagnostics.AddRange(libBinder.Diagnostics);
        diagnostics.AddRange(mainBinder.Diagnostics);

        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        // --- Phase 3: 构建 $eval 主函数 ---
        var allGlobalStmts = libGlobalStmts.Concat(mainGlobalStmts);
        var main = new FunctionSymbol("$eval", [], ScriptType.Void);
        var evalBody = new BoundBlockStatement(main.Declaration!, [.. allGlobalStmts]);
        var loweredEval = Lowerer.Lower(main, evalBody);
        AllocateLocalSlots(main, loweredEval);
        functionBodies.Add(main, loweredEval);

        var allStructDefs = mainBinder._scope.CollectAllStructDefs();

        return new BoundProgram(main, [.. diagnostics], functionBodies.ToImmutable(), externFunctions.ToImmutable(), [.. ilNames], allStructDefs)
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
        var lowered = Lowerer.Lower(function, body);
        AllocateLocalSlots(function, lowered);
        return (lowered, binderFn);
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
}