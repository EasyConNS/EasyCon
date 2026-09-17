using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 回归（死 φ 臂双重结算）：优化后全函数零读取的 φ 不占槽、不发边副本，
/// 但其臂在 EmitEdgeCopies 中曾被结算两次——死 φ 分支一次 + 尾部全量结算循环
/// 再一次；当臂是池化常量且块内仅登记 1 次读取时计数 0 → -1，触发
/// SlotAllocator「读取结算次数超过登记次数」断言（Debug 构建进程终止）。
/// 触发形状：循环 + 可常量折叠的 IF/ELSE 对同一变量赋值 + 读取点同被折叠，
/// 循环头/出口块留下零读取 φ。用户报告 FOR 100 崩溃；WHILE 等价写法同样命中，
/// 与 FOR 语法本身无关。
/// </summary>
[TestFixture]
public class DeadPhiSlotSettlementTests
{
    const string FoldedIfElseInLoop = """
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
        """;

    static void AssertCompiles(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(),
            Is.Empty, "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
    }

    [Test]
    public void For_WithConstantFoldedIfElse_CompilesClean()
    {
        AssertCompiles(FoldedIfElseInLoop);
    }

    [Test]
    public void While_WithConstantFoldedIfElse_CompilesClean()
    {
        AssertCompiles("""
            $c = 0
            $x = 0
            $i = 1
            WHILE $i <= 100
            IF $c == 1
            $x = 1
            ELSE
            $x = 2
            ENDIF
            PRINT $x
            $i += 1
            END
            """);
    }

    [Test]
    public void PlainForLoop_CompilesClean()
    {
        AssertCompiles("""
            FOR 100
            PRINT "x"
            NEXT
            """);
    }

    [Test]
    public void For_WithWaitImmediate_CompilesClean()
    {
        // WAIT 的常量时长按立即数编码（不读槽）；FOR 上限常量与之同值内联，
        // 且循环尾 EqInt 在同块登记该常量的真实读取——发射侧若不过滤立即数
        // 释放，会抢走该登记（槽位提前归还 + 计数扣负触发断言）
        AssertCompiles("""
            FOR 100
            WAIT 100
            NEXT
            """);
    }

    [Test]
    public void FoldedBranch_SemanticsPreserved()
    {
        var result = Compilation.CompileSource(FoldedIfElseInLoop, new CompileOptions { UseDiskCache = false });
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0), "解释器执行失败");
        Assert.That(host.Lines, Has.Count.EqualTo(100), "FOR 100 应执行 100 次");
        Assert.That(host.Lines.Distinct().ToList(), Is.EqualTo(new[] { "2" }), "恒假条件应恒走 ELSE 臂 $x = 2");
    }
}