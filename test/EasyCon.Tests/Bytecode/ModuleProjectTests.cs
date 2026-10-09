using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Resolution;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 模块系统端到端测试（docs/ModuleSystem.md M4–M6 交付判据 + docs/ModuleImportRules.md）：：
/// 独立编译管线 vs 现有合并管线执行对拍、obj/ 缓存 Merkle 失效、
/// &lt;init:module&gt;/&lt;main&gt; 合成的初始化顺序。
///
/// 布局约定（docs/ModuleImportRules.md）：R2 导入基准 = 导入文件所在目录（lib 内互导沿用 "xxx.ecs" 相对路径）；
/// R3 main 隐式导入 lib/——用例不显式 import lib 模块；R5 模块名 = 相对主脚本目录的规范化路径
///（lib/utils.ecs → "lib/utils"）。显式环检测仍生效（跨目录显式环 = 编译错误）。
/// 语言约束：PRINT 不支持表达式（Script.md §PRINT）；lib 顶层语句合法化为模块初始化（&lt;init&gt; 合成）。
/// </summary>
[TestFixture]
public class ModuleProjectTests
{
    string _dir = null!;
    string _objDir = null!;

    [SetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"EcsModProj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "lib"));
        _objDir = Path.Combine(_dir, "obj");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    void Write(string relativePath, string code)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }

    // 顶层赋值 = init 副本；twice 读 $__mult 验证 init 先于调用
    const string UtilsSource = """
        $__mult = 2
        FUNC twice($x):INT
            RETURN $x * $__mult
        ENDFUNC
        FUNC add($a, $b):INT
            RETURN $a + $b
        ENDFUNC
        """;

    // mathx 独立（不 import utils）；嵌套依赖的初始化顺序由 InitRunsBeforeMain 承担
    const string MathxSource = """
        FUNC twice2($x:INT):INT
            RETURN $x * 2 + 1
        ENDFUNC
        FUNC area($w:INT, $h:INT):INT
            RETURN $w * $h
        ENDFUNC
        """;

    const string MainSource = """
        IMPORT "lib/mathx.ecs" AS m
        $r = m.twice2(21)
        PRINT $r
        $d = twice(4)
        PRINT $d
        $a = m.area(2, 3)
        PRINT $a
        PRINT "main-end"
        """;

    /// <summary>写嵌套依赖布局：main →（R3 自动）lib/utils、lib/mathx（别名显式导入）。</summary>
    void WriteNestedProject()
    {
        Write("lib/utils.ecs", UtilsSource);
        Write("lib/mathx.ecs", MathxSource);
        Write("main.ecs", MainSource);
    }

    [Test]
    public void E2E_ModulePipelineParity()
    {
        WriteNestedProject();

        // 路径 A：统一链路（CompileFile 现编；与路径 B 同一模块图，仅缓存策略不同）
        var resultA = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"), new CompileOptions { UseDiskCache = false });
        Assert.That(resultA.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", resultA.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var hostA = new EcxHost();
        hostA.EnableRecording();
        Assert.That(EcxInterpreter.Run(resultA.Image!, hostA), Is.EqualTo(0));

        // 路径 B：模块管线（M4 图+缓存 → M5 逐模块编译 → M6 链接）
        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"));
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        Assert.That(project.Diagnostics, Is.Empty);
        var hostB = new EcxHost();
        hostB.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, hostB), Is.EqualTo(0));

