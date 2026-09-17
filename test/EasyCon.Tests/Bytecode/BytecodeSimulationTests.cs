using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using EasyScript;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 统一链路语义验证（docs/VmSemanticContract.md §五）：真实例程与混合语料经
/// CompileSource -> EcxImage -> EcxInterpreter 执行，事件流与语义基线断言
/// （基线由 v1 金标准 SsaEvaluator 对拍时代逐字锁定，见 PipelineUnificationTests）。
/// 记录型宿主桩（RecordingIo/RecordingPad）见 Support/EcsTestHost。
/// </summary>
[TestFixture]
public class BytecodeSimulationTests
{
    // ---------- 管线辅助 ----------

    static CompileResult Compile(string source, string fileName = "probe.ecs")
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(),
            Is.Empty, "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    /// <summary>经 EcxVm 桥执行（与桌面运行时同一装配面）。</summary>
    static List<string> RunChain(CompileResult result, RecordingIo io, RecordingPad pad)
    {
        EcxVm.Run(result.Image!, EcsTestHost.Capabilities(io, pad),
            new CancellationTokenSource().Token, [], result.NativeSymbols);
        return io.Lines;
    }

    static List<string> RunAfterRoundtrip(CompileResult result, EcxHost host)
    {
        // ECM roundtrip：序列化 -> 反序列化后重链接（验证格式）
        var restored = result.Artifacts.Select(a => EcmFormat.Read(EcmFormat.Write(a))).ToList();
        var image = EcxPipeline.Link(restored, restored.Any(a => a.KeyAction), restored.Any(a => a.NeedIL));
        var code = EcxInterpreter.Run(image, host);
        Assert.That(code, Is.EqualTo(0), $"模拟执行失败：ECS 错误码 {code}");
        return host.Lines;
    }

    // ---------- 光速过帧：例程模拟测试 ----------

    const string GuangshuSource = """
        # <<光速过帧>>（例程改编：帧数缩减用于模拟测试）
        _帧数 = 2
        _延迟 = 30
        $4 = 120

        $3 = _延迟
        $4 += _延迟
        FOR 10
            RCLICK
            300
        NEXT
        $1 = 1
        $5 = 0
        FOR _帧数
            $2 = 1
            $1 += 1
            IF $1 == 31
                $1 = 1
                $2 = 2
            ENDIF
            $5 += 1
            FOR $2
                A 50
                WAIT $4
                LS LEFT
                WAIT $3
                RS LEFT
                WAIT $3
                LEFT DOWN
                WAIT $3
                LS UP
                WAIT $3
                RIGHT DOWN
                WAIT $3
                LS RIGHT
                WAIT $3
                RS RIGHT
                WAIT $3
                LEFT UP
                RIGHT UP
                A 50
                WAIT $4
            NEXT
            PRINT 过了 & $5 & 帧
        NEXT
        """;

    [Test]
    public void Guangshu_EventsAndSemantics()
    {
        var result = Compile(GuangshuSource, "guangshu.ecs");

        var io = new RecordingIo();
        var pad = new RecordingPad();
        var lines = RunChain(result, io, pad);

        // 例程语义抽查：每帧 RCLICK + 内层循环 1 次的固定序列
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("过了1帧"));
        Assert.That(lines[1], Is.EqualTo("过了2帧"));

        // 按键/摇杆事件流（金标准 mock 格式）
        Assert.That(pad.Events.Where(e => e.StartsWith("KEY ")).ToList(),
            Has.Count.EqualTo(14), "前奏 10 次 RCLICK + 每帧 2 次 A（共 2 帧）");
        Assert.That(pad.Events.Where(e => e.StartsWith("KEYST ")).Count(), Is.GreaterThan(0), "按键状态事件");
        Assert.That(pad.Events.Where(e => e.StartsWith("STICK ")).Count(), Is.GreaterThan(0), "摇杆设置事件");
        // 本例程只含摇杆方向设置（STICK），无摇杆点击（STICKC）

