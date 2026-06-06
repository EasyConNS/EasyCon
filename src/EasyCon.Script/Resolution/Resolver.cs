using EasyCon.Script.Binding;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;

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
    public ResolutionResult Resolve(SyntaxTree mainTree, CompilationTiming timing)
    {
        var diagnostics = new DiagnosticBag();

        // Phase 1: 文件加载 + import 解析 + 循环检测
        var importResolver = new ImportResolver();
        var (trees, aliasedTrees, importDiags) = importResolver.Resolve(mainTree, timing);
        diagnostics.AddRange(importDiags);

        // 有致命错误时跳过声明收集
        if (diagnostics.HasErrors())
            return new ResolutionResult(trees, null,
                ImmutableDictionary<string, Binding.BoundScope>.Empty,
                aliasedTrees, diagnostics);

        // Phase 2: 收集顶层声明 → GlobalScope + ModuleScopes
        var sw = Stopwatch.StartNew();
        var collector = new DeclarationCollector();
        var (globalScope, moduleScopes, _, declDiags) = collector.Collect(trees, aliasedTrees);
        timing.DeclarationCollect = sw.Elapsed;
        diagnostics.AddRange(declDiags);

        return new ResolutionResult(trees, globalScope, moduleScopes, aliasedTrees, diagnostics);
    }
}