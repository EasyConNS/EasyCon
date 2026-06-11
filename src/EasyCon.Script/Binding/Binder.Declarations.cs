using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed partial class Binder
{
    #region 声明绑定

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

            var parameter = new ParamSymbol(parameterName, parameterType, parameters.Count);
            parameter.SlotIndex = parameters.Count;
            parameters.Add(parameter);
        }

        var returnType = BindTypeClause(syntax.Declare, syntax.Declare.Type) ?? ScriptType.Void;

        // 尝试复用 DeclarationCollector 已创建的 FunctionSymbol 实例，
        // 避免 Binder 和 DeclarationCollector 各持一份实例导致 Layout 等运行时属性不一致。
        var existing = TryFindMatchingFunction(syntax.Declare.Name, parameters);
        if (existing != null)
        {
            if (!_scope.TryDeclareFunction(existing))
                _diagnostics.ReportFunctionAlreadyDeclared(syntax.Declare.Location, syntax.Declare.Name);
            return existing;
        }

        // 兜底：DeclarationCollector 未创建过（如旧 API 路径），创建新实例
        var function = new FunctionSymbol(syntax.Declare.Name, parameters.ToImmutable(), returnType) { Declaration = syntax };
        function.LocalSlotCount = parameters.Count;

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

        // 尝试复用 DeclarationCollector 已创建的 FunctionSymbol 实例
        var existing = TryFindMatchingFunction(syntax.Name, parameters);
        if (existing != null)
        {
            if (!_scope.TryDeclareFunction(existing))
                _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
            return existing;
        }

        // 兜底：DeclarationCollector 未创建过
        var function = new FunctionSymbol(syntax.Name, parameters.ToImmutable(), returnType, libraryName: syntax.Library, externalName: syntax.ExportName != syntax.Name ? syntax.ExportName : null);
        function.LocalSlotCount = parameters.Count;

        if (BuiltinFunctions.GetAll().Any(b => b.Name == syntax.Name.ToUpper()))
        {
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
            return function;
        }

        if (!_scope.TryDeclareFunction(function))
            _diagnostics.ReportFunctionAlreadyDeclared(syntax.Location, syntax.Name);
        return function;
    }

    /// <summary>
    /// 从 DeclarationCollector 创建的 scope（GlobalScope + ModuleScopes）中查找与目标签名匹配的 FunctionSymbol。
    /// 复用同一实例确保 Layout 等运行时属性在整个管线中保持一致。
    /// </summary>
    private FunctionSymbol? TryFindMatchingFunction(string name, ImmutableArray<ParamSymbol>.Builder parameters)
    {
        if (_resolution == null) return null;

        // 依次搜索 GlobalScope（非 aliased 函数）和 ModuleScopes（aliased import 函数）
        List<BoundScope?> scopes = [_resolution.GlobalScope];
        if (_resolution.ModuleScopes != null)
            scopes.AddRange(_resolution.ModuleScopes.Values);

        foreach (var scope in scopes)
        {
            if (scope == null) continue;
            foreach (var candidate in scope.TryLookupFuncs(name))
            {
                if (candidate.Parameters.Length != parameters.Count) continue;
                bool match = true;
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (!candidate.Parameters[i].Type.Equals(parameters[i].Type))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return candidate;
            }
        }
        return null;
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

    #endregion

    #region 变量声明与类型查找

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
        if (_function == null && allowGlobal)
        {
            var gvar = new GlobalVariableSymbol(syntax.Tag, isReadOnly, type);
            _scope.TryDeclareVariable(gvar);
            return gvar;
        }

        var variable = new LocalVariableSymbol(syntax.Tag, isReadOnly, type);
        _scope.TryDeclareVariable(variable);
        return variable;
    }

    private VariableSymbol LookupVariable(ConstVarExpr syntax, bool isReadOnly, ScriptType type, bool allowGlobal = true)
    {
        if (_function == null && allowGlobal)
        {
            var gvar = new GlobalVariableSymbol(syntax.Tag, isReadOnly, type);
            _scope.TryDeclareVariable(gvar);
            return gvar;
        }

        var variable = new LocalVariableSymbol(syntax.Tag, isReadOnly, type);
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
            _ => _scope.TryLookupStruct(name) is { } def ? new StructType(def) : null,
        };
    }

    #endregion
}