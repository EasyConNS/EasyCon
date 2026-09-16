using EasyCon.Script.Bytecode;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Script.Modules;

/// <summary>独立编译模式的项目结果（docs/ModuleSystem.md M4–M6）。</summary>
public sealed class ModuleProjectResult
{
    public bool Success;
    public EcxImage? Image;
    /// <summary>链接序产物（std → vision → 依赖拓扑序 → main）。</summary>
    public List<ModuleArtifact> Artifacts = new();
    /// <summary>结构化诊断（错误阻断编译；模块归属由 Location.FileName 区分，§7.3 重放除外）。</summary>
    public List<Diagnostic> Diagnostics = new();
    /// <summary>警告（不阻断编译；MD_AMBIGUOUS_EXPORT 等，§6）。</summary>
    public List<string> Warnings = new();
    public int CacheHits;
    public int CacheMisses;
    /// <summary>错误缓存命中次数（§7.3 重放）。</summary>
    public int ErrorHits;
    /// <summary>进程级产物缓存命中/未命中次数（仅 UseDiskCache=false 时计数）。</summary>
    public int ProcessCacheHits;
    public int ProcessCacheMisses;
    /// <summary>缓存 GC 删除的过期文件数（§7.3）。</summary>
    public int GarbageCollected;
    /// <summary>全项目编译耗时聚合（逐模块累加）。</summary>
    public CompilationTiming Timing { get; } = new();
    /// <summary>主脚本语法树（format/ToCode 用）。</summary>
    public SyntaxTree? MainTree;
    /// <summary>main 模块 SSA（现编路径恒有；DumpIr 用）。</summary>
    public SsaProgram? Program;
    /// <summary>全项目 extern 符号并集（FFI 原生按名分发的类型来源）。</summary>
    public ImmutableArray<FunctionSymbol> NativeSymbols = [];
}

/// <summary>
/// 项目级独立编译编排（docs/ModuleSystem.md §3.3/§5 + 统一链路 docs/Pipeline.md）：
///
///   main.ecs ─► ModuleGraphBuilder（IMPORT 递归展开 + 环检测 + lib/ 自动加载）──► 拓扑序
///                          │
///              ModuleCompilePipeline：obj/ 缓存命中 → 读 .ecm（接口区 + 代码区）（M4）
///                      未命中 → Synthesize(依赖接口) → 急切绑定 → SSA → 优化 →
///                               编码（外部调用 = 导入标记 / CallN 原生）→ 原子写回 obj/（M5）
///                          │
///              Link：类型表合并 + 导入解析 + &lt;main&gt;/&lt;init&gt; 合成（M6）──► EcxImage
///
/// std/vision 为隐式依赖（内嵌源码、模块名固定），先于一切用户模块编译；
/// lib/ 自动加载对齐 v1 ImportResolver 顺序语义（显式 import 之后、main 之前，全局可见无 alias）。
/// 桌面执行直接消费 EcxImage（可含宽槽旁表）；MCU 分发由 EcxWriter 校验并写出 ECX2 子集。
/// </summary>
public static class ProjectCompiler
{
    public const string StdModule = "std";
    public const string VisionModule = "vision";
    public const string MainModule = "main";

    /// <summary>从脚本文件编译（统一链路主入口；lib/ 自动加载 + obj/ 缓存按 options）。</summary>
    public static ModuleProjectResult CompileProject(string mainPath, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var result = new ModuleProjectResult();
        mainPath = Path.GetFullPath(mainPath);
        var scriptDir = Path.GetDirectoryName(mainPath)!;

        var sw = Stopwatch.StartNew();
        var mainTree = SyntaxTree.Load(mainPath);
        result.Timing.FileLoad += sw.Elapsed;

        return CompileCore(mainTree, scriptDir,
            options.ObjDir ?? Path.Combine(scriptDir, "obj"), options, result,
            allowLibAutoLoad: true);
    }

    /// <summary>从内存源码编译（单文件场景：无 lib/ 自动加载；磁盘缓存仅显式提供 objDir 时启用）。</summary>
    public static ModuleProjectResult CompileProject(SyntaxTree mainTree, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var result = new ModuleProjectResult();
        return CompileCore(mainTree, scriptDir: null,
            objDir: options.ObjDir ?? "", options, result,
            allowLibAutoLoad: false);
    }

    static ModuleProjectResult CompileCore(SyntaxTree mainTree, string? scriptDir, string objDir,
        CompileOptions options, ModuleProjectResult result, bool allowLibAutoLoad)
    {
        ModuleCache? cache = null;
        if (options.UseDiskCache && objDir.Length > 0)
            cache = new ModuleCache(objDir);

        // 主树语法错误：编译前短路（不建图、不触碰缓存统计）
        if (mainTree.Diagnostics.Where(d => d.IsError).ToList() is { Count: > 0 } mainTreeErrors)
        {
            result.Diagnostics.AddRange(mainTreeErrors);
            return result;
        }

        var diagnostics = new DiagnosticBag();
        var graph = ModuleGraphBuilder.Build(mainTree, scriptDir, allowLibAutoLoad, result.Timing, diagnostics,
            options.LegacySyntax);
        if (diagnostics.HasErrors())
        {
            result.Diagnostics.AddRange(diagnostics);
            return result;
        }
        result.Diagnostics.AddRange(diagnostics.Where(d => !d.IsError));   // 非致命提示透传

        var pipeline = new ModuleCompilePipeline(graph.Nodes, cache, options, result);
        if (!pipeline.CompileAll(graph.CompileOrder))
        {
            result.MainTree = mainTree;
            return result;
        }

        foreach (var node in graph.CompileOrder)
            result.Artifacts.Add(node.Artifact);

        // MD_AMBIGUOUS_EXPORT（§6）：同名同签名跨模块导出 → 警告，链接期首匹配实际采用先导入者
        var seenExports = new Dictionary<(string Name, int NParams), string>();
        foreach (var art in result.Artifacts)
        {
            foreach (var e in art.Exports)
            {
                if (e.Name.StartsWith('<'))
                    continue;   // $eval/内部函数不参与导出遮蔽判定
                if (seenExports.TryGetValue((e.Name, e.NParams), out var first))
                    result.Warnings.Add($"MD_AMBIGUOUS_EXPORT: 函数 {e.Name}/{e.NParams} 同时由 {first} 与 {art.Name} 导出，实际采用先导入者 {first}");
                else
                    seenExports[(e.Name, e.NParams)] = art.Name;
            }
        }

        // 缓存 GC（§7.3）：引用闭包之外且 mtime 超龄的产物/错误文件
        if (cache != null)
        {
            var keepFileNames = graph.CompileOrder
                .Where(n => pipeline.CacheKeys.ContainsKey(n.Name))
                .Select(n => ModuleCacheKeys.FileName(n.Name, pipeline.CacheKeys[n.Name]))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.GarbageCollected = cache.GarbageCollect(keepFileNames, options.GcMaxAge ?? TimeSpan.FromDays(30));
        }

        result.NativeSymbols = pipeline.NativeSymbols.ToImmutableArray();
        result.Image = EcxPipeline.Link(result.Artifacts,
            result.Artifacts.Any(a => a.KeyAction),
            result.Artifacts.Any(a => a.NeedIL));
        result.Success = true;
        result.MainTree = mainTree;
        return result;
    }
}