using EasyCon.Script;
using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// COW（move-on-unique）语义与收益回归：
/// - Q1-A 可观察值语义双态逐字一致（别名独立、深拷贝边界、嵌套视图写穿透）；
/// - 收益：唯一引用（新容器字面量）的赋值/返回跳过容器深拷贝——分配计数对比；
/// - rc&gt;1（变量→变量）维持 S-17 深拷贝——两态分配计数一致。
/// </summary>
[TestFixture]
public class CowSemanticsTests
{
    static (int Code, EcxInterpreter Vm, EcxHost Host) Run(string source, bool cow)
    {
        EcxInterpreter.CopyOnWrite = cow;
        try
        {
            var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
            Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
                "编译失败：" + string.Join("\n", result.Diagnostics));
            var host = new EcxHost();
            host.EnableRecording();
            var code = EcxInterpreter.RunWithVm(result.Image!, host, out var vm);
            return (code, vm, host);
        }
        finally
        {
            EcxInterpreter.CopyOnWrite = true;   // 恢复默认态，避免污染其他测试
        }
    }

    [Test]
    public void ValueSemantics_IdenticalAcrossStates()
    {
        // 别名独立（Q1-A）：$b = $a 后写 $b 不得影响 $a；覆盖数组/结构体/全局/传参/返回
        const string source = """
            STRUCT P
                $v:INT
            END
            FUNC take($q:P):INT
                $q.v = 999
                RETURN $q.v
            ENDFUNC
            FUNC echo($arr:INT[]):INT[]
                $arr[0] = 777
                RETURN $arr
            ENDFUNC
            $a = [1, 2, 3]
            $b = $a
            $b[0] = 99
            $x = $a[0]
            PRINT $x
            $x = $b[0]
            PRINT $x
            $p = P{}
            $p.v = 7
            $r = take($p)
            $x = $p.v
            PRINT $x
            $x = $r
            PRINT $x
            $g = [5, 6]
            $h = $g
            $h[1] = 66
            $x = $g[1]
            PRINT $x
            """;

        var (_, vmOn, hostOn) = Run(source, cow: true);
        var (_, vmOff, hostOff) = Run(source, cow: false);

        Assert.That(hostOn.Lines, Is.EqualTo(hostOff.Lines), "双态输出逐字一致");
        Assert.That(hostOn.Lines, Is.EqualTo(new[] { "1", "99", "7", "999", "6" }),
            "值语义：变量→变量拷贝后互不影响；传参/返回为独立副本");
        Assert.That(vmOn.HeapCountsConsistent, Is.True);
        Assert.That(vmOff.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void FreshAssign_SkipsContainerCopy_WhenCowOn()
    {
        // 新容器字面量赋值：COW 开 → 每迭代仅字面量一次分配；关 → 字面量 + 深拷贝两次
        const string source = """
            $s = [0, 0, 0]
            FOR $i = 1 TO 3000
                $s = [1, 2, 3]
            NEXT
            $n = LEN($s)
            PRINT $n
            """;

        var (_, on, hostOn) = Run(source, cow: true);
        var (_, off, hostOff) = Run(source, cow: false);

        Assert.That(hostOn.Lines, Is.EqualTo(hostOff.Lines), "双态行为一致");
        Assert.That(hostOn.Lines, Is.EqualTo(new[] { "3" }));
        Assert.That(on.HeapAllocTotal, Is.LessThan(off.HeapAllocTotal),
            $"COW 开应跳过容器深拷贝（开={on.HeapAllocTotal}，关={off.HeapAllocTotal}）");
        Assert.That(on.HeapAllocTotal, Is.LessThan(6000), "开态 ≈ 每迭代一次分配（3000 字面量 + 少量）");
        Assert.That(on.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void FreshReturn_MovesInsteadOfCopies_WhenCowOn()
    {
        // 函数返回新容器字面量：Ret 站点唯一引用移交
        const string source = """
            FUNC make():INT[]
                RETURN [1, 2, 3]
            ENDFUNC
            $s = [0, 0, 0]
            FOR $i = 1 TO 3000
                $s = make()
            NEXT
            $n = LEN($s)
            PRINT $n
            """;

        var (_, on, hostOn) = Run(source, cow: true);
        var (_, off, hostOff) = Run(source, cow: false);

        Assert.That(hostOn.Lines, Is.EqualTo(hostOff.Lines));
        Assert.That(hostOn.Lines, Is.EqualTo(new[] { "3" }));
        Assert.That(on.HeapAllocTotal, Is.LessThan(off.HeapAllocTotal),
            $"返回站点 COW 应减少分配（开={on.HeapAllocTotal}，关={off.HeapAllocTotal}）");
        Assert.That(on.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void AliasedAssign_StillDeepCopies_InBothStates()
    {
        // 变量→变量（源 rc>1）：两态都走 S-17 深拷贝 → 分配计数同量级
        const string source = """
            $a = [1, 2, 3]
            $b = [0, 0, 0]
            FOR $i = 1 TO 3000
                $b = $a
            NEXT
            $n = LEN($b)
            PRINT $n
            """;

        var (_, on, hostOn) = Run(source, cow: true);
        var (_, off, hostOff) = Run(source, cow: false);

        Assert.That(hostOn.Lines, Is.EqualTo(hostOff.Lines));
        Assert.That(hostOn.Lines, Is.EqualTo(new[] { "3" }));
        Assert.That(on.HeapAllocTotal, Is.EqualTo(off.HeapAllocTotal).Within(off.HeapAllocTotal / 10),
            "rc>1 路径两态都深拷贝（分配计数不因 COW 减少）");
    }

    [Test]
    public void LongRun_HeapBounded_InBothStates()
    {
        const string source = """
            $s = [0, 0]
            $acc = 0
            FOR $i = 1 TO 20000
                $s = [$i, $i]
                $acc = $acc + $s[0]
            NEXT
            PRINT $acc
            """;

        var (_, on, hostOn) = Run(source, cow: true);
        var (_, off, hostOff) = Run(source, cow: false);

        Assert.That(hostOn.Lines, Is.EqualTo(hostOff.Lines));
        Assert.That(hostOn.Lines, Is.EqualTo(new[] { "200010000" }));
        Assert.That(on.HeapHighWater, Is.LessThan(1000), "开态堆峰值有界");
        Assert.That(off.HeapHighWater, Is.LessThan(1000), "关态堆峰值有界");
    }
}