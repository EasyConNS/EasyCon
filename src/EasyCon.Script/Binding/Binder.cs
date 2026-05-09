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

        // 收集所有诊断
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

        // --- Phase 1: lib 绑定（独立作用域，无法访问主脚本全局变量）---
        var libBinder = new Binder(new BoundScope(parentScope), function: null);
        var libUserFunctions = new List<FunctionSymbol>();
        var libGlobalStmts = new List<BoundStmt>();
        var libMembers = libTrees.SelectMany(t => t.Root.Members);

        // 第 1 遍：所有声明（函数 + EXTERN + STRUCT），确保前向引用可用
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

        // 第 2 遍：全局语句
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

        // --- Phase 2: 主脚本绑定 ---
        var bindingFunctions = new HashSet<FunctionSymbol>();
        var mainBinder = new Binder(new BoundScope(parentScope), function: null,
            functionBodies, diagnostics, bindingFunctions);

        // 将 lib 函数注册到主作用域（使主脚本可以调用 lib 函数）
        foreach (var fn in libUserFunctions)
            mainBinder._scope.TryDeclareFunction(fn);
        foreach (var fn in externFunctions)
            mainBinder._scope.TryDeclareFunction(fn);
        // 将 lib 结构体注册到主作用域
        foreach (var kv in libBinder._scope.CollectAllStructDefs())
            mainBinder._scope.TryDeclareStruct(kv.Key, kv.Value);

        var mainUserFunctions = new List<FunctionSymbol>();
        var mainGlobalStmts = new List<BoundStmt>();
        var mainMembers = mainTrees.SelectMany(t => t.Root.Members);

        // 第 1 遍：所有声明
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

        // 主脚本全局语句检查
        var firstGlobalPerTree = mainTrees
            .Select(t => t.Root.Members.FirstOrDefault(m => m is not FuncDeclBlock && m is not EmptyStmt && m is not ImportStmt && m is not ExternFuncStmt && m is not StructDeclBlock))
            .Where(g => g != null)
            .ToArray();
        if (firstGlobalPerTree.Length > 1)
            foreach (var g in firstGlobalPerTree)
                diagnostics.ReportOnlyOneFileCanHaveGlobalStatements(g!.Location);

        // 第 2 遍：全局语句（函数体在调用时按需绑定）
        foreach (var member in mainMembers)
        {
            if (member is not FuncDeclBlock and not ExternFuncStmt and not StructDeclBlock)
                mainGlobalStmts.Add(mainBinder.BindStatement(member));
        }

        // 收集主绑定器中的 image labels（包含懒绑定的函数体中的标签）
        ilNames.UnionWith(mainBinder._ilNames);

        diagnostics.AddRange(libBinder.Diagnostics);
        diagnostics.AddRange(mainBinder.Diagnostics);

        if (diagnostics.HasErrors())
            return ErrorProgram(diagnostics);

        // --- Phase 3: 构建 $eval 主函数 ---
        var allGlobalStmts = libGlobalStmts.Concat(mainGlobalStmts);
        var main = new FunctionSymbol("$eval", [], [], ScriptType.Void);
        var evalBody = new BoundBlockStatement(main.Declaration!, [.. allGlobalStmts]);
        var loweredEval = Lowerer.Lower(main, evalBody);
        LifecycleAnalyzer.AllocateLocalSlots(main, loweredEval);
        functionBodies.Add(main, loweredEval);

        // 合并 struct 定义
        var allStructDefs = mainBinder._scope.CollectAllStructDefs();

        return new BoundProgram(main, [.. diagnostics], functionBodies.ToImmutable(), externFunctions.ToImmutable(), [.. ilNames], allStructDefs);
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
        LifecycleAnalyzer.AllocateLocalSlots(function, lowered);
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
        return new BoundProgram(new("$error", [], [], ScriptType.Void), [.. diagnostics], [], [], [], []);
    }

    private FunctionSymbol BindFuncDeclaration(FuncDeclBlock syntax)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();
        var seenParameterNames = new HashSet<string>();

        for (int i = 0; i < syntax.Declare.Paramters.Length; i++)
        {
            var parameterSyntax = syntax.Declare.Paramters[i];
            var parameterName = parameterSyntax.Identifier.Tag;
            var parameterType = BindTypeClause(syntax.Declare, parameterSyntax.Type) ?? ScriptType.Int;

            if (!seenParameterNames.Add(parameterName))
                _diagnostics.ReportParameterAlreadyDeclared(syntax.Declare.Location, parameterName);

            // 检查是否是最后一个参数且带有默认值
            bool isLast = i == syntax.Declare.Paramters.Length - 1;

            // 如果不是最后一个参数却尝试定义默认值，抛出异常
            //if (!isLast && parameterSyntax.) throw new("只有最后一个参数可以有默认值");

            var parameter = new ParamSymbol(parameterName, parameterType, parameters.Count);
            parameter.SlotIndex = parameters.Count; // 参数 slot 从 0 开始
            parameters.Add(parameter);
        }

        var returnType = BindTypeClause(syntax.Declare, syntax.Declare.Type) ?? ScriptType.Void;
        var function = new FunctionSymbol(syntax.Declare.Name, [], parameters.ToImmutable(), returnType, syntax);
        function.LocalSlotCount = parameters.Count; // 初始帧大小 = 参数数量

        // builtin 名称保护：用户函数禁止使用 builtin 名称
        if (BuiltinFunctions.GetAll().Any(b => b.Name == syntax.Declare.Name.ToUpper()))
        {
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Declare.Location, syntax.Declare.Name);
            return function;
        }

        if (!_scope.TryDeclareFunction(function))
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Declare.Location, syntax.Declare.Name);
        return function;
    }

    private FunctionSymbol BindExternDeclaration(ExternFuncStmt syntax)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();
        var seenParameterNames = new HashSet<string>();

        for (int i = 0; i < syntax.Parameters.Length; i++)
        {
            var parameterSyntax = syntax.Parameters[i];
            var parameterName = parameterSyntax.Identifier.Tag;
            var parameterType = BindTypeClause(syntax, parameterSyntax.Type) ?? ScriptType.Int;

            if (!seenParameterNames.Add(parameterName))
                _diagnostics.ReportParameterAlreadyDeclared(syntax.Location, parameterName);

            var parameter = new ParamSymbol(parameterName, parameterType, parameters.Count);
            parameter.SlotIndex = parameters.Count;
            parameters.Add(parameter);
        }

        var returnType = BindTypeClause(syntax, syntax.ReturnType) ?? ScriptType.Void;
        var function = new FunctionSymbol(syntax.Name, [], parameters.ToImmutable(), returnType, libraryName: syntax.Library, externalName: syntax.ExportName != syntax.Name ? syntax.ExportName : null);
        function.LocalSlotCount = parameters.Count;

        // builtin 名称保护
        if (BuiltinFunctions.GetAll().Any(b => b.Name == syntax.Name.ToUpper()))
        {
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
            return function;
        }

        if (!_scope.TryDeclareFunction(function))
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
        return function;
    }

    private void BindStructDeclaration(StructDeclBlock syntax)
    {
        if (_scope.TryLookupStruct(syntax.Header.Name) != null)
        {
            _diagnostics.ReportBadStruct(syntax.Location, $"结构体 {syntax.Header.Name} 已定义");
            return;
        }

        var def = new EcsStructDef { Name = syntax.Header.Name };

        foreach (var field in syntax.Fields)
        {
            var fieldType = LookupType(field.TypeName);
            if (fieldType is null)
            {
                _diagnostics.ReportBadStruct(field.Location, $"未知字段类型 {field.TypeName}");
                continue;
            }

            var fieldDef = new EcsFieldDef
            {
                Name = field.Name[1..],
                FieldType = fieldType,
            };
            def.Fields.Add(fieldDef);
        }

        StructLayout.Calculate(def);
        _scope.TryDeclareStruct(syntax.Header.Name, def);
    }

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null);

        foreach (var f in BuiltinFunctions.GetAll())
            result.TryDeclareFunction(f);

        return result;
    }
    #region 辅助方法

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
        bool isArray = type is GenericType { Definition.Name: "Array" };
        return (isString, isArray);
    }

    private BoundExpr? DesugarAugmentedAssign(AssignmentStmt syntax, Func<BoundExpr> readCurrent, ScriptType targetType, BoundExpr rhs)
    {
        if (!syntax.AssignmentToken.Type.OperatorIsAug())
            return null;

        var readExpr = readCurrent();
        var op = BoundBinaryOperator.Bind(syntax.AssignmentToken.Type, targetType, rhs.Type);
        if (op == null)
        {
            _diagnostics.ReportUnsupportedBinaryOperator(syntax.Location, syntax.AssignmentToken, targetType, rhs.Type);
            return null;
        }
        return new BoundBinaryExpression(syntax.Expression, readExpr, op, rhs);
    }

    #endregion

    private VariableSymbol BindVariableDeclaration(VariableExpr syntax, bool isReadOnly, ScriptType type, bool allowGlobal = true)
    {
        var vrr = _scope.TryLookupVar(syntax.Tag);
        if (vrr is not null)
        {
            if (vrr.IsReadOnly) _diagnostics.ReportReadOnlyVariable(syntax.Syntax);
            return vrr;
        }

        return LookupVariable(syntax, isReadOnly, type, allowGlobal);
    }
    private VariableSymbol LookupVariable(VariableExpr syntax, bool isReadOnly, ScriptType type, bool allowGlobal = true)
    {
        var variable = _function == null && allowGlobal
                    ? (VariableSymbol)new GlobalVariableSymbol(syntax.Tag, isReadOnly, type)
                    : new LocalVariableSymbol(syntax.Tag, isReadOnly, type);

        _scope.TryDeclareVariable(variable);

        return variable;
    }
    private ScriptType? BindTypeClause(Statement syntax, TypeClauseSyntax? tcs)
    {
        if (tcs == null) return null;
        var type = LookupType(tcs.TypeName);
        if (type is null)
        {
            _diagnostics.ReportUnknownType(syntax.Location, tcs.Identifier);
            return null;
        }
        return type;
    }

    private ScriptType? LookupType(string name)
    {
        var upper = name.ToUpper();
        if (upper.EndsWith(']'))
        {
            var openBracket = upper.LastIndexOf('[');
            if (openBracket > 0)
            {
                var inner = upper[(openBracket + 1)..^1];
                var baseName = name[..openBracket];
                var elem = LookupType(baseName);
                if (elem is null) return null;
                if (inner.Length == 0)
                    return ScriptType.Array.Bind(elem);
                if (int.TryParse(inner, out var count))
                    return new FixedArrayType(elem, count);
            }
        }
        return upper switch
        {
            "BOOL" => ScriptType.Bool,
            "BYTE" => ScriptType.Byte,
            "INT" => ScriptType.Int,
            "UINT" => ScriptType.UInt,
            "UINT64" => ScriptType.UInt64,
            "STRING" => ScriptType.String,
            "PTR" => ScriptType.Ptr,
            "DOUBLE" => ScriptType.Double,
            _ => _scope.TryLookupStruct(name) is { } def ? new StructType(def) : null,
        };
    }

}
