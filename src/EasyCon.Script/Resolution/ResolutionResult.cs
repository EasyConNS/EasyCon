using EasyCon.Script.Binding;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Resolution;

/// <summary>
/// Resolution 阶段的产出：包含解析后的 SyntaxTree 序列、全局作用域、模块作用域和诊断信息。
/// </summary>
internal sealed class ResolutionResult
{
    public static readonly ResolutionResult Empty = new([], null,
        ImmutableDictionary<string, BoundScope>.Empty,
        ImmutableDictionary<string, SyntaxTree>.Empty,
        new DiagnosticBag());

    public ResolutionResult(
        ImmutableArray<SyntaxTree> trees,
        BoundScope? globalScope,
        ImmutableDictionary<string, BoundScope> moduleScopes,
        ImmutableDictionary<string, SyntaxTree> aliasedTrees,
        DiagnosticBag diagnostics)
    {
        Trees = trees;
        GlobalScope = globalScope;
        ModuleScopes = moduleScopes;
        AliasedTrees = aliasedTrees;
        Diagnostics = diagnostics;
    }

    /// <summary>按加载顺序排列的 SyntaxTree（stdlib → imports → lib/ → main）。</summary>
    public ImmutableArray<SyntaxTree> Trees { get; }

    /// <summary>全局作用域，包含所有全局可见的符号声明（Phase 2 填充）。</summary>
    public BoundScope? GlobalScope { get; }

    /// <summary>命名空间模块作用域：alias → 包含该模块符号的 BoundScope（Phase 2 填充）。</summary>
    public ImmutableDictionary<string, BoundScope> ModuleScopes { get; }

    /// <summary>带别名的 import 映射：alias → SyntaxTree。这些模块的符号只能通过命名空间限定访问。</summary>
    public ImmutableDictionary<string, SyntaxTree> AliasedTrees { get; }

    /// <summary>Resolution 阶段产生的诊断信息。</summary>
    public DiagnosticBag Diagnostics { get; }
}