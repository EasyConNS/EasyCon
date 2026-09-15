using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Resolution;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Script.Modules;

/// <summary>
/// 逐模块编译管线（docs/ModuleSystem.md §5 + §7 缓存三层编排）：
/// 缓存键计算 → disk(.ecm)/process/.err(sidecar) 三路查找 → EnsureParsed（未命中才全量
/// parse）→ CompileModuleNode（接口合成 → 急切绑定 → SSA → 优化 → 编码）→ 落缓存；
/// 统计聚合（CacheHits/Misses、ProcessCacheHits/Misses、ErrorHits、Timing）。
/// 失败（parse/绑定/编码错误或错误重放命中）时诊断写入 result 并返回 false；成功返回 true。
/// </summary>
internal sealed class ModuleCompilePipeline
{
    readonly Dictionary<string, ModuleNode> _nodes;
    readonly ModuleCache? _cache;
    readonly CompileOptions _options;
    readonly ModuleProjectResult _result;
    readonly HashSet<FunctionSymbol> _nativeSymbols = new();
    readonly int _procHitsBefore;
    readonly int _procMissesBefore;
    readonly Dictionary<string, string> _cacheKeys = new(StringComparer.OrdinalIgnoreCase);

    public ModuleCompilePipeline(Dictionary<string, ModuleNode> nodes, ModuleCache? cache,
        CompileOptions options, ModuleProjectResult result)
    {
        _nodes = nodes;
        _cache = cache;
        _options = options;
        _result = result;
        _procHitsBefore = ProcessModuleCache.Hits;
        _procMissesBefore = ProcessModuleCache.Misses;
    }

    /// <summary>缓存 GC 引用闭包所需的 模块名 → cacheKey 映射。</summary>
    public IReadOnlyDictionary<string, string> CacheKeys => _cacheKeys;

    /// <summary>全项目 extern 符号并集（FFI 原生按名分发的类型来源）。</summary>
    public HashSet<FunctionSymbol> NativeSymbols => _nativeSymbols;

    /// <summary>按拓扑序编译全部模块；false = 已失败（诊断与统计已写入 result，调用方直接返回）。</summary>
    public bool CompileAll(List<ModuleNode> compileOrder)
    {
        var timing = _result.Timing;
        foreach (var node in compileOrder)
        {
            var depIfaces = node.Dependencies
                .Where(_nodes.ContainsKey)
                .Select(d => _nodes[d].Interface)
                .ToList();
            var cacheKey = ModuleCacheKeys.Compute(node.Source,
                depIfaces.Select(i => i.InterfaceHash).ToList(), ModuleInterface.CurrentCompilerVersion,
                _options.ProductFingerprint());
            _cacheKeys[node.Name] = cacheKey;

            ModuleArtifact? artifact = null;
            if (_cache != null)
            {
                artifact = _cache.TryLoad(node.Name, cacheKey);
                if (artifact != null && artifact.Interface!.Dependencies.Count != node.Dependencies.Count)
                    artifact = null;   // 依赖集合变化（防御；cacheKey 已覆盖，双保险）
            }
            else if (_options.UseProcessCache)
            {
                // 进程级产物缓存：仅现编路径（UseDiskCache=false）；命中反序列化出新实例，无别名共享
                artifact = ProcessModuleCache.TryLoad(node.Name, cacheKey);
                if (artifact != null && artifact.Interface!.Dependencies.Count != node.Dependencies.Count)
                    artifact = null;
            }

            // The ECM artifact records native names but not FunctionSymbol signatures.
            // An FFI cache hit would leave NativeSymbols empty, so the VM cannot
            // dispatch a subsequent run. Recompile only modules that call external
            // native functions until the artifact format carries those signatures.
            if (artifact != null && artifact.Natives.Any(n => n.Name.Contains('!')))
                artifact = null;

            if (artifact == null)
            {
                // 错误缓存重放（§7.3）：同 cacheKey 的历史编译失败 → 快速失败，不绑定/编码也不 parse
                if (_cache != null)
                {
                    var replay = _cache.TryLoadErrors(node.Name, cacheKey);
                    if (replay != null)
                    {
                        _result.Diagnostics.AddRange(replay.Select(line =>
                            DiagnosticBag.FromMessage(node.Tree, node.Name, line)));
                        return Fail();
                    }
                }

                // 缓存未命中才全量 parse（轻量导入扫描只建立图；此处拿到绑定所需语法树）
                if (node.Tree == null && !EnsureParsed(node))
                {
                    StoreErrors(node.Name, cacheKey);
                    return Fail();
                }

                var imports = ImportedList(node);
                artifact = CompileModuleNode(node, imports, isMain: node.Name == ProjectCompiler.MainModule);
                if (artifact == null)
                {
                    StoreErrors(node.Name, cacheKey);
                    return Fail();
                }
                _cache?.Store(artifact, cacheKey);
                if (_cache == null && _options.UseProcessCache)
                    ProcessModuleCache.Store(artifact, cacheKey);
            }
            node.Artifact = artifact;
            node.Interface = artifact.Interface!;
        }
        FillCacheStats();
        return true;
    }

