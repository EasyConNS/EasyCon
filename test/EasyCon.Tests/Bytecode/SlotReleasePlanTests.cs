using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 槽位结算计划（登记/结算单一推导，发射侧回放）的结构守护。
/// 每个用例走真实编译管线（SSA→ECX 编码，Debug 构建下断言活跃），
/// 断言无错误诊断且无 [slots] 结算失衡告警——欠结算与超结算统一在此暴露。
/// 语料按结算机制分形：立即数/寄存器操作数、同块多读常量、跨块常量、
/// φ 形态（循环头活 φ、IF 汇合活 φ、常量折叠死 φ、FOR 收尾融合块）、
/// 三种终结符出口（Ret/CondBranch/Jump）、多参数调用的 ExtraArgs 结算。
/// </summary>
[TestFixture]
public class SlotReleasePlanTests
{
    static void AssertClean(string source)
    {
        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
            Assert.Multiple(() =>
            {
                Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
                    "编译诊断：" + string.Join("\n", result.Diagnostics));
                Assert.That(captured.ToString(), Does.Not.Contain("[slots]"),
                    "槽位结算失衡（登记/结算计数不匹配）");
            });
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Test]
    public void ImmediateDurations_InLoop_CompilesClean()
    {
        // KeyPress/Wait 常量时长按立即数编码（不读槽）：登记与结算计划同点过滤
        AssertClean("""
            FOR 3
            A
            WAIT 50
            NEXT
            """);
    }

    [Test]
    public void SameConstReadMultipleTimesInBlock_CompilesClean()
    {
        // 同块内同一常量多次登记（UseCount>1），逐次结算、末次归零归还
        AssertClean("""
            $r = RAND(10)
            $a = $r + 5
            $b = $a * 5
            $c = $b - 5
            PRINT $c
            """);
    }

    [Test]
    public void ConstReadAcrossBlocks_CompilesClean()
    {
        // 跨块常量：各使用块独立槽与独立计数（块首物化、块内各自结算）
        AssertClean("""
            $r = RAND(10)
            FOR 3
            $t = $r + 7
            PRINT $t
            NEXT
            """);
    }

    [Test]
    public void WhileLoop_LivePhi_CompilesClean()
    {
        // 循环头活 φ：臂读取按前驱对齐登记，回边终结符出边结算
        AssertClean("""
            $i = 1
            WHILE $i <= 5
            PRINT $i
            $i += 1
            END
            """);
    }

    [Test]
    public void IfElseJoin_LivePhi_CompilesClean()
    {
        // IF/ELSE 汇合活 φ（RAND 对编译器不透明，不可折叠）：两臂边副本 + 汇合读取
        AssertClean("""
            $x = 0
            $c = RAND(2) * 0
            IF $c == 1
            $x = 10
            ELSE
            $x = 20
            ENDIF
            PRINT $x
            """);
    }

    [Test]
    public void IfElse_FoldedDeadPhi_CompilesClean()
    {
        // 常量条件被折叠 → 汇合块留下零读取死 φ：臂登记一次、结算恰一次
        AssertClean("""
            $c = 0
            $x = 0
            FOR 100
            IF $c == 1
            $x = 1
            ELSE
            $x = 2
            ENDIF
            PRINT $x
            NEXT
            """);
    }

    [Test]
    public void ForLoop_WithWaitImmediate_CompilesClean()
    {
        // FOR 收尾 EqInt 与 WAIT 立即数时长共用内联常量：融合块内登记/结算不失衡
        AssertClean("""
            FOR 100
            WAIT 100
            NEXT
            """);
    }

    [Test]
    public void MultiArgCall_ExtraArgs_CompilesClean()
    {
        // Call/StaticCall 的 ExtraArgs 逐个登记、逐个结算（staging 区 arity 路径）
        AssertClean("""
            FUNC add3($a, $b, $c) : int
            RETURN $a + $b + $c
            ENDFUNC
            $s = add3(1, 2, 3)
            PRINT $s
            """);
    }

    [Test]
    public void RecursiveCall_WithBranchyReturns_CompilesClean()
    {
        // 函数内多出口：多个 Return 块的终结符计划 + 调用 φ
        AssertClean("""
            FUNC fact($n) : int
            IF $n <= 1
            RETURN 1
            ENDIF
            RETURN $n * fact($n - 1)
            ENDFUNC
            $r = fact(5)
            PRINT $r
            """);
    }

    [Test]
    public void BreakContinue_InNestedLoops_CompilesClean()
    {
        // BREAK/CONTINUE 跳转目标（有无 φ 混合）的 JumpTarget 出边
        AssertClean("""
            FOR 3
            FOR 3
            IF RAND(2) == 1
            CONTINUE
            ENDIF
            BREAK 2
            NEXT
            NEXT
            """);
    }

    [Test]
    public void ArrayIndex_StoreAndLoad_CompilesClean()
    {
        // 数组：ArrayInit 的 ExtraArgs + StoreIndex/索引读取
        AssertClean("""
            $a = [1, 2, 3]
            $a[1] = 9
            $v = $a[1]
            PRINT $v
            """);
    }

    // ── 语义抽查：槽位复用正确性（防止"提前归还→结果复用→读错槽"） ──

    [Test]
    public void ForLoop_CountPreserved_AfterSettlement()
    {
        var result = Compilation.CompileSource("""
            FOR 5
            PRINT "x"
            NEXT
            """, new CompileOptions { UseDiskCache = false });
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0), "解释器执行失败");
        Assert.That(host.Lines, Has.Count.EqualTo(5), "FOR 5 应执行 5 次");
    }

    [Test]
    public void IfElseJoin_TakesRuntimeBranch_AfterPhiCopies()
    {
        var result = Compilation.CompileSource("""
            $x = 0
            $c = RAND(2) * 0
            IF $c == 1
            $x = 10
            ELSE
            $x = 20
            ENDIF
            PRINT $x
            """, new CompileOptions { UseDiskCache = false });
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0), "解释器执行失败");
        Assert.That(host.Lines, Is.EqualTo(new[] { "20" }), "RAND(2)*0 恒为 0，应走 ELSE 臂");
    }

    [Test]
    public void WhileLoop_CounterValues_AccurateAcrossIterations()
    {
        var result = Compilation.CompileSource("""
            $i = 1
            WHILE $i <= 5
            PRINT $i
            $i += 1
            END
            """, new CompileOptions { UseDiskCache = false });
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0), "解释器执行失败");
        Assert.That(host.Lines, Is.EqualTo(new[] { "1", "2", "3", "4", "5" }), "循环 φ 应逐迭代取到正确值");
    }
}