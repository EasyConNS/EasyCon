using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Resolution;

internal readonly record struct ImportedInterface(ModuleInterface Iface, string? Alias);

/// <summary>
/// 接口后端的 ResolutionResult 合成（M3，docs/ModuleSystem.md §5.3）：
/// 消费者绑定只读依赖的 ModuleInterface，合成与源码收集（DeclarationCollector）等价的
/// GlobalScope / ModuleScopes——Cardelli 完备性由接口区承载（§4.6 清单）。
///
/// 与源码路径的形状对齐：
/// - Trees 仅含本模块树 → Binder 的模块单树绑定（v1 lib/main 双阶段已退役）；
/// - AliasedTrees 为空 → alias 过滤的源码树比对退化为 no-op；
/// - 接口函数符号 Declaration=null → EnsureFunctionBodyBound 自动跳过绑体，
///   调用点生成 Call，函数体由链接期提供（VM2 外部调用标记，M5）；
/// - 非 alias 接口导出注册进 GlobalScope（导入序遮蔽，first-wins）；
///   alias 接口注册进独立 ModuleScope，只能 ns.func() 限定访问（§6）。
/// </summary>
internal static class InterfaceScopeSynthesizer
{
    public static (ResolutionResult Resolution, IReadOnlySet<FunctionSymbol> ExternalFunctions) Synthesize(
        SyntaxTree mainTree, ImmutableArray<ImportedInterface> imports,
        bool includeCaptureHoles = false)
    {
        var diagnostics = new DiagnosticBag();
        var globalScope = DeclarationCollector.CreateRootScope();

        // lib 模块可见采集卡洞函数（对齐 v1 libOnlyScope）；main 模块与 v1 一致不注入
        if (includeCaptureHoles)
            foreach (var hole in Binding.BuiltinFunctions.GetCaptureHoles())
                globalScope.TryDeclareFunction(hole);

        // ---- 结构体：两遍注册（字段类型可引用其他接口结构体），布局消费端重算 ----
        var pending = new List<(ExportedStruct St, EcsStructDef Def)>();
        foreach (var (iface, _) in imports)
        {
            foreach (var s in iface.Structs)
            {
                if (globalScope.TryLookupStruct(s.Name) is not null)
                    continue;   // 首匹配优先（镜像导入遮蔽语义，§6）
                var def = new EcsStructDef { Name = s.Name };
                globalScope.TryDeclareStruct(s.Name, def);
                pending.Add((s, def));
            }
        }
        foreach (var (s, def) in pending)
        {
            foreach (var f in s.Fields)
            {
                var fieldType = DeclarationCollector.ResolveTypeFromName(f.TypeName, globalScope);
                if (fieldType is null)
                {
                    diagnostics.ReportBadStruct(default, $"接口结构体 {s.Name} 字段 {f.Name} 类型无法解析: {f.TypeName}");
                    continue;
                }
                def.Fields.Add(new EcsFieldDef { Name = f.Name, FieldType = fieldType });
            }
            StructLayout.Calculate(def);
        }

        // ---- 本模块源码树自身声明：v1 该树经 DeclarationCollector 收集，模块模式不走
        // Resolution，结构体必须在此补声明（字段可引用接口结构体，两遍注册）。
        // 同名结构体已被接口声明（first-match）时跳过，与 §2.2 首匹配遮蔽语义一致。
        var ownPending = new List<(StructDeclBlock Syntax, EcsStructDef Def)>();
        foreach (var member in mainTree.Root.Members)
        {
            if (member is not StructDeclBlock sd || globalScope.TryLookupStruct(sd.Header.Name) is not null)
                continue;
            var def = new EcsStructDef { Name = sd.Header.Name };
            globalScope.TryDeclareStruct(sd.Header.Name, def);
            ownPending.Add((sd, def));
        }
        foreach (var (sd, def) in ownPending)
        {
            foreach (var f in sd.Fields)
            {
                var fieldType = DeclarationCollector.ResolveTypeFromName(f.TypeName, globalScope);
                if (fieldType is null)
                {
                    diagnostics.ReportBadStruct(f.Location, $"未知字段类型 {f.TypeName}");
                    continue;
                }
                def.Fields.Add(new EcsFieldDef { Name = f.Name[1..], FieldType = fieldType });
            }
            StructLayout.Calculate(def);
        }

        // ---- 函数/extern 与内建名冲突（v1 由 DeclarationCollector 报告，模块模式在此补齐）----
        foreach (var member in mainTree.Root.Members)
        {
            var name = member switch
            {
                FuncDeclBlock func => func.Declare.Name,
                ExternFuncStmt ext => ext.Name,
                _ => null,
            };
            if (name != null && BuiltinFunctions.GetAll().Any(b => b.Name == name.ToUpper()))
                diagnostics.ReportFunctionAlreadyDeclared(member.Location, name);
        }

        // ---- 函数：非 alias → GlobalScope；alias → 独立 ModuleScope ----
        var moduleScopes = ImmutableDictionary.CreateBuilder<string, BoundScope>(StringComparer.OrdinalIgnoreCase);
        var externalFunctions = new HashSet<FunctionSymbol>();
        foreach (var (iface, alias) in imports)
        {
            var target = alias is null ? globalScope : new BoundScope(globalScope);
            foreach (var f in iface.Functions)
            {
                var symbol = DeclareExport(target, f, diagnostics);
                if (symbol is not null)
                    externalFunctions.Add(symbol);
            }
            if (alias is not null)
                moduleScopes[alias] = target;
        }

        return (
            new ResolutionResult(
                [mainTree],
                globalScope,
                moduleScopes.ToImmutable(),
                ImmutableDictionary<string, SyntaxTree>.Empty,
                diagnostics),
            externalFunctions);
    }

    static FunctionSymbol? DeclareExport(BoundScope scope, ExportedFunction f, DiagnosticBag diagnostics)
    {
        var parameters = ImmutableArray.CreateBuilder<ParamSymbol>();
        foreach (var p in f.Params)
        {
            var ptype = DeclarationCollector.ResolveTypeFromName(p.TypeName, scope);
            if (ptype is null)
            {
                ptype = ScriptType.Int;
                diagnostics.ReportBadStruct(default, $"接口函数 {f.Name} 参数 {p.Name} 类型无法解析: {p.TypeName}");
            }
            var parameter = new ParamSymbol(p.Name, ptype, p.Ordinal, p.HasDefault, p.DefaultValue);
            parameter.SlotIndex = parameters.Count;
            parameters.Add(parameter);
        }

        var returnType = DeclarationCollector.ResolveTypeFromName(f.ReturnTypeName, scope) ?? ScriptType.Void;
        var symbol = f.IsExtern
            ? new FunctionSymbol(f.Name, parameters.ToImmutable(), returnType,
                libraryName: f.ExternLibrary ?? "internal",
                externalName: f.ExternalName)
            : new FunctionSymbol(f.Name, parameters.ToImmutable(), returnType);
        symbol.LocalSlotCount = parameters.Count;
        // Declaration 保持 null：EnsureFunctionBodyBound 跳过绑体（Binder.cs:252 既有机制）

        scope.TryDeclareFunction(symbol);   // 同签名冲突 first-wins（导入遮蔽，§6）
        return symbol;
    }
}