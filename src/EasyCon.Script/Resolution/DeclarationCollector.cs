using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Resolution;

/// <summary>
/// 遍历所有 SyntaxTree 的顶层声明，构建 GlobalScope 和 ModuleScopes。
/// 只处理签名声明（函数签名、extern、结构体），不处理函数体。
/// </summary>
internal sealed class DeclarationCollector
{
    private readonly DiagnosticBag _diagnostics = new();

    public DeclarationCollector() { }

    /// <summary>
    /// 收集所有顶层声明，返回 (GlobalScope, ModuleScopes, AllStructDefs, Diagnostics)。
    /// </summary>
    public (BoundScope GlobalScope,
            ImmutableDictionary<string, BoundScope> ModuleScopes,
            ImmutableDictionary<string, EcsStructDef> AllStructDefs,
            DiagnosticBag Diagnostics)
        Collect(ImmutableArray<SyntaxTree> trees, ImmutableDictionary<string, SyntaxTree> aliasedTrees)
    {
        // 构建根作用域：builtin 函数 + Pixel 结构体
        var globalScope = CreateRootScope();

        // 收集 aliased trees 的文件路径集合，用于过滤
        var aliasedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tree in aliasedTrees.Values)
        {
            if (!string.IsNullOrEmpty(tree.Text.FileName))
                aliasedPaths.Add(Path.GetFullPath(tree.Text.FileName));
        }

        // 第一遍：非 aliased 树的声明收集到 GlobalScope
        // aliased 树的符号只能通过命名空间限定访问，不进入全局作用域
        foreach (var tree in trees)
        {
            var treePath = tree.Text.FileName;
            if (!string.IsNullOrEmpty(treePath) && aliasedPaths.Contains(Path.GetFullPath(treePath)))
                continue;
            CollectDeclarations(tree, globalScope);
        }

