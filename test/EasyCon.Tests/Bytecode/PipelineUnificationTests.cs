using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Tests.Support;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 统一编译链路端到端验证（docs/Pipeline.md）：
/// CompileSource/CompileFile -> EcxImage -> EcxVm 桥 -> EcxInterpreter。
/// 已知语义（ModuleSystem.md §7）：同签名跨模块导出为首匹配遮蔽 + MD_AMBIGUOUS_EXPORT 警告。
/// 记录型宿主桩（RecordingIo/RecordingPad）见 Support/EcsTestHost。
/// </summary>
[TestFixture]
public class PipelineUnificationTests
{
    // ---------- 运行器 ----------

    static (List<string> Lines, List<string> Events) RunNewChain(CompileResult result, RecordingIo io, RecordingPad pad)
    {
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "统一链路编译失败：" + string.Join("\n", result.Diagnostics));
        EcxVm.Run(result.Image!, EcsTestHost.Capabilities(io, pad),
            new CancellationTokenSource().Token, [], result.NativeSymbols);
        return (io.Lines, pad.Events);
    }

    // ---------- 用例（单文件语义语料已迁至 corpus/，由 CorpusCrossValidationTests 数据驱动执行） ----------

    [Test]
    public void Import_AndInitOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"EcsUnify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            // utils：顶层 init（模块全局）+ 导出函数；main 经 R3 自动导入 lib/
            File.WriteAllText(Path.Combine(dir, "lib", "utils.ecs"),
                "$__mult = 2\nFUNC twice($x):INT\n    RETURN $x * $__mult\nENDFUNC\n");
            File.WriteAllText(Path.Combine(dir, "main.ecs"),
                "$r = twice(21)\nPRINT $r\nPRINT \"main-end\"\n");

            var result = Compilation.CompileFile(Path.Combine(dir, "main.ecs"));
            var io = new RecordingIo();
            var (lines, _) = RunNewChain(result, io, new RecordingPad());

            Assert.That(lines, Is.EqualTo(new[] { "42", "main-end" }));

            // 链接序：std -> vision -> lib/utils（自动依赖，先于 main）-> main；
            // 入口 = <main>（$eval 本体前插 init 调用序列并更名，无合成壳函数）
            Assert.That(result.Artifacts.Select(a => a.Name).ToList(),
                Is.EqualTo(new[] { "std", "vision", "lib/utils", "main" }));
            Assert.That(result.Image!.Functions[result.Image.Entry].Name, Is.EqualTo("<main>"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void StructParamCrossModule()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"EcsUnify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            // lib 导出带结构体参数的函数（消费端按同形状重算布局，R3 风险点）
            File.WriteAllText(Path.Combine(dir, "lib", "geo.ecs"),
                """
                STRUCT Point
                    $x:INT
                    $y:INT
                END
                STRUCT Rect
                    $p:Point
                    $w:INT
                    $h:INT
                END
                FUNC sumxy($pt:Point):INT
                    RETURN $pt.x + $pt.y
                ENDFUNC
                FUNC area($r:Rect):INT
                    RETURN $r.w * $r.h + sumxy($r.p)
                ENDFUNC
                """);
            File.WriteAllText(Path.Combine(dir, "main.ecs"),
                """
                $pt = Point{}
                $pt.x = 3
                $pt.y = 4
                $r = Rect{}
                $r.p = $pt
                $r.w = 5
                $r.h = 6
                $s1 = sumxy($pt)
                $s2 = area($r)
                PRINT $s1
                PRINT $s2
                """);

            var result = Compilation.CompileFile(Path.Combine(dir, "main.ecs"));
            var io = new RecordingIo();
            var (lines, _) = RunNewChain(result, io, new RecordingPad());

            Assert.That(lines, Is.EqualTo(new[] { "7", "37" }), "结构体参数跨模块");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void SameSignatureShadow_FirstMatchWithWarning()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"EcsUnify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib", "a.ecs"),
                "FUNC dup($x:INT):INT\n    RETURN $x * 2\nENDFUNC\n");
            File.WriteAllText(Path.Combine(dir, "lib", "b.ecs"),
                "FUNC dup($x:INT):INT\n    RETURN $x * 3\nENDFUNC\n");
            File.WriteAllText(Path.Combine(dir, "main.ecs"),
                """
                IMPORT "lib/b.ecs" AS u2
                $a = dup(7)
                $b = u2.dup(7)
                PRINT $a
                PRINT $b
                """);

            // 无限定名 = 首个注入者（自动依赖名序：lib/a 先于 lib/b，绑定层 first-wins）；
            // alias 限定 = 精确指向来源模块（限定导入名 "模块!函数"）；MD_AMBIGUOUS_EXPORT 警告保留
            var result = Compilation.CompileFile(Path.Combine(dir, "main.ecs"));
            var io = new RecordingIo();
            var (lines, _) = RunNewChain(result, io, new RecordingPad());
            Assert.That(lines, Is.EqualTo(new[] { "14", "21" }), "无限定首匹配（14）+ alias 精确指向（21）");
            Assert.That(result.Diagnostics.Any(d => d.IsWarning && d.Message.Contains("MD_AMBIGUOUS_EXPORT")),
                Is.True, "应报告 MD_AMBIGUOUS_EXPORT 警告：" + string.Join("; ", result.Diagnostics));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void ExternFfi()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("libc FFI 仅在 Unix 平台验证");

        const string source = """
            EXTERN FUNC libc_abs($x:INT):INT AS "abs" FROM "libc"
            $a = libc_abs(-42)
            PRINT $a
            """;

        var result = Compilation.CompileSource(source);
        var io = new RecordingIo();
        var (lines, _) = RunNewChain(result, io, new RecordingPad());

        Assert.That(lines, Is.EqualTo(new[] { "42" }));
    }

    [Test]
    [Platform("Linux,MacOsX")]
    public void CsvTestExample_PlatformLibFailsAtFfi()
    {
        // csv_test：平台实现放 lib/ 子目录（R4 显式导入选择平台，不依赖同签名遮蔽序）。
        // 本机非 Windows：编译成功，首次 FFI 调用处终止（msvcrt.dll 不可加载）。
        var main = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "examples", "csv_test", "main.ecs");
        if (!File.Exists(main))
            Assert.Ignore("examples/csv_test 不存在");

        var result = Compilation.CompileFile(main);
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "统一链路编译失败：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Diagnostics.Any(d => d.IsWarning && d.Message.Contains("MD_AMBIGUOUS_EXPORT")),
            Is.False, "单平台导入不应有同签名歧义");

        var io = new RecordingIo();
        Assert.Catch(() => RunNewChain(result, io, new RecordingPad()),
            "非 Windows 平台 msvcrt.dll FFI 必然失败（Windows 上运行到完成）");
        Assert.That(io.Lines, Is.EqualTo(new[] { "========== CSV文件读取测试 ==========", "" }),
            "FFI 失败前输出行（构造到 csv_open 为止）");
    }
}