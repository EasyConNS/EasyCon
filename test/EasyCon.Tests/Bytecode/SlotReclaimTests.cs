using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 编码期槽位回收（优化后变量提升/删除的编码器配套）：
///   - 局部变量读值全走 SSA（构建期 mem2reg），ECX 里帧槽唯一读取者是参数入口 LoadLocal
///     （Call ABI 播种）→ StoreLocal 不发射，非参数局部不占帧槽（NSlots 收缩）；
///   - 全局提升（SsaGlobalPromotion A/B 类）删除全部 LoadG/StoreG → 不进镜像全局表；
///   - 语义不变量经 EcxInterpreter 模拟对拍锁定（参数窗 ABI、循环 phi、跨函数全局）。
/// </summary>
[TestFixture]
public class SlotReclaimTests
{
    static CompileResult Compile(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(),
            Is.Empty, "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    static List<string> RunChain(CompileResult result)
    {
        var io = new RecordingIo();
        EcxVm.Run(result.Image!, EcsTestHost.Capabilities(io, new RecordingPad()),
            new CancellationTokenSource().Token, [], result.NativeSymbols);
        return io.Lines;
    }

    static HashSet<EcsOpcode> OpcodeSet(EcsFunction f)
    {
        var ops = new HashSet<EcsOpcode>();
        for (int i = 0; i < f.Code.Count;)
        {
            var op = (EcsOpcode)(f.Code[i] & 0xFF);
            ops.Add(op);
            i += EcsFormat.WordCount(op);
        }
        return ops;
    }

    // ---------- 局部帧槽回收 ----------

    [Test]
    public void Dead_Locals_Do_Not_Reserve_Frame_Slots()
    {
        const string source = """
            FUNC heavy($a):INT
                $d1 = 1
                $d2 = $d1 + 2
                $d3 = $d2 * 3
                $d4 = "dead"
                $d5 = 99
                IF $a > 100
                    RETURN 0
                ENDIF
                RETURN $a + 1
            ENDFUNC
            $r = heavy(41)
            PRINT $r
            """;

        var result = Compile(source);
        var fn = result.Image!.Functions.Single(f => f.Name == "heavy");

        // 5 个死局部（SCCP 折叠 + 无观察读取）不再占帧槽：
        // NSlots 压到参数窗 + 少量编码器保留槽（staging/receive）之内；
        // 回收前 layout 至少 params+5，此断言即失败
        Assert.That(fn.NSlots, Is.LessThanOrEqualTo(fn.NParams + 4),
            $"死局部仍占帧槽：params={fn.NParams}, slots={fn.NSlots}");

        // 无 SetVar 残留：局部存储不发射（本函数无 DeepCopy，SetVar 不应有任何来源）
        Assert.That(OpcodeSet(fn), Does.Not.Contain(EcsOpcode.SetVar), "heavy 不应残留局部存储指令");

        Assert.That(RunChain(result), Is.EqualTo(new List<string> { "42" }), "死局部删除不改变可观察语义");
    }

    [Test]
    public void Param_Slots_Kept_For_Call_Abi_While_Locals_Dropped()
    {
        const string source = """
            FUNC mix($a, $b):INT
                $t = $a * 2
                $i = 0
                WHILE $i < 3
                    $t = $t + $b
                    $i = $i + 1
                END
                RETURN $t
            ENDFUNC
            $r1 = mix(2, 3)
            PRINT $r1
            $r2 = mix(10, 1)
            PRINT $r2
            """;

        var result = Compile(source);
        var fn = result.Image!.Functions.Single(f => f.Name == "mix");

        // 参数窗 = Call ABI（实参写入被调帧槽 0..n-1），NSlots 下限恒为 NParams
        Assert.That(fn.NSlots, Is.GreaterThanOrEqualTo(fn.NParams), "参数窗必须保留");
        // 循环变量 $t/$i 全程走 SSA（phi 合并）：无局部存储指令，帧槽只含参数窗 + SSA 临时槽
        Assert.That(OpcodeSet(fn), Does.Not.Contain(EcsOpcode.SetVar), "mix 不应残留局部存储指令");

        Assert.That(RunChain(result), Is.EqualTo(new List<string> { "13", "23" }),
            "参数窗 + 循环 phi 语义（2*2+3*3=13；10*2+1*3=23）");
    }

    // ---------- TRE 回边参数 store ----------

    [Test]
    public void Tail_Recursive_Param_Store_Survives_Encoding()
    {
        const string source = """
            FUNC sum($n, $acc):INT
                IF $n == 0
                    RETURN $acc
                ENDIF
                RETURN sum($n - 1, $acc + $n)
            ENDFUNC
            $r = sum(50000, 0)
            PRINT $r
            """;

        var result = Compile(source);

        // 回归锁：TRE 把自尾调用改写为「回边参数 store + 跳回 entry」，entry（参数 LoadLocal
        // 所在块）随之成为循环头，每轮迭代从帧槽重读参数——该 store 必须发射。
        // 曾因「非参数局部不发射」被误伤：参数永远保持初值 → VM 无限循环。
        var runTask = Task.Run(() => RunChain(result));
        Assert.That(runTask.Wait(15000), Is.True, "尾递归被误编译成死循环（回边参数 store 丢失）");
        Assert.That(runTask.Result, Is.EqualTo(new List<string> { "1250025000" }),
            "TRE 循环语义（1+2+…+50000）");
    }

    // ---------- 全局表回收 ----------

    [Test]
    public void Promoted_Globals_Are_Dropped_From_Image_Global_Table()
    {
        const string source = """
            $constv = 42
            $folded = $constv + 1
            $shared = 7
            FUNC bumpg():INT
                $shared = $shared + 1
                RETURN $shared
            ENDFUNC
            $r = bumpg()
            $r2 = bumpg()
            PRINT $folded
            PRINT $r
            PRINT $r2
            """;

        var result = Compile(source);

        // $constv/$folded：常量全局（A 类）→ 读折叠为常量、store 删除 → 不进镜像全局表
        // $shared：跨函数读写（$eval 与 bumpg 均有 store，C 类）→ 保留全局槽
        var names = result.Image!.Globals.Select(g => g.Name).ToList();
        Assert.That(names, Does.Not.Contain("$constv"), "常量全局不应进镜像全局表");
        Assert.That(names, Does.Not.Contain("$folded"), "常量折叠全局不应进镜像全局表");
        Assert.That(names, Does.Contain("$shared"), "跨函数全局必须保留全局槽");

        Assert.That(RunChain(result), Is.EqualTo(new List<string> { "43", "8", "9" }));
    }
}