    /// <summary>统计聚合（成功与失败路径共用；Cache 统计直接读缓存对象，Process 统计按差值）。</summary>
    void FillCacheStats()
    {
        _result.CacheHits = _cache?.Hits ?? 0;
        _result.CacheMisses = _cache?.Misses ?? 0;
        _result.ErrorHits = _cache?.ErrorHits ?? 0;
        _result.ProcessCacheHits = ProcessModuleCache.Hits - _procHitsBefore;
        _result.ProcessCacheMisses = ProcessModuleCache.Misses - _procMissesBefore;
    }

    bool Fail()
    {
        FillCacheStats();
        _result.Success = false;
        return false;
    }

    void StoreErrors(string name, string cacheKey)
    {
        if (_cache != null)
            _cache.StoreErrors(name, cacheKey, _result.Diagnostics
                .Where(d => d.IsError)
                .Select(d => d.Message).ToList());
    }

    /// <summary>依赖接口列表（别名随 IMPORT 语句；已 parse 走 AST，未 parse 补 parse 提取语句）。</summary>
    List<ImportedInterface> ImportedList(ModuleNode node)
    {
        var tree = node.Tree ?? SyntaxTree.Parse(SourceText.From(node.Source, node.Path), _options.LegacySyntax);
        var importStmts = tree.Root.Members.OfType<ImportStmt>()
            .ToDictionary(i => Path.GetFullPath(i.FullFileName), i => i);
        var list = new List<ImportedInterface>();
        foreach (var depName in node.Dependencies)
        {
            var dep = _nodes[depName];
            importStmts.TryGetValue(dep.Path, out var stmt);
            list.Add(new ImportedInterface(dep.Interface, stmt?.Alias?.Value));
        }
        return list;
    }

    /// <summary>缓存未命中后的全量 parse；语法错误写入 result 并返回 false（模块失败）。</summary>
    bool EnsureParsed(ModuleNode node)
    {
        // parse 耗时计入 LexingAndParsing（缓存命中路径恒为 0，是「零 parse」的可观测证据）
        var swParse = Stopwatch.StartNew();
        var tree = SyntaxTree.Parse(SourceText.From(node.Source, node.Path), _options.LegacySyntax);
        _result.Timing.LexingAndParsing += swParse.Elapsed;
        node.Tree = tree;
        if (tree.Diagnostics.Where(d => d.IsError).ToList() is { Count: > 0 } treeErrors)
        {
            _result.Diagnostics.AddRange(treeErrors);
            return false;
        }
        return true;
    }

    /// <summary>编译单个模块（M5 + 统一链路）：接口合成 → 急切绑定 → SSA → 优化（导出为根）→ 编码。
    /// 失败时结构化诊断写入 result，且不写产物缓存（错误缓存由调用方存字符串 sidecar）。</summary>
    ModuleArtifact? CompileModuleNode(ModuleNode node, List<ImportedInterface> importedIfaces,
        bool isMain)
    {
        var timing = _result.Timing;
        var moduleTree = node.Tree ?? SyntaxTree.Parse(SourceText.From(node.Source, node.Path), _options.LegacySyntax);
        node.Tree = moduleTree;
        try
        {
            // 解析作用域合成（M3）：依赖以接口区提供，本模块源码不经 IMPORT 解析；
            // lib 模块可见采集卡洞函数，main 与 v1 一致不注入。
            var sw = Stopwatch.StartNew();
            var (resolution, externalFunctions) = InterfaceScopeSynthesizer.Synthesize(
                moduleTree, importedIfaces.ToImmutableArray(),
                includeCaptureHoles: !isMain);
            var bound = Binder.BindProgram(resolution, _options.ExtVars, eagerBindMainFunctions: true,
                legacySyntax: _options.LegacySyntax);
            timing.Binding += sw.Elapsed;

            if (bound.Diagnostics.HasErrors())
            {
                _result.Diagnostics.AddRange(bound.Diagnostics);
                return null;
            }
            foreach (var ext in bound.ExternFunctions)
                _nativeSymbols.Add(ext);

            sw.Restart();
            var ssa = SsaProgramBuilder.Build(bound);
            timing.SsaBuild += sw.Elapsed;

            sw.Restart();
            if (_options.Optimize)
                SsaOptimizer.Optimize(ssa, moduleRoots: ssa.Functions.Keys);
            timing.SsaOptimize += sw.Elapsed;

            if (isMain)
                _result.Program = ssa;

            bool hasInit = isMain || ModuleInterfaceBuilder.FromSyntaxTree(moduleTree, node.Name).HasInit;
            var artifact = EcxModuleEncoder.CompileWholeProgramAsModule(ssa, node.Name, externalFunctions,
                hasInit: hasInit, isMain: isMain);
            artifact.Ssa = _options.KeepSsa ? ssa : null;

            // 接口区：声明收集层提取；依赖表 = 图边（含隐式 std/vision，§4.4）
            var deps = importedIfaces.Select(i => new ModuleDependency
            {
                Name = i.Iface.Name,
                InterfaceHash = i.Iface.InterfaceHash,
            }).ToList();
            artifact.Interface = ModuleInterfaceBuilder.FromSyntaxTree(moduleTree, node.Name, deps);
            return artifact;
        }
        catch (Exception ex)
        {
            _result.Diagnostics.Add(DiagnosticBag.FromMessage(node.Tree, node.Name, ex.Message));
            return null;
        }
    }
}