        // ECM roundtrip 后重链接执行一致（格式验证）
        var host = new EcxHost();
        host.EnableRecording();
        var roundtripLines = RunAfterRoundtrip(result, host);
        Assert.That(roundtripLines, Is.EqualTo(lines), "roundtrip 后输出不一致");
    }

    [Test]
    public void Guangshu_FullScript_CompilesAndStaysSmall()
    {
        var examplePath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "examples", "光速过帧v1.4精准版.txt");
        if (!File.Exists(examplePath))
            Assert.Ignore("例程文件不存在");

        var source = File.ReadAllText(examplePath);
        var result = Compile(source, "guangshu_full.ecs");
        var image = result.Image!;
        var bytes = EcxWriter.Write(image);

        Assert.That(image.KeyAction, Is.True, "应携带 KeyAction 标志");
        Assert.That(bytes.Length, Is.LessThan(2048), "光速过帧镜像应小于 2KB");
        var disasm = EcxDisassembler.Disassemble(image);
        Assert.That(disasm, Does.Contain("CallN").And.Contain("FWRITE"));
    }

    // ---------- BDSP图鉴过帧：例程模拟测试 ----------

    const string BdspExampleName = "BDSP图鉴过帧v2.1光速过帧版.txt";

    static string BdspSource(string? replaceFrom = null, string? replaceTo = null)
    {
        var examplePath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "examples", BdspExampleName);
        Assert.That(File.Exists(examplePath), Is.True, "例程文件不存在");
        var source = File.ReadAllText(examplePath);
        return replaceFrom == null ? source : source.Replace(replaceFrom, replaceTo!);
    }

    static CompileResult CompileBdsp(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(),
            Is.Empty, "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    [Test]
    public void Bdsp_FanPath_EventsAndSemantics()
    {
        // 默认 mode=1（翻图鉴）/ 地上：main 折叠为 DISAHNGA → FAN → DISHANGB；
        // _翻几次 1→2 缩短模拟时长，每次翻页 PRINT 一行
        var result = CompileBdsp(BdspSource("_翻几次 = 1", "_翻几次 = 2"));

        var io = new RecordingIo();
        var pad = new RecordingPad();
        var lines = RunChain(result, io, pad);
        Assert.That(lines, Is.EqualTo(new[] { "已过帧1次", "已过帧2次" }),
            "FAN 每次翻页 PRINT 一行，$1 跨 FUNC 累计");

        // 唤醒 5×LCLICK + DISAHNGA 6 键（B/B/X/PLUS/B/UP）+ FAN 的 A 与每页 1×DOWN + DISHANGB 6 键（B/B/PLUS/A/HOME/HOME）
        Assert.That(pad.Events.Count(e => e.StartsWith("KEY ")), Is.EqualTo(20), "KEY 点击事件计数");
        // 每页 RS RIGHT + LS RIGHT（4 次 SetStick）+ DISHANGB RS/LS RESET（2 次）
        Assert.That(pad.Events.Count(e => e.StartsWith("STICK ")), Is.EqualTo(6), "摇杆方向设置事件");
        Assert.That(pad.Events.Count(e => e.StartsWith("STICKC ")), Is.EqualTo(1), "LS RIGHT,100 摇杆点击");
        // 裸方向键（DOWN）编译为 50ms 点击（KeyI），不产生按压状态事件
        Assert.That(pad.Events.Where(e => e.StartsWith("KEYST ")), Is.Empty, "裸方向键应为点击而非按压");

        // ECM roundtrip 后重链接执行一致（格式验证）
        var host = new EcxHost();
        host.EnableRecording();
        var roundtripLines = RunAfterRoundtrip(result, host);
        Assert.That(roundtripLines, Is.EqualTo(lines), "roundtrip 后输出不一致");
    }

    [Test]
    public void Bdsp_FullScript_ConstFoldAndWholeFuncDce()
    {
        // 源码 8 个 FUNC：mode=1（_过帧模式=1）与 地上（_在哪测帧=1）编译期常量折叠后，
        // 仅 DISAHNGA/FAN/DISHANGB 可达；KAIGUAN/GUO/YEWAI/DIXIAA/DIXIAB 被链接期整函数 DCE 消除
        var result = CompileBdsp(BdspSource());
        var image = result.Image!;
        var bytes = EcxWriter.Write(image);

        var names = image.Functions.Select(f => f.Name).ToList();
        Assert.That(names, Does.Contain("DISAHNGA").And.Contain("FAN").And.Contain("DISHANGB"),
            "mode=1 地上路径三函数应保留");
        Assert.That(names, Does.Not.Contain("KAIGUAN").And.Not.Contain("GUO")
            .And.Not.Contain("YEWAI").And.Not.Contain("DIXIAA").And.Not.Contain("DIXIAB"),
            "不可达模式的整函数应被 DCE 消除");
        Assert.That(image.Globals.Select(g => g.Name), Is.EqualTo(new[] { "$1", "$2" }),
            "跨 FUNC 共享的脚本变量进全局表");
        Assert.That(bytes.Length, Is.LessThan(1024), "BDSP 镜像应小于 1KB");
        Assert.That(EcxDisassembler.Disassemble(image), Does.Contain("Call"), "含用户函数调用");
    }

    // ---------- 通用管线：算术/字符串/数组/结构体/控制流 ----------

    [Test]
    public void Pipeline_MixedSemantics()
    {
        const string source = """
            $g = 0
            FUNC fact($n): INT
                IF $n <= 1
                    RETURN 1
                ENDIF
                RETURN $n * fact($n - 1)
            ENDFUNC
            FUNC add($a, $b): INT
                RETURN $a + $b
            ENDFUNC
            STRUCT Point
                $x:INT
                $y:INT
            END
            $g = fact(5)
            $t = add(3, 4)
            $p = Point{}
            $p.x = 11
            $p.y = $t
            $px = $p.x
            $py = $p.y
            $arr = [10, 20, 30]
            $arr[1] = 99
            $slice = $arr[0:2]
            $s = "n=" & $g & ",x=" & $px & ",y=" & $py & ",len=" & LEN($arr) & "," & LEN($slice)
            PRINT $s
            $cmp = $px > $py
            PRINT $cmp
            $has = 20 in $arr
            PRINT "has20:" & $has
            $i = 0
            WHILE $i < 3
                $i += 1
                IF $i == 2
                    CONTINUE
                ENDIF
                PRINT "i=" & $i
            END
            """;

        var result = Compile(source);
        var io = new RecordingIo();
        var lines = RunChain(result, io, new RecordingPad());

        Assert.That(lines[0], Is.EqualTo("n=120,x=11,y=7,len=3,2"), "结构体/递归/数组语义");
        Assert.That(lines[1], Is.EqualTo("true"), "比较与字符串化");
        Assert.That(lines[2], Is.EqualTo("has20:false"), "in 运算：$arr[1]=99 改写后 20 不在数组中");
        Assert.That(lines[3], Is.EqualTo("i=1"), "WHILE + CONTINUE");
    }
}