        // 第二遍：为每个 aliased import 创建独立 ModuleScope 并收集其符号
        var moduleScopes = ImmutableDictionary.CreateBuilder<string, BoundScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, tree) in aliasedTrees)
        {
            var moduleScope = new BoundScope(globalScope);
            CollectDeclarations(tree, moduleScope);
            moduleScopes[alias] = moduleScope;
        }

        var allStructDefs = globalScope.CollectAllStructDefs();
        return (globalScope, moduleScopes.ToImmutable(), allStructDefs.ToImmutableDictionary(), _diagnostics);
    }

    /// <summary>
    /// 收集单个 SyntaxTree 的顶层声明到目标 scope。
    /// </summary>
    private void CollectDeclarations(SyntaxTree tree, BoundScope scope)
    {
        var members = tree.Root.Members;

        // 第一遍：结构体声明（函数参数可能引用结构体类型）
        foreach (var member in members)
        {
            if (member is StructDeclBlock structDecl)
                CollectStructDeclaration(structDecl, scope);
        }

        // 第二遍：函数和 extern 声明
        foreach (var member in members)
        {
            switch (member)
            {
                case FuncDeclBlock func:
                    CollectFuncDeclaration(func, scope);
                    break;
                case ExternFuncStmt ext:
                    CollectExternDeclaration(ext, scope);
                    break;
            }
        }
    }

    private void CollectFuncDeclaration(FuncDeclBlock syntax, BoundScope scope)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();

        for (int i = 0; i < syntax.Declare.Paramters.Length; i++)
        {
            var paramSyntax = syntax.Declare.Paramters[i];
            var paramName = paramSyntax.Identifier.Tag;
            var paramType = ResolveType(paramSyntax.Type, syntax.Declare, scope) ?? ScriptType.Int;
            var parameter = new ParamSymbol(paramName, paramType, parameters.Count);
            parameter.SlotIndex = parameters.Count;
            parameters.Add(parameter);
        }

        var returnType = ResolveType(syntax.Declare.Type, syntax.Declare, scope) ?? ScriptType.Void;
        var function = new FunctionSymbol(syntax.Declare.Name, parameters.ToImmutable(), returnType) { Declaration = syntax };
        function.LocalSlotCount = parameters.Count;

        // 检查与 builtin 函数冲突
        if (BuiltinFunctions.GetAll().Any(b => b.Name == syntax.Declare.Name.ToUpper()))
        {
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Declare.Location, syntax.Declare.Name);
            return;
        }

        // 检查 GlobalScope 中的重复定义
        if (!scope.TryDeclareFunction(function))
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Declare.Location, syntax.Declare.Name);
    }

    private void CollectExternDeclaration(ExternFuncStmt syntax, BoundScope scope)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();

        for (int i = 0; i < syntax.Parameters.Length; i++)
        {
            var paramSyntax = syntax.Parameters[i];
            var paramName = paramSyntax.Identifier.Tag;
            var paramType = ResolveType(paramSyntax.Type, syntax, scope) ?? ScriptType.Int;
            var parameter = new ParamSymbol(paramName, paramType, parameters.Count);
            parameter.SlotIndex = parameters.Count;
            parameters.Add(parameter);
        }

        var returnType = ResolveType(syntax.ReturnType, syntax, scope) ?? ScriptType.Void;
        var function = new FunctionSymbol(syntax.Name, parameters.ToImmutable(), returnType,
            libraryName: syntax.Library,
            externalName: syntax.ExportName != syntax.Name ? syntax.ExportName : null);
        function.LocalSlotCount = parameters.Count;

        if (BuiltinFunctions.GetAll().Any(b => b.Name == syntax.Name.ToUpper()))
        {
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
            return;
        }

        if (!scope.TryDeclareFunction(function))
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
    }

    private void CollectStructDeclaration(StructDeclBlock syntax, BoundScope scope)
    {
        if (scope.TryLookupStruct(syntax.Header.Name) != null)
        {
            _diagnostics.ReportBadStruct(syntax.Location, $"结构体 {syntax.Header.Name} 已定义");
            return;
        }

        var def = new EcsStructDef { Name = syntax.Header.Name };

        foreach (var field in syntax.Fields)
        {
            var fieldType = ResolveTypeFromName(field.TypeName, scope);
            if (fieldType is null)
            {
                _diagnostics.ReportBadStruct(field.Location, $"未知字段类型 {field.TypeName}");
                continue;
            }

            def.Fields.Add(new EcsFieldDef
            {
                Name = field.Name[1..],  // 去掉 '$' 前缀
                FieldType = fieldType,
            });
        }

        StructLayout.Calculate(def);
        scope.TryDeclareStruct(syntax.Header.Name, def);
    }

    #region 类型解析（复制自 Binder 的 LookupType 逻辑）

    private ScriptType? ResolveType(TypeClauseSyntax? tcs, Statement syntax, BoundScope scope)
    {
        if (tcs == null) return null;
        var type = ResolveTypeFromName(tcs.TypeName, scope);
        if (type is null)
            _diagnostics.ReportUnknownType(syntax.Location, tcs.Identifier);
        return type;
    }

    private static ScriptType? ResolveTypeFromName(string name, BoundScope scope)
    {
        var upper = name.ToUpper();
        if (upper.EndsWith(']'))
        {
            var openBracket = upper.LastIndexOf('[');
            if (openBracket > 0)
            {
                var baseName = name[..openBracket];
                var inner = upper[(openBracket + 1)..^1];
                var elem = ResolveTypeFromName(baseName, scope);
                if (elem is null) return null;
                var count = int.TryParse(inner, out var c) ? c : 0;
                return new ArrayType(elem, count);
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
            _ => scope.TryLookupStruct(name) is { } def ? new StructType(def) : null,
        };
    }

    #endregion

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null);
        result.TryDeclareStruct("Pixel", BuiltinFunctions.PixelStructDef);
        foreach (var f in BuiltinFunctions.GetAll())
            result.TryDeclareFunction(f);
        return result;
    }
}