using EasyCon.Script.Binding;
using System.Collections.Concurrent;
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

    /// <summary>按拓扑波次编译全部模块（M3）：同波模块互不依赖 → 波内并行；
    /// main 的层级严格最大（IMPORT 闭包）恒末波。false = 已失败（诊断与统计已写入 result）。
    /// 线程模型：每模块独立诊断 sink（波末并入全局）+ 状态锁（诊断/计时/缓存键/extern 并集）；
    /// 产物互不共享（缓存命中反序列化新实例），缓存计数 Interlocked。</summary>
    public bool CompileAll(List<ModuleNode> compileOrder)
    {
        // 拓扑分波（Kahn 层级）：level(n) = 1 + max(level(dep))；依赖接口在同波之前已就绪
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in compileOrder)
        {
            var level = 0;
            foreach (var dep in node.Dependencies)
                if (levels.TryGetValue(dep, out var depLevel))
                    level = Math.Max(level, depLevel + 1);
            levels[node.Name] = level;
        }

        var failedModules = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var wave in compileOrder.GroupBy(n => levels[n.Name])
                     .OrderBy(g => g.Key)
                     .Select(g => g.ToList()))
        {
            if (wave.Count == 1)
            {
                CompileOne(wave[0], failedModules);
                continue;
            }
            Parallel.ForEach(wave, node => CompileOne(node, failedModules));
        }

        if (!failedModules.IsEmpty)
            return Fail();
        FillCacheStats();
        return true;
    }

    /// <summary>编译单个模块（缓存键 → 三路缓存查找 → 错误重放/parse → 编码 → 落缓存）。
    /// 返回 false = 本模块失败（诊断进 <paramref name="sink"/>，调用方波末并入全局；
    /// 失败标记进 <paramref name="failedModules"/> 供依赖者级联拦截）。</summary>
    bool CompileOne(ModuleNode node, ConcurrentDictionary<string, byte> failedModules)
    {
        // M2 级联容错：依赖模块编译失败 → 级联诊断拦截（不进绑定，避免符号错误风暴），
        // 其余无关模块照常编译——一次编译收集全部模块的诊断（SCRIPT_MODERNIZATION_PLAN.md）。
        var failedDeps = node.Dependencies.Where(failedModules.ContainsKey).ToList();
        if (failedDeps.Count > 0)
        {
            var cascade = new DiagnosticBag();
            cascade.Add(Diagnostic.Error(
                Modules.ModuleLocations.Default(node.Tree), $"[{node.Name}] 依赖模块编译失败，已跳过: {string.Join(", ", failedDeps)}",
                DiagnosticCodes.DependencyFailed));
            MergeDiagnostics(cascade);
            failedModules.TryAdd(node.Name, 0);
            return false;
        }
        var sink = new DiagnosticBag();

        string sourceContext = ModuleCacheKeys.SourceContext(node.Path);
        var depIfaces = node.Dependencies
            .Where(_nodes.ContainsKey)
            .Select(d => _nodes[d].Interface)
            .ToList();
        var cacheKey = ModuleCacheKeys.Compute(node.Source,
            depIfaces.Select(i => i.InterfaceHash).ToList(), ModuleInterface.CurrentCompilerVersion,
            _options.ProductFingerprint(), sourceContext);
        lock (_stateLock)
        {
            _cacheKeys[node.Name] = cacheKey;
        }

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

        // FFI 缓存限制（挂账）：.ecm 只记录原生名不带 FunctionSymbol 签名——缓存命中会使
        // NativeSymbols 缺失、运行期无法分发。检测面 = Natives 表 '!' 名（L3 名表 FFI）
        // + 接口区 ExternLibrary（EXTERN FROM，经接口传递的 FFI——M3 前仅前者，遮蔽场景间歇暴露）。
        // 根修 = 产物格式携带签名，另批。
        if (artifact != null && (artifact.Natives.Any(n => n.Name.Contains('!'))
            || artifact.Interface!.Functions.Any(f => !string.IsNullOrEmpty(f.ExternLibrary))))
            artifact = null;

        if (artifact == null)
        {
            // 错误缓存重放（§7.3）：同 cacheKey 的历史编译失败 → 不绑定/编码也不 parse
            if (_cache != null)
            {
                var replay = _cache.TryLoadErrors(node.Name, cacheKey);
                if (replay != null)
                {
                    sink.AddRange(replay.Select(line =>
                        DiagnosticBag.FromMessage(node.Tree, node.Name, line)));
                    MergeDiagnostics(sink);
                    failedModules.TryAdd(node.Name, 0);
                    return false;
                }
            }

            // 缓存未命中才全量 parse（轻量导入扫描只建立图；此处拿到绑定所需语法树）
            if (node.Tree == null && !EnsureParsed(node, sink))
            {
                StoreErrors(sink, node.Name, cacheKey);
                MergeDiagnostics(sink);
                failedModules.TryAdd(node.Name, 0);
                return false;
            }

            var imports = ImportedList(node);
            artifact = CompileModuleNode(node, imports, sink, isMain: node.Name == ProjectCompiler.MainModule);
            if (artifact == null)
            {
                StoreErrors(sink, node.Name, cacheKey);
                MergeDiagnostics(sink);
                failedModules.TryAdd(node.Name, 0);
                return false;
            }
            _cache?.Store(artifact, cacheKey);
            if (_cache == null && _options.UseProcessCache)
                ProcessModuleCache.Store(artifact, cacheKey);
        }
        node.Artifact = artifact;
        node.Interface = artifact.Interface!;
        MergeDiagnostics(sink);
        return true;
    }

    /// <summary>并行聚合锁：诊断 / 计时 / 缓存键 / extern 并集（波内多模块竞争点）。</summary>
    readonly object _stateLock = new();

    void MergeDiagnostics(DiagnosticBag sink)
    {
        lock (_stateLock)
            _result.Diagnostics.AddRange(sink);
    }

    void StoreErrors(DiagnosticBag sink, string name, string cacheKey)
    {
        if (_cache != null)
            _cache.StoreErrors(name, cacheKey, sink
                .Where(d => d.IsError)
                .Select(d => d.Message).ToList());
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
        var tree = node.Tree ?? SyntaxTree.Parse(SourceText.From(node.Source, node.Path), _options.LegacySyntax,
            node.LibRoot);
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
    bool EnsureParsed(ModuleNode node, DiagnosticBag sink)
    {
        // parse 耗时计入 LexingAndParsing（缓存命中路径恒为 0，是「零 parse」的可观测证据）
        var swParse = Stopwatch.StartNew();
        var tree = SyntaxTree.Parse(SourceText.From(node.Source, node.Path), _options.LegacySyntax,
            node.LibRoot);
        lock (_stateLock)
            _result.Timing.LexingAndParsing += swParse.Elapsed;
        node.Tree = tree;
        if (tree.Diagnostics.Where(d => d.IsError).ToList() is { Count: > 0 } treeErrors)
        {
            sink.AddRange(treeErrors);
            return false;
        }
        return true;
    }

    /// <summary>编译单个模块（M5 + 统一链路）：接口合成 → 急切绑定 → SSA → 优化（导出为根）→ 编码。
    /// 失败时结构化诊断写入 result，且不写产物缓存（错误缓存由调用方存字符串 sidecar）。</summary>
    ModuleArtifact? CompileModuleNode(ModuleNode node, List<ImportedInterface> importedIfaces,
        DiagnosticBag sink, bool isMain)
    {
        var timing = _result.Timing;
        var moduleTree = node.Tree ?? SyntaxTree.Parse(SourceText.From(node.Source, node.Path), _options.LegacySyntax,
            node.LibRoot);
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
            lock (_stateLock)
                timing.Binding += sw.Elapsed;

            if (bound.Diagnostics.HasErrors())
            {
                sink.AddRange(bound.Diagnostics);
                return null;
            }
            lock (_stateLock)
                foreach (var ext in bound.ExternFunctions)
                    _nativeSymbols.Add(ext);

            sw.Restart();
            var ssa = SsaProgramBuilder.Build(bound);
            lock (_stateLock)
                timing.SsaBuild += sw.Elapsed;

            sw.Restart();
            if (_options.Optimize)
                SsaOptimizer.Optimize(ssa, moduleRoots: ssa.Functions.Keys);
            lock (_stateLock)
                timing.SsaOptimize += sw.Elapsed;

            SsaUseDefValidator.ValidateProgram(ssa);

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
            sink.Add(DiagnosticBag.FromMessage(node.Tree, node.Name, ex.Message));
            return null;
        }
    }
}