        // 逐行一致 + 语义抽查（init 副本先于调用、嵌套依赖跨模块调用、alias 限定调用）
        Assert.That(hostB.Lines, Is.EqualTo(hostA.Lines), "输出行应逐行一致");
        Assert.That(hostB.Lines, Is.EqualTo(new[] { "43", "8", "6", "main-end" }), "语义抽查");
        Assert.That(hostB.WaitLog, Is.EqualTo(hostA.WaitLog));
    }

    [Test]
    public void PeerLibs_CrossCalls_SameDirVisible()
    {
        // 导入规则 R1：同目录互见——lib 内模块互调无需 IMPORT
        Write("lib/a.ecs", "FUNC double($x:INT):INT\n    RETURN $x * 2\nENDFUNC\n");
        Write("lib/b.ecs", "FUNC quad($x:INT):INT\n    RETURN double(double($x))\nENDFUNC\n");
        Write("main.ecs", "$r = quad(3)\nPRINT $r\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"));
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "12" }));
        Assert.That(project.Artifacts.Single(a => a.Name == "lib/b").Interface!.Dependencies
            .Select(d => d.Name), Does.Contain("lib/a"));
    }

    [Test]
    public void ChainedImports_SameDirResolvable()
    {
        // 导入规则 R1/R2：lib 内链式可见（00_wrapper → 27_target 同目录互见），main 经 R3 自动导入
        Write("lib/00_wrapper.ecs", "FUNC wrapper():INT\n    RETURN target()\nENDFUNC\n");
        Write("lib/27_target.ecs", "FUNC target():INT\n    RETURN 42\nENDFUNC\n");
        Write("main.ecs", "$r = wrapper()\nPRINT $r\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "42" }));
        Assert.That(project.Artifacts.Single(a => a.Name == "main").Interface!.Dependencies
            .Select(d => d.Name), Does.Contain("lib/00_wrapper"));
    }

    [Test]
    public void PeerLib_SameDirVisibility()
    {
        Write("lib/00_wrapper.ecs", "FUNC wrapper():INT\n    RETURN target()\nENDFUNC\n");
        Write("lib/02_consumer.ecs", "FUNC consume():INT\n    RETURN target()\nENDFUNC\n");
        Write("lib/27_target.ecs", "FUNC target():INT\n    RETURN 42\nENDFUNC\n");
        Write("main.ecs", "$r = consume()\nPRINT $r\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "42" }));
        Assert.That(project.Artifacts.Single(a => a.Name == "lib/02_consumer").Interface!.Dependencies
            .Select(d => d.Name), Does.Contain("lib/27_target"));
    }

    [Test]
    public void ExplicitAliasToSubdirModule_RemainsScopedToTheImporter()
    {
        // R4+R6：跨目录显式 AS 导入只注入别名 scope——无限定名不可达
        Write("mod/target.ecs", "FUNC target():INT\n    RETURN 42\nENDFUNC\n");
        Write("main.ecs", "IMPORT \"mod/target.ecs\" AS t\n$r = t.target()\nPRINT $r\n");

        var aliased = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        Assert.That(aliased.Success, Is.True, string.Join("\n", aliased.Diagnostics));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(aliased.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "42" }));

        Write("main.ecs", "IMPORT \"mod/target.ecs\" AS t\n$r = target()\nPRINT $r\n");
        var unqualified = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        Assert.That(unqualified.Success, Is.False, "AS 导入不得额外注入无别名函数");
        Assert.That(unqualified.Diagnostics.Any(d => d.IsError && d.Message.Contains("target")), Is.True,
            string.Join("\n", unqualified.Diagnostics));
    }

    [Test]
    public void ForwardCalls_SameDir_InitializeOnceInStableOrder()
    {
        // 导入规则：同目录互见环（自动边）由同层名序破环——init 顺序 = 文件名序（a 先于 b），
        // 每个模块只初始化一次；跨目录显式链的拓扑序见 InitRunsBeforeMain_TopologicalOrder。
        Write("lib/a.ecs", "PRINT \"a-init\"\nFUNC alpha($depth:INT):INT\n    IF $depth < 1\n        RETURN beta($depth)\n    END\n    RETURN beta($depth - 1)\nENDFUNC\n");
        Write("lib/b.ecs", "PRINT \"b-init\"\nFUNC beta($depth:INT):INT\n    IF $depth < 1\n        RETURN 42\n    END\n    RETURN $depth\nENDFUNC\n");
        Write("main.ecs", "$r = alpha(0)\nPRINT $r\nPRINT \"main-init\"\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "a-init", "b-init", "42", "main-init" }));
        Assert.That(host.Lines.Count(line => line == "a-init"), Is.EqualTo(1));
        Assert.That(host.Lines.Count(line => line == "b-init"), Is.EqualTo(1));
    }

    [Test]
    public void DiskCache_InvalidatesOnSignatureChange_AcrossVisibilityChain()
    {
        // 可见链 main →（R3 自动）wrapper →（R1 同目录）target：目标签名变化沿可见链失效
        Write("lib/00_wrapper.ecs", "FUNC wrapper():INT\n    RETURN target()\nENDFUNC\n");
        Write("lib/27_target.ecs", "FUNC target():INT\n    RETURN 42\nENDFUNC\n");
        Write("main.ecs", "$r = wrapper()\nPRINT $r\n");
        string mainPath = Path.Combine(_dir, "main.ecs");
        var options = new CompileOptions { ObjDir = _objDir, UseDiskCache = true, UseProcessCache = false };

        var cold = ProjectCompiler.CompileProject(mainPath, options);
        Assert.That(cold.Success, Is.True, string.Join("\n", cold.Diagnostics));
        Assert.That(cold.CacheMisses, Is.GreaterThanOrEqualTo(3));

        var diskHot = ProjectCompiler.CompileProject(mainPath, options);
        Assert.That(diskHot.Success, Is.True, string.Join("\n", diskHot.Diagnostics));
        Assert.That(diskHot.CacheMisses, Is.EqualTo(0));
        Assert.That(diskHot.CacheHits, Is.GreaterThanOrEqualTo(5));

        File.WriteAllText(Path.Combine(_dir, "lib", "27_target.ecs"),
            "FUNC target($value:INT):INT\n    RETURN $value + 2\nENDFUNC\n");
        Write("lib/00_wrapper.ecs", "FUNC wrapper($value:INT):INT\n    RETURN target($value)\nENDFUNC\n");
        Write("main.ecs", "$r = wrapper(40)\nPRINT $r\n");
        var changed = ProjectCompiler.CompileProject(mainPath, options);
        Assert.That(changed.Success, Is.True, string.Join("\n", changed.Diagnostics));
        Assert.That(changed.CacheMisses, Is.GreaterThanOrEqualTo(2),
            "目标签名变化应使目标库与绑定目标签名的 wrapper 失效");
        var changedHost = new EcxHost();
        changedHost.EnableRecording();
        Assert.That(EcxInterpreter.Run(changed.Image!, changedHost), Is.EqualTo(0));
        Assert.That(changedHost.Lines, Is.EqualTo(new[] { "42" }));
    }

    [Test]
    public void HierarchicalModule_NestedLibDir_UniqueNames()
    {
        // 导入规则 R5：模块名 = 相对主脚本目录的层级路径——lib/http.ecs 与 lib/net/http.ecs
        // 是两个模块（"lib/http" / "lib/net/http"）；缓存文件名 '/' 落盘转义
        Write("lib/http.ecs", "FUNC name():INT\n    RETURN 1\nENDFUNC\n");
        Write("lib/net/http.ecs", "FUNC name():INT\n    RETURN 2\nENDFUNC\n");
        Write("main.ecs", "IMPORT \"lib/http.ecs\" AS h\nIMPORT \"lib/net/http.ecs\" AS n\n"
            + "$a = h.name()\n$b = n.name()\n$c = $a + $b\nPRINT $c\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { ObjDir = _objDir, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        Assert.That(project.Artifacts.Select(a => a.Name), Does.Contain("lib/http").And.Contain("lib/net/http"));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "3" }));
        // 缓存文件名 '/' 转义落盘
        Assert.That(Directory.GetFiles(_objDir, "lib_net_http-*.ecm"), Has.Length.EqualTo(1),
            "层级模块名落盘转义");
    }

    [Test]
    public void Cache_MerkleInvalidation()
    {
        WriteNestedProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        // run1：全量未命中（std/vision/utils/mathx/main）
        var run1 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run1.Success, Is.True, string.Join("\n", run1.Diagnostics));
        Assert.That(run1.CacheMisses, Is.GreaterThanOrEqualTo(5), "首次编译应全量未命中");
        Assert.That(run1.CacheHits, Is.EqualTo(0));

        // run2：源码未变 → 全部命中
        var run2 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run2.Success, Is.True);
        Assert.That(run2.CacheMisses, Is.EqualTo(0), "二次编译应全量命中");
        Assert.That(run2.CacheHits, Is.GreaterThanOrEqualTo(5));

        // run3：utils 实现体改动（twice 改写法，语义不变）→ 接口哈希不变
        // → utils 重编（新 cacheKey），mathx/main 的缓存键只含 utils 接口哈希 → 全部命中（Merkle 收益）
        File.WriteAllText(Path.Combine(_dir, "lib", "utils.ecs"),
            UtilsSource.Replace("RETURN $x * $__mult", "RETURN $__mult * $x"));
        var run3 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run3.Success, Is.True, string.Join("\n", run3.Diagnostics));
        Assert.That(run3.CacheMisses, Is.EqualTo(1), "仅 utils 重编");
        var host3 = new EcxHost();
        host3.EnableRecording();
        Assert.That(EcxInterpreter.Run(run3.Image!, host3), Is.EqualTo(0));
        Assert.That(host3.Lines, Is.EqualTo(new[] { "43", "8", "6", "main-end" }), "utils 重编后行为一致");

        // run4：utils 接口变化（新增导出）→ mathx（调用 utils）失效重编；
        // main 经 R3 自动导入 utils → main 的缓存键含 utils 接口哈希 → 一并失效（保守但正确）
        File.WriteAllText(Path.Combine(_dir, "lib", "utils.ecs"),
            UtilsSource + "\nFUNC ping():INT\n    RETURN 1\nENDFUNC\n");
        var run4 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run4.Success, Is.True, string.Join("\n", run4.Diagnostics));
        Assert.That(run4.CacheMisses, Is.EqualTo(3), "utils + mathx + main 重编（main 自动依赖 utils 接口）");
        var host4 = new EcxHost();
        host4.EnableRecording();
        Assert.That(EcxInterpreter.Run(run4.Image!, host4), Is.EqualTo(0));
        Assert.That(host4.Lines, Is.EqualTo(new[] { "43", "8", "6", "main-end" }), "接口新增导出不改变既有行为");
    }

    [Test]
    public void Cache_StdVisionSharedAcrossProjects()
    {
        // stdlib 分发策略（ModuleSystem.md §0/§5.1）：std/vision 内嵌源码与用户模块同走 obj/ 缓存，
        // 两个不同 main 共享同一 objDir → std/vision 二次命中（首次编译任一脚本自然缓存）。
        // R1 目录即包：两个入口脚本分放不同目录，避免同目录互见把 mainB 拉进 mainA 的包
        Write("a/main.ecs", "$a = 1\nPRINT $a\n");
        Write("b/main.ecs", "$b = 2\nPRINT $b\n");

        var runA = ProjectCompiler.CompileProject(Path.Combine(_dir, "a", "main.ecs"), new CompileOptions { ObjDir = _objDir });
        Assert.That(runA.Success, Is.True, string.Join("\n", runA.Diagnostics));
        Assert.That(runA.CacheMisses, Is.EqualTo(3), "std/vision/mainA 全量未命中");

        var runB = ProjectCompiler.CompileProject(Path.Combine(_dir, "b", "main.ecs"), new CompileOptions { ObjDir = _objDir });
        Assert.That(runB.Success, Is.True, string.Join("\n", runB.Diagnostics));
        Assert.That(runB.CacheMisses, Is.EqualTo(1), "仅 mainB 新源码重编");
        Assert.That(runB.CacheHits, Is.EqualTo(2), "std/vision 跨项目命中");
        Assert.That(Directory.GetFiles(_objDir, "std-*.ecm"), Has.Length.EqualTo(1), "std 应落盘 obj/");
        Assert.That(Directory.GetFiles(_objDir, "vision-*.ecm"), Has.Length.EqualTo(1), "vision 应落盘 obj/");

        var runA2 = ProjectCompiler.CompileProject(Path.Combine(_dir, "a", "main.ecs"), new CompileOptions { ObjDir = _objDir });
        Assert.That(runA2.CacheMisses, Is.EqualTo(0), "共享缓存互不干扰");
    }

    [Test]
    public void MissingImport_GracefulFailure()
    {
        Write("main.ecs", "IMPORT \"nope.ecs\"\n$x = 1\n");
        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"));
        Assert.That(project.Success, Is.False, "缺失导入应失败");
        Assert.That(project.Diagnostics.Any(d => d.Message.Contains("导入文件不存在") || d.Message.Contains("导入库不存在")), Is.True,
            string.Join("; ", project.Diagnostics));
    }

    [Test]
    public void IndirectImport_Chain_SameDirResolvable()
    {
        // 导入规则：lib 内 a → b 同目录互见（R1），main 经 R3 自动导入 a——链式依赖可达
        Write("lib/a.ecs", "FUNC fromA():INT\n    RETURN fromB() + 1\nENDFUNC\n");
        Write("lib/b.ecs", "FUNC fromB():INT\n    RETURN 41\nENDFUNC\n");
        Write("main.ecs", "$result = fromA()\nPRINT $result\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        Assert.That(project.Diagnostics.Where(d => d.IsError), Is.Empty);
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "42" }), "fromA = fromB + 1 = 42（链式依赖）");
    }

    [Test]
    public void RelativeExternPath_ResolvesAgainstDeclaringModule()
    {
        Write("lib/probe.ecs", "EXTERN FUNC probe($value:INT):INT FROM \"../probe.dll\"\n");
        Write("main.ecs", "IMPORT \"lib/probe.ecs\" AS p\n$x = p.probe(1)\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        Assert.That(project.NativeSymbols, Has.One.Matches<FunctionSymbol>(symbol =>
            symbol.LibraryName == Path.GetFullPath(Path.Combine(_dir, "probe.dll"))));
    }

    [Test]
    public void RelativeExternPaths_ResolveAgainstMainRootAndNestedDeclarations()
    {
        Write("lib/probe.ecs", "EXTERN FUNC root_probe($value:INT):INT FROM \"../native/root.dll\"\n");
        Write("lib/nested/nestedprobe.ecs", "EXTERN FUNC nested_probe($value:INT):INT FROM \"../../native/nested.dll\"\n");
        Write("lib/autoload.ecs", "EXTERN FUNC autoload_probe($value:INT):INT FROM \"../native/autoload.dll\"\n");
        Write("main.ecs", "IMPORT \"lib/probe.ecs\" AS p\nIMPORT \"lib/nested/nestedprobe.ecs\" AS n\n"
            + "EXTERN FUNC main_probe($value:INT):INT FROM \"native/main.dll\"\n"
            + "$result = main_probe(1) + p.root_probe(2) + n.nested_probe(3) + autoload_probe(4)\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"),
            new CompileOptions { UseDiskCache = false, UseProcessCache = false });

        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var libraries = project.NativeSymbols.ToDictionary(symbol => symbol.Name, symbol => symbol.LibraryName);
        Assert.Multiple(() =>
        {
            Assert.That(libraries["main_probe"], Is.EqualTo(Path.GetFullPath(Path.Combine(_dir, "native", "main.dll"))));
            Assert.That(libraries["root_probe"], Is.EqualTo(Path.GetFullPath(Path.Combine(_dir, "native", "root.dll"))));
            Assert.That(libraries["nested_probe"], Is.EqualTo(Path.GetFullPath(Path.Combine(_dir, "native", "nested.dll"))));
            Assert.That(libraries["autoload_probe"], Is.EqualTo(Path.GetFullPath(Path.Combine(_dir, "native", "autoload.dll"))));
        });
    }

    [Test]
    public void ExternPath_PreservesAbsoluteAndBareNames_AndFormatterKeepsRawSyntax()
    {
        string sourcePath = Path.Combine(_dir, "lib", "probe.ecs");
        string absolutePath = Path.GetFullPath(Path.Combine(_dir, "native", "probe.dll"));
        Assert.Multiple(() =>
        {
            Assert.That(ExternLibraryPath.Resolve(absolutePath, sourcePath), Is.EqualTo(absolutePath));
            Assert.That(ExternLibraryPath.Resolve("FrlgFfi.dll", sourcePath), Is.EqualTo("FrlgFfi.dll"));
            Assert.That(ExternLibraryPath.Resolve("../native/probe.dll", "<memory>"), Is.EqualTo("../native/probe.dll"));
        });

        const string relativeDeclaration = "EXTERN FUNC probe():INT FROM \"../native/probe.dll\"";
        string formatted = Compilation.FormatSource(relativeDeclaration);
        Assert.That(formatted, Does.Contain("FROM \"../native/probe.dll\""));
        Assert.That(formatted, Does.Not.Contain(absolutePath));
    }

    [Test]
    public void MissingNativeLibrary_ReportsAttemptedPathAndPreservesLoaderException()
    {
        string missingPath = Path.GetFullPath(Path.Combine(_dir, "native", "missing.dll"));
        var symbol = new FunctionSymbol("probe", [], ScriptType.Int, libraryName: missingPath);

        var exception = Assert.Throws<ScriptException>(() => new NativeLoader().ResolveFunction(symbol));

        Assert.That(exception!.Message, Does.Contain(missingPath).And.Contain("文件不存在"));
        Assert.That(exception.InnerException, Is.Not.Null);
        Assert.That(exception.Address, Is.Zero, "NativeLoader 没有源行元数据时保留未知位置哨兵");
    }

    [Test]
    public void RelativeExternPath_DiskCachesAreIsolatedByDeclaringDirectory()
    {
        Write("lib/ffi.ecs", "EXTERN FUNC probe($value:INT):INT FROM \"../native/FrlgFfi.dll\"\n");
        Write("main.ecs", "IMPORT \"lib/ffi.ecs\" AS ffi\n$result = ffi.probe(1)\n");
        var optionsA = new CompileOptions { ObjDir = _objDir, UseProcessCache = false };
        var projectA = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"), optionsA);
        Assert.That(projectA.Success, Is.True, string.Join("\n", projectA.Diagnostics));

        string dirB = Path.Combine(Path.GetTempPath(), $"Ecs FFI Cache 汉字_{Guid.NewGuid():N}");
        string objDirB = Path.Combine(dirB, "obj");
        Directory.CreateDirectory(Path.Combine(dirB, "lib"));
        Directory.CreateDirectory(objDirB);
        try
        {
            File.WriteAllText(Path.Combine(dirB, "lib", "ffi.ecs"),
                "EXTERN FUNC probe($value:INT):INT FROM \"../native/FrlgFfi.dll\"\n");
            File.WriteAllText(Path.Combine(dirB, "main.ecs"),
                "IMPORT \"lib/ffi.ecs\" AS ffi\n$result = ffi.probe(1)\n");
            foreach (string cachedFile in Directory.GetFiles(_objDir))
                File.Copy(cachedFile, Path.Combine(objDirB, Path.GetFileName(cachedFile)));

            var optionsB = new CompileOptions { ObjDir = objDirB, UseProcessCache = false };
            var projectB = ProjectCompiler.CompileProject(Path.Combine(dirB, "main.ecs"), optionsB);
            string expectedLibrary = Path.GetFullPath(Path.Combine(dirB, "native", "FrlgFfi.dll"));

            Assert.That(projectB.Success, Is.True, string.Join("\n", projectB.Diagnostics));
            Assert.That(projectB.NativeSymbols.Single().LibraryName, Is.EqualTo(expectedLibrary));
            Assert.That(projectB.CacheHits, Is.GreaterThanOrEqualTo(2), "std/vision 仍可跨项目共享");
            Assert.That(projectB.CacheMisses, Is.GreaterThanOrEqualTo(2), "用户 FFI 模块和 main 应按源路径重新编译");
            // 导入规则：.eci 接口预提取缓存复活（同目录互见需要依赖接口先行），
            // .ecm 的 iface blob 仍随产物携带
            Assert.That(Directory.GetFiles(objDirB, "lib_ffi-*.ecm"), Has.Length.EqualTo(2),
                "B 项目应生成独立于 A 项目的 .ecm");
            Assert.That(projectB.Artifacts.Single(a => a.Name == "lib/ffi").Interface!.Functions
                .Single().ExternLibrary, Is.EqualTo(expectedLibrary),
                "FFI 库路径按声明目录解析并随接口区携带");

            ProcessModuleCache.Clear();
            var processOptions = new CompileOptions { UseDiskCache = false, UseProcessCache = true };
            var processA = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"), processOptions);
            var processB = ProjectCompiler.CompileProject(Path.Combine(dirB, "main.ecs"), processOptions);
            Assert.That(processA.Success, Is.True, string.Join("\n", processA.Diagnostics));
            Assert.That(processB.Success, Is.True, string.Join("\n", processB.Diagnostics));
            Assert.That(processB.NativeSymbols.Single().LibraryName, Is.EqualTo(expectedLibrary));
            Assert.That(processB.ProcessCacheMisses, Is.GreaterThanOrEqualTo(2),
                "进程缓存不得把 A 的 FFI 模块或 main 带入 B");
        }
        finally
        {
            ProcessModuleCache.Clear();
            if (Directory.Exists(dirB))
                Directory.Delete(dirB, recursive: true);
        }
    }

    [Test]
    public void InitRunsBeforeMain_TopologicalOrder()
    {
        // 跨目录/显式链的 init 拓扑序：依赖链 main →（R3 自动）mathx →（R1 同目录显式 IMPORT）utils
        // &lt;init&gt; 顺序应为 utils → mathx → main
        Write("lib/utils.ecs", "PRINT \"init-utils\"\nFUNC twice($x):INT\n    RETURN $x * 2\nENDFUNC\n");
        Write("lib/mathx.ecs", "IMPORT \"utils.ecs\"\nPRINT \"init-mathx\"\nFUNC triple($x:INT):INT\n    RETURN twice($x) + $x\nENDFUNC\n");
        Write("main.ecs", "PRINT \"init-main\"\n$a = triple(5)\nPRINT $a\n");

        var project = ProjectCompiler.CompileProject(Path.Combine(_dir, "main.ecs"));
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "init-utils", "init-mathx", "init-main", "15" }),
            "<init> 应按拓扑序先于 main 执行");
    }

    [Test]
    public void CacheHit_SkipsRecompile_ProducesWorkingImage()
    {
        WriteNestedProject();
        var mainPath = Path.Combine(_dir, "main.ecs");

        var first = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(first.Success, Is.True);
        var objFiles = Directory.GetFiles(_objDir, "*.ecm");
        Assert.That(objFiles.Length, Is.GreaterThanOrEqualTo(3), "产物应落盘 obj/（std/vision/utils/mathx/main）");
        foreach (var f in objFiles)
            Assert.That(new FileInfo(f).Length, Is.GreaterThan(0));

        var second = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(second.Success, Is.True);
        Assert.That(second.CacheMisses, Is.EqualTo(0));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(second.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "43", "8", "6", "main-end" }));
    }

    [Test]
    public void NestedStruct_DiskCacheRoundtrip()
    {
        // 嵌套结构体经 .ecm 缓存往返：ext 槽须携带嵌套 sid（Box=0/Item=1，误写 Count=0 即自嵌套）；
        // 读侧布局展开须递归（嵌套 sid 大于父时单遍扫描把 SlotCount/SlotOffset 算错）
        Write("main.ecs", """
            STRUCT Item
                $v:INT
            END
            STRUCT Box
                $it:Item
                $n:INT
            END
            FUNC make():Box
                $b = Box{}
                $b.it.v = 41
                $b.n = 9
                RETURN $b
            ENDFUNC
            $b = make()
            $y = $b.it.v
            PRINT $y
            $z = $b.n
            PRINT $z
            """);
        var mainPath = Path.Combine(_dir, "main.ecs");

        var cold = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(cold.Success, Is.True, string.Join("\n", cold.Diagnostics));
        Assert.That(cold.CacheMisses, Is.GreaterThanOrEqualTo(1), "冷编译应落盘 obj/ 缓存");

        var warm = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(warm.Success, Is.True, string.Join("\n", warm.Diagnostics));
        Assert.That(warm.CacheMisses, Is.EqualTo(0), "热编译应全量命中缓存");

        // 冷/热镜像逐字节一致（.ecm 往返不得丢嵌套 sid，也不得让布局展开结果参与序列化）
        var coldBytes = EcsContainer.WriteImage(cold.Image!);
        var warmBytes = EcsContainer.WriteImage(warm.Image!);
        Assert.That(warmBytes, Is.EqualTo(coldBytes), "缓存往返后镜像应逐字节一致");

        // 热缓存镜像可执行：读侧 SlotCount/SlotOffset 展开正确（自嵌套会令 ZeroFill 无限递归）
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(warm.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "41", "9" }));

        // 布局抽查：Box = 嵌套 Item(1 槽) + n(1 槽) → 共 2 槽；it 指向 Item（sid=1）
        var structs = warm.Image!.Structs;
        var box = structs.Single(s => s.Name == "Box");
        Assert.That(box.SlotCount, Is.EqualTo(2));
        var it = box.Fields.Single(f => f.Name == "it");
        Assert.That(it.Kind, Is.EqualTo(EcsFieldKind.NestedStruct));
        Assert.That(it.NestedSid, Is.EqualTo(structs.FindIndex(s => s.Name == "Item")));
        Assert.That(it.SlotOffset, Is.EqualTo(0));
        Assert.That(box.Fields.Single(f => f.Name == "n").SlotOffset, Is.EqualTo(1));
    }
    // ---------- lib 顶层常量 与 lib 互导（ModuleSystem.md 新设计：lib 允许导入其他 lib）----------

    [Test]
    public void LibConstant_Parity()
    {
        // lib 顶层 CONST：编译期求值（BoundNop，零运行时语句），本模块函数可见；
        // 消费者不可见（常量 = 模块私有，ModuleSystem.md §4-⑦）
        Write("lib/utils.ecs", """
            _scale = 3
            FUNC scale($x:INT):INT
                RETURN $x * _scale
            ENDFUNC
            """);
        Write("main.ecs", """
            $r = scale(5)
            PRINT $r
            """);
        var mainPath = Path.Combine(_dir, "main.ecs");

        // 路径 A：统一链路（lib 常量语义：编译期折叠进模块产物）
        var resultA = Compilation.CompileFile(mainPath, new CompileOptions { UseDiskCache = false });
        Assert.That(resultA.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", resultA.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var hostA = new EcxHost();
        hostA.EnableRecording();
        Assert.That(EcxInterpreter.Run(resultA.Image!, hostA), Is.EqualTo(0));

        // 路径 B：缓存路径
        var project = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        var hostB = new EcxHost();
        hostB.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, hostB), Is.EqualTo(0));

        Assert.That(hostA.Lines, Is.EqualTo(new[] { "15" }), "lib 常量参与编译期折叠");
        Assert.That(hostB.Lines, Is.EqualTo(hostA.Lines), "缓存路径行为一致");
    }

    [Test]
    public void NestedLibImport_ConstAndCrossCalls()
    {
        // lib → lib 同目录互见（R1）+ main 经 R3 自动导入：
        //   level（lib/）调用 base（lib/，同目录互见）
        //   - level 的函数调用 base 的函数（链接期 dep→dep 导入解析）
        //   - 两层各自的顶层 CONST 编译期折叠、互不可见
        //   - main 不 IMPORT：base/level 经 R3 自动可见
        Write("lib/base.ecs", """
            _base = 10
            FUNC bump($x:INT):INT
                RETURN $x + _base
            ENDFUNC
            """);
        Write("lib/level.ecs", """
            _step = 5
            FUNC level($x:INT):INT
                RETURN bump($x) + _step
            ENDFUNC
            """);
        Write("main.ecs", """
            $r = level(2)
            PRINT $r
            """);
        var mainPath = Path.Combine(_dir, "main.ecs");

        var project = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));

        // 链接序：std → vision → lib/base → lib/level → main（自动依赖同层名序）
        Assert.That(project.Artifacts.Select(a => a.Name).ToList(),
            Is.EqualTo(new[] { "std", "vision", "lib/base", "lib/level", "main" }));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "17" }), "level(2) = bump(2) + _step = (2 + _base) + 5 = 17");

        // 依赖接口闭包：level 的依赖表含 base；base 的接口哈希参与 level 的缓存键
        var level = project.Artifacts.Single(a => a.Name == "lib/level");
        Assert.That(level.Interface!.Dependencies.Select(d => d.Name), Does.Contain("lib/base"));
    }
    // ---------- M7：诊断与工具 ----------

    [Test]
    public void AmbiguousExport_Warns_FirstWins()
    {
        // 两个模块导出同名同签名 → MD_AMBIGUOUS_EXPORT 警告（不阻断），
        // 全局限定采用先导入者；alias 限定仍可达各自版本
        Write("lib/utils.ecs", """
            FUNC dup($x:INT):INT
                RETURN $x * 2
            ENDFUNC
            """);
        Write("lib/utils2.ecs", """
            FUNC dup($x:INT):INT
                RETURN $x * 3
            ENDFUNC
            """);
        Write("main.ecs", """
            IMPORT "lib/utils2.ecs" AS u2
            $a = dup(7)
            PRINT $a
            $b = u2.dup(7)
            PRINT $b
            """);
        var mainPath = Path.Combine(_dir, "main.ecs");

        var project = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(project.Success, Is.True, string.Join("\n", project.Diagnostics));
        Assert.That(project.Warnings.Any(w => w.Contains("MD_AMBIGUOUS_EXPORT") && w.Contains("dup/1")
            && w.Contains("utils") && w.Contains("utils2")), Is.True,
            "应报告歧义导出警告：" + string.Join("; ", project.Warnings));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(project.Image!, host), Is.EqualTo(0));
        // 无限定名 = 首个注入者（自动依赖名序：lib/utils 先于 lib/utils2）；alias 限定 = 精确指向来源模块。
        // 同名同签名重复导出 = MD_AMBIGUOUS_EXPORT 警告，不报错。
        Assert.That(host.Lines, Is.EqualTo(new[] { "14", "21" }), "无限定首匹配（14）+ alias 精确指向（21）");
    }

    [Test]
    public void CacheGC_RemovesStaleFiles()
    {
        WriteNestedProject();
        var mainPath = Path.Combine(_dir, "main.ecs");
        var run1 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run1.Success, Is.True);

        // 埋入 40 天前的孤儿产物/错误文件
        var staleEcm = Path.Combine(_objDir, "ghost-12345678.ecm");
        var staleErr = Path.Combine(_objDir, "ghost-12345678.err");
        File.WriteAllBytes(staleEcm, new byte[] { 1 });
        File.WriteAllBytes(staleErr, new byte[] { 1 });
        var old = DateTime.UtcNow.AddDays(-40);
        File.SetLastWriteTimeUtc(staleEcm, old);
        File.SetLastWriteTimeUtc(staleErr, old);

        var run2 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run2.Success, Is.True);
        Assert.That(run2.GarbageCollected, Is.EqualTo(2), "应清理孤儿 .ecm 与 .err");
        Assert.That(File.Exists(staleEcm), Is.False);
        Assert.That(File.Exists(staleErr), Is.False);
        Assert.That(Directory.GetFiles(_objDir, "*.ecm").Length, Is.GreaterThanOrEqualTo(5), "在用产物不受影响");
    }

    [Test]
    public void ErrorCache_ReplaysDiagnostics()
    {
        Write("lib/utils.ecs", "FUNC bad():INT\n    RETURN $nope\nENDFUNC\n");
        Write("main.ecs", "$r = bad()\nPRINT $r\n");
        var mainPath = Path.Combine(_dir, "main.ecs");

        // run1：编译失败，诊断入错误缓存（结构化诊断，模块归属由 Location.FileName 区分）
        var run1 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run1.Success, Is.False);
        Assert.That(run1.Diagnostics.Any(d => d.FileName.EndsWith("utils.ecs") && d.Message.Contains("找不到变量")), Is.True,
            string.Join("; ", run1.Diagnostics));
        Assert.That(Directory.GetFiles(_objDir, "*.err").Length, Is.EqualTo(1), "失败诊断应持久化 .err");

        // run2：同 cacheKey → 错误重放，快速失败（不重新绑定/编码）；
        // 重放诊断带模块名前缀（docs/Pipeline.md：.err 存字符串，重放构造默认位置）；
        // 级联：main 依赖失败的 utils → 追加级联诊断
        var run2 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run2.Success, Is.False);
        Assert.That(run2.ErrorHits, Is.EqualTo(1), "应命中错误缓存");
        Assert.That(run2.Diagnostics.Count(d => d.Message.StartsWith("[lib/utils] ")),
            Is.EqualTo(run1.Diagnostics.Count(d => d.FileName.EndsWith("utils.ecs"))),
            "utils 重放诊断条数与首次失败一致：" + string.Join("; ", run2.Diagnostics));
        Assert.That(run2.Diagnostics.Any(d => d.Code == DiagnosticCodes.DependencyFailed && d.Message.Contains("lib/utils")),
            Is.True, "main 应带级联诊断：" + string.Join("; ", run2.Diagnostics));
        Assert.That(run2.Diagnostics.Any(d => d.Message.Contains("找不到变量")), Is.True, "重放诊断内容一致");

        // run3：修复 lib（源码变 → 新 cacheKey）→ 重编成功
        File.WriteAllText(Path.Combine(_dir, "lib", "utils.ecs"), "FUNC bad():INT\n    RETURN 1\nENDFUNC\n");
        var run3 = ProjectCompiler.CompileProject(mainPath, new CompileOptions { ObjDir = _objDir });
        Assert.That(run3.Success, Is.True, string.Join("\n", run3.Diagnostics));
        Assert.That(run3.Diagnostics, Is.Empty);
    }
}