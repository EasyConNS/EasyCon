using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using System.Collections.Immutable;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 阶段 B（编译体验）回归：缓存键纳入影响产物的 CompileOptions（Optimize/ExtVars）、
/// 图构建轻量导入扫描（Lexer 令牌，缓存命中路径零 parse）、进程级产物缓存
/// （仅 UseDiskCache=false 现编路径；命中反序列化新实例，杜绝别名共享）。
/// </summary>
[TestFixture]
public class ModuleCacheOptimizationTests
{
    string _dir = null!;
    string _objDir = null!;

    const string UtilsSource = """
        FUNC twice($x:INT):INT
            RETURN $x * 2
        ENDFUNC
        """;

    const string MainSource = """
        IMPORT "utils.ecs"
        $r = twice(21)
        PRINT $r
        """;

    [SetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"EcsCacheOpt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "lib"));
        _objDir = Path.Combine(_dir, "obj");
        ProcessModuleCache.Clear();   // 静态缓存：测试间隔离保证断言确定
    }

    [TearDown]
    public void TearDown()
    {
        ProcessModuleCache.Clear();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    void WriteProject()
    {
        File.WriteAllText(Path.Combine(_dir, "lib", "utils.ecs"), UtilsSource);
        File.WriteAllText(Path.Combine(_dir, "main.ecs"), MainSource);
    }

    static EcxHost RecordedHost()
    {
        var host = new EcxHost();
        host.EnableRecording();
        return host;
    }

    // ---------- 缓存键：Optimize / ExtVars 进键 ----------

    [Test]
    public void CacheKey_OptimizeFlag_IsolatesEntries()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        // run1：optimize=true 全量落盘
        var run1 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir, Optimize = true });
        Assert.That(run1.Success, Is.True, string.Join("\n", run1.Diagnostics));
        Assert.That(run1.CacheMisses, Is.GreaterThanOrEqualTo(3));

        // run2：optimize=false 不得命中 optimize=true 的缓存（字节码产物不同）
        var run2 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir, Optimize = false });
        Assert.That(run2.Success, Is.True, string.Join("\n", run2.Diagnostics));
        Assert.That(run2.CacheHits, Is.EqualTo(0), "optimize:false 不得命中 optimize:true 的缓存条目");
        Assert.That(run2.CacheMisses, Is.GreaterThanOrEqualTo(3), "optimize 切换应全量重编");

        // run3：optimize=false 自身可命中
        var run3 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir, Optimize = false });
        Assert.That(run3.CacheMisses, Is.EqualTo(0), "同 Optimize 值应全量命中");

        // run4：切回 true 只命中自己的 O=true 条目（run1 落盘、run2 键不同未覆盖）
        var run4 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir, Optimize = true });
        Assert.That(run4.CacheMisses, Is.EqualTo(0), "O=true 条目仍应存在且可命中");
        // O=true 与 O=false 两套条目共存于 obj/（4 模块 × 2 键），证明键隔离产生独立条目
        Assert.That(Directory.GetFiles(_objDir, "*.ecm").Length, Is.EqualTo(8),
            "Optimize 两个取值应产生两套互不覆盖的缓存条目");
    }

    [Test]
    public void CacheKey_ExtVars_IsolateEntries()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        var run1 = ProjectCompiler.CompileProject(mainPath, new CompileOptions
        {
            ObjDir = _objDir,
            ExtVars = ImmutableHashSet.Create("enemy"),
        });
        Assert.That(run1.Success, Is.True, string.Join("\n", run1.Diagnostics));

        // 相同 ExtVars → 命中
        var run2 = ProjectCompiler.CompileProject(mainPath, new CompileOptions
        {
            ObjDir = _objDir,
            ExtVars = ImmutableHashSet.Create("enemy"),
        });
        Assert.That(run2.CacheMisses, Is.EqualTo(0), "相同 ExtVars 应全量命中");

        // 不同 ExtVars → 键变（ExtVars 改变绑定与 IL 名，进产物）
        var run3 = ProjectCompiler.CompileProject(mainPath, new CompileOptions
        {
            ObjDir = _objDir,
            ExtVars = ImmutableHashSet.Create("ally"),
        });
        Assert.That(run3.CacheHits, Is.EqualTo(0), "不同 ExtVars 不得互相命中");
        Assert.That(run3.Success, Is.True);
    }

    // ---------- 轻量导入扫描：缓存命中路径零 parse ----------

    [Test]
    public void CacheHit_SkipsParsing_TimingEvidence()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        // 冷跑：std/vision/utils 依赖模块全量 parse（main 计入 FileLoad）
        var cold = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(cold.Success, Is.True, string.Join("\n", cold.Diagnostics));
        Assert.That(cold.Timing!.LexingAndParsing, Is.GreaterThan(TimeSpan.Zero),
            "冷跑应存在依赖模块 parse 耗时");

        // 热跑：全部缓存命中 → 无任何模块 parse（LexingAndParsing 恒为 0）；
        // main 树恒 parse（MainTree/格式化需要），计入 FileLoad
        var warm = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(warm.Success, Is.True);
        Assert.That(warm.CacheMisses, Is.EqualTo(0));
        Assert.That(warm.Timing!.LexingAndParsing, Is.EqualTo(TimeSpan.Zero),
            "缓存全命中路径不得 parse 任何模块");

        // 热缓存镜像行为与冷跑一致
        var hostCold = RecordedHost();
        Assert.That(EcxInterpreter.Run(cold.Image!, hostCold), Is.EqualTo(0));
        var hostWarm = RecordedHost();
        Assert.That(EcxInterpreter.Run(warm.Image!, hostWarm), Is.EqualTo(0));
        Assert.That(hostWarm.Lines, Is.EqualTo(hostCold.Lines));
        Assert.That(hostWarm.Lines, Is.EqualTo(new[] { "42" }));
        Assert.That(warm.MainTree, Is.Not.Null, "MainTree 恒可用");
    }

    [Test]
    public void LightScan_IgnoresCommentAndStringImports()
    {
        // 注释中的 IMPORT 与 PRINT 字符串里的 IMPORT 字样不得建立图边（无 ghost.ecs 也不报错）
        File.WriteAllText(Path.Combine(_dir, "lib", "utils.ecs"),
            "# IMPORT \"ghost.ecs\"\n" + UtilsSource);
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            MainSource + "\nPRINT \"IMPORT never.ecs here\"\n");

        var result = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { ObjDir = _objDir });
        Assert.That(result.Success, Is.True,
            "注释/字符串中的 IMPORT 不得触发导入解析：" + string.Join("; ", result.Diagnostics));
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("导入文件不存在")), Is.False);
    }

    // ---------- 进程级产物缓存 ----------

    [Test]
    public void ProcessCache_ColdHotIdenticalBehavior()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");
        var options = new CompileOptions { UseDiskCache = false };

        var cold = ProjectCompiler.CompileProject(mainPath, options);
        Assert.That(cold.Success, Is.True, string.Join("\n", cold.Diagnostics));
        var hot = ProjectCompiler.CompileProject(mainPath, options);

        Assert.That(hot.ProcessCacheHits, Is.GreaterThanOrEqualTo(3),
            "二次现编应命中进程缓存（std/vision/utils/main）");
        Assert.That(hot.ProcessCacheMisses, Is.EqualTo(0));
        Assert.That(hot.Timing!.Binding, Is.LessThan(cold.Timing!.Binding),
            "命中路径跳过绑定（耗时占位断言：热跑 Binding 为 0）");
        Assert.That(hot.Timing.Binding, Is.EqualTo(TimeSpan.Zero));

        // 冷/热行为逐字一致
        var hostCold = RecordedHost();
        Assert.That(EcxInterpreter.Run(cold.Image!, hostCold), Is.EqualTo(0));
        var hostHot = RecordedHost();
        Assert.That(EcxInterpreter.Run(hot.Image!, hostHot), Is.EqualTo(0));
        Assert.That(hostHot.Lines, Is.EqualTo(hostCold.Lines));
        Assert.That(hostHot.Lines, Is.EqualTo(new[] { "42" }));
    }

    [Test]
    public void ProcessCache_OptimizeSwitch_NoCrossHit()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        var fast = ProjectCompiler.CompileProject(mainPath,
            new CompileOptions { UseDiskCache = false, Optimize = true });
        Assert.That(fast.Success, Is.True);

        // 切 Optimize：键不同 → 全 miss（不串缓存）
        var raw = ProjectCompiler.CompileProject(mainPath,
            new CompileOptions { UseDiskCache = false, Optimize = false });
        Assert.That(raw.Success, Is.True);
        Assert.That(raw.ProcessCacheHits, Is.EqualTo(0), "Optimize 切换不得命中对方条目");
        Assert.That(raw.ProcessCacheMisses, Is.GreaterThanOrEqualTo(3));

        // 同 Optimize 值复跑 → 全 hit
        var fast2 = ProjectCompiler.CompileProject(mainPath,
            new CompileOptions { UseDiskCache = false, Optimize = true });
        Assert.That(fast2.ProcessCacheMisses, Is.EqualTo(0), "同键复跑应全命中");
    }

    [Test]
    public void ProcessCache_OrthogonalToDiskCache()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        // 磁盘缓存路径：进程缓存不参与、不计数（语义与既有统计不变）
        var run1 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run1.Success, Is.True);
        Assert.That(run1.ProcessCacheHits, Is.EqualTo(0));
        Assert.That(run1.ProcessCacheMisses, Is.EqualTo(0));

        var run2 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run2.CacheMisses, Is.EqualTo(0), "磁盘缓存语义不变");
        Assert.That(run2.ProcessCacheHits, Is.EqualTo(0), "磁盘路径不走进程缓存");

        // 现编路径：磁盘缓存不读写 obj/
        var fresh = ProjectCompiler.CompileProject(mainPath, new CompileOptions { UseDiskCache = false });
        Assert.That(fresh.Success, Is.True);
        Assert.That(Directory.Exists(_objDir), Is.True, "此前磁盘缓存落盘不受影响");
    }

    [Test]
    public void ProcessCache_CanBeDisabled()
    {
        WriteProject();
        var mainPath = Path.Combine(_dir, "main.ecs");
        var options = new CompileOptions { UseDiskCache = false, UseProcessCache = false };

        var run1 = ProjectCompiler.CompileProject(mainPath, options);
        Assert.That(run1.Success, Is.True);
        var run2 = ProjectCompiler.CompileProject(mainPath, options);
        Assert.That(run2.Success, Is.True);
        Assert.That(run2.ProcessCacheHits, Is.EqualTo(0), "开关关闭时不命中");
        Assert.That(run2.ProcessCacheMisses, Is.EqualTo(0), "关闭时进程缓存完全不参与（不计数）");
        Assert.That(run2.Timing!.LexingAndParsing, Is.GreaterThan(TimeSpan.Zero),
            "关闭进程缓存后每次现编都全量 parse");
    }
}