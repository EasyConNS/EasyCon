using EasyCon.Script.Binding;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Resolution;

/// <summary>
/// Resolution 编译阶段的入口。
/// 编排 ImportResolver（文件加载）和 DeclarationCollector（符号声明收集）。
/// </summary>
internal sealed class Resolver
{
    /// <summary>
    /// 执行 Resolution 阶段，返回 ResolutionResult。
    /// </summary>
    public ResolutionResult Resolve(SyntaxTree mainTree)
    {
        var diagnostics = new DiagnosticBag();

        // Phase 1: 文件加载 + import 解析 + 循环检测
        var importResolver = new ImportResolver();
        var (trees, aliasedTrees, importDiags) = importResolver.Resolve(mainTree);
        diagnostics.AddRange(importDiags);

        // 有致命错误时跳过声明收集
        if (diagnostics.HasErrors())
            return new ResolutionResult(trees, null, ImmutableDictionary<string, Binding.BoundScope>.Empty, diagnostics);

        // Phase 2: 收集顶层声明 → GlobalScope + ModuleScopes
        var collector = new DeclarationCollector();
        var (globalScope, moduleScopes, _, declDiags) = collector.Collect(trees, aliasedTrees);
        diagnostics.AddRange(declDiags);

        return new ResolutionResult(trees, globalScope, moduleScopes, diagnostics);
    }
}