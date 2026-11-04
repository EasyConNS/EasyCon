using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 常量串 pinned（契约 S-20 / docs/ZeroAllocVm.md §2）的锁定测试：
/// 常量池来的字符串直接引用镜像数据，**零堆分配、零对象表占用、不参与引用计数**；
/// 内容语义（Eq/Cont/Len/GetI/Cat/TOSTR）必须与动态字符串完全一致。
/// 跨端一致性由 <c>corpus/pinned_strings.ecs</c> 承担（同一镜像同时跑 C# 解释器与 C VM）。
///
/// 断言口径说明：本组只断言"**常量串本身**不产生堆分配"。脚本里 `PRINT &lt;非字符串&gt;` 会经
/// `Conv(ToStr)` 建一个堆串（`EcxInterpreter.cs` `EcsConvKind.ToStr`，两端一致的既有行为），
/// 因此涉及打印整数的用例改为用条件判断观测，避免把该既有分配算进 pinned 的口径。
/// </summary>
[TestFixture]
public class PinnedStringTests
{
    static (int Code, EcxInterpreter Vm, EcxHost Host) Run(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译失败：" + string.Join("\n", result.Diagnostics));
        var host = new EcxHost();
        host.EnableRecording();
        var code = EcxInterpreter.RunWithVm(result.Image!, host, out var vm);
        return (code, vm, host);
    }

    // ---------- 收益断言：常量串不再产生任何堆对象 ----------

    [Test]
    public void LiteralOnly_AllocatesNothing()
    {
        var (code, vm, host) = Run("""
            PRINT "hello"
            PRINT "hello"
            PRINT "world"
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "hello", "hello", "world" }));
        Assert.That(vm.HeapAllocTotal, Is.Zero, "常量串 pinned 后不应有任何堆分配");
        Assert.That(vm.HeapLiveCount, Is.Zero);
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void LiteralInLoop_AllocatesNothing()
    {
        var (code, vm, host) = Run("""
            $n = 0
            FOR $i = 1 TO 100
                PRINT "x"
                $n = $i
            NEXT
            PRINT "done"
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines.Count, Is.EqualTo(101));
        Assert.That(host.Lines[0], Is.EqualTo("x"));
        Assert.That(host.Lines[^1], Is.EqualTo("done"));
        // 改动前：每轮块首物化字面量都新建一个堆串（100+ 次分配）
        Assert.That(vm.HeapAllocTotal, Is.Zero, "循环内物化字面量不得分配（此前每轮一次）");
        Assert.That(vm.HeapHighWater, Is.Zero);
    }

    [Test]
    public void StaticInContainerAndSlot_NoRefcountLeak()
    {
        var (code, vm, host) = Run("""
            $arr = ["x", "y"]
            $g = "z"
            $s = $arr[1] & $g
            PRINT $s
            $same = $g == "z"
            PRINT $same
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "yz", "true" }));
        Assert.That(vm.HeapCountsConsistent, Is.True, "静态值进出容器/全局槽不得破坏 RC 自洽");
        // 唯一允许的分配是数组容器本身；字符串元素与全局字面量均为 pinned
        Assert.That(vm.HeapLiveCount, Is.LessThanOrEqualTo(1), "存活对象只有数组容器");
    }

    // ---------- 调用约定（S-17）与静态值的交互：实参/返回深拷贝路径 ----------

    [Test]
    public void StaticArgumentAndReturn_AllocatesNothing()
    {
        var (code, vm, host) = Run("""
            FUNC measure($s : string) : int
                $n = LEN($s)
                RETURN $n
            ENDFUNC
            FUNC pick() : string
                RETURN "lit"
            ENDFUNC
            $k = measure("hello")
            IF $k == 5
                PRINT "ok"
            ENDIF
            $p = pick()
            PRINT $p
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "ok", "lit" }));
        Assert.That(vm.HeapAllocTotal, Is.Zero,
            "实参深拷贝进新帧槽（S-17）与返回值深拷贝对静态值都必须是 no-op");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    // ---------- 内容语义与动态串完全一致（S-20 的核心约束） ----------

    [Test]
    public void StaticAndDynamic_SameContents_SameResults()
    {
        var (code, vm, host) = Run("""
            $s = "ab"
            $d = "a" & "b"
            $eq = $s == $d
            PRINT $eq
            $rev = $d == $s
            PRINT $rev
            $has = "b" in $s
            PRINT $has
            $n = LEN($d)
            PRINT $n
            $i = $d[1]
            PRINT $i
            $cat = $s & $d
            PRINT $cat
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "true", "true", "true", "2", "b", "abab" }),
            "静态串与动态串必须按内容等价（此前若用新 tag 实现会让 Eq 判不等）");
        Assert.That(vm.HeapAllocTotal, Is.GreaterThanOrEqualTo(1), "动态拼接仍走堆（反证：只有常量被 pinned）");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void RepeatedLiteralAssignment_HeapDoesNotGrow()
    {
        var (code, vm, host) = Run("""
            $s = "x"
            FOR $i = 1 TO 1000
                $s = "y"
            NEXT
            PRINT $s
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "y" }));
        Assert.That(vm.HeapHighWater, Is.Zero, "反复覆写槽位为字面量不得产生存活对象");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    // ---------- 静态句柄不得进入对象表（防御） ----------

    [Test]
    public void StaticHandle_IsNegative_SoHeapGuardsRejectIt()
    {
        var v = TaggedValue.FromStaticString(3);

        Assert.Multiple(() =>
        {
            Assert.That(v.IsString, Is.True);
            Assert.That(v.IsStatic, Is.True);
            Assert.That(v.StaticIndex, Is.EqualTo(3));
            Assert.That(v.Handle, Is.LessThan(0), "静态句柄 bit31 置位 ⇒ int32 负值，一切 h > 0 守卫天然拒绝");
            Assert.That(TaggedValue.FromStringHandle(7).IsStatic, Is.False, "普通句柄不得被误判为静态");
        });
    }
}