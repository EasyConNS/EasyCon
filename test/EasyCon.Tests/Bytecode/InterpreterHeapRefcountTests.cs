using EasyCon.Script;
using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 解释器堆引用计数回归（阶段 A，对齐 ecs_vm.c §4.2 / VM2.md §8.4）：
/// 释放形态 = 对象置 null + 自由链表复用下标（句柄 = 列表下标，不移除/移动元素）；
/// 拷贝创建 retain、槽位覆写释放旧值、弹帧释放全部槽位、嵌套视图按普通堆对象计数。
/// 长跑堆必须有界（此前 _heap 只 Store 不回收，长跑自动化脚本内存单调增长）。
/// 注：ECS 变量首次赋值定型且块作用域，测试脚本先声明后循环使用。
/// </summary>
[TestFixture]
public class InterpreterHeapRefcountTests
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

    // ---------- 长跑界限：循环 N 万次创建堆对象，堆峰值有界 ----------

    [Test]
    public void LongRun_StringConcat_HeapBounded()
    {
        var (code, vm, host) = Run("""
            $s = "x"
            $n = 0
            FOR $i = 1 TO 60000
                $s = "v" & $i
            NEXT
            $n = LEN($s)
            PRINT $n
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "6" }), "循环结束后字符串内容正确");
        Assert.That(vm.HeapHighWater, Is.LessThan(1000),
            $"堆峰值应有界（实际 {vm.HeapHighWater}；无回收时约为迭代数×2）");
        Assert.That(vm.HeapCountsConsistent, Is.True, "alloc − release = live 且无负计数");
    }

    [Test]
    public void LongRun_ArrayLiteralAndIndex_HeapBounded()
    {
        // 含 CSE phi 臂回归：循环体内 SetI 的算术 RHS 与 FOR 增量同形（AddInt($i,1)），
        // CSE 删除回边块冗余定义时必须同步改写后继 phi 臂（编码器曾因悬空臂 KeyNotFound）
        var (code, vm, host) = Run("""
            $a = [0, 0, 0]
            $n = ""
            FOR $i = 1 TO 40000
                $a = [$i, $i, $i]
                $a[1] = $i + 1
            NEXT
            $n = $a[0] & "," & $a[1] & "," & $a[2]
            PRINT $n
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "40000,40001,40000" }));
        Assert.That(vm.HeapHighWater, Is.LessThan(1000), "数组逐迭代创建应被回收");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void LongRun_StructPassAndFrameExit_HeapBounded()
    {
        // 结构体传参（实参 S-17 深拷贝）+ 帧退出释放：每迭代创建 2 层帧、2 个结构体容器
        var (code, vm, host) = Run("""
            STRUCT P
                $x:INT
                $y:INT
            END
            FUNC mk($a:INT):P
                $p = P{}
                $p.x = $a
                $p.y = $a * 2
                RETURN $p
            ENDFUNC
            FUNC take($q:P):INT
                RETURN $q.x + $q.y
            ENDFUNC
            $acc = 0
            FOR $i = 1 TO 30000
                $acc = $acc + take(mk($i))
            NEXT
            PRINT $acc
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "1350045000" }), "1..30000 的 3i 累加");
        Assert.That(vm.HeapHighWater, Is.LessThan(1000), "传参拷贝与被弹帧槽位应随帧退出释放");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void LongRun_NestedViewWriteThrough_HeapBounded()
    {
        // GetF 嵌套视图每迭代新建（普通堆对象计数），PutF 经 ViewParent 回写父槽（写穿透，F3 语义）。
        // $front 在嵌套字段之前：回写必须落在 ViewOffset（非 0 偏移曾误写父槽 0）。
        // 注：$view = $o.inner 是 SetVar 深拷贝边界，视图拷贝脱离父结构体；
        // 写穿透仅适用于 GetF 视图句柄直达 PutF 的直接嵌套赋值 $o.inner.v = $i。
        var (code, vm, host) = Run("""
            STRUCT Inner
                $v:INT
            END
            STRUCT Outer
                $front:INT
                $inner:Inner
                $w:INT
            END
            $o = Outer{}
            FOR $i = 1 TO 30000
                $o.front = $i
                $o.inner.v = $i
                $o.w = $i
            NEXT
            $v = $o.inner.v
            PRINT $v
            $w = $o.w
            PRINT $w
            $f = $o.front
            PRINT $f
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "30000", "30000", "30000" }),
            "嵌套视图按 ViewOffset 写穿透父结构体");
        Assert.That(vm.HeapHighWater, Is.LessThan(1000), "每迭代的嵌套视图应被回收");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void LongRun_SliceCatAppend_HeapBounded()
    {
        var (code, vm, host) = Run("""
            $base = [1, 2, 3, 4]
            $s = [0, 0]
            $t = "ab"
            $u = [0]
            $n = ""
            FOR $i = 1 TO 30000
                $s = $base[0:2] + $base[2:4]
                $t = "ab" & "cd"
                $u = APPEND($base, 5)
            NEXT
            $n = LEN($s) & "," & $t & "," & LEN($u)
            PRINT $n
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "4,abcd,5" }));
        Assert.That(vm.HeapHighWater, Is.LessThan(1000), "切片/拼接/追加的临时容器应被回收");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    // ---------- 释放正确性专项 ----------

    [Test]
    public void Overwrite_ReleasesOldHandle_FreeListReused()
    {
        // 同一变量反复覆盖：旧对象释放、下标经自由链复用 → 分配总数远大于堆长度
        var (code, vm, host) = Run("""
            $s = "first"
            FOR $i = 1 TO 10000
                $s = "n" & $i
            NEXT
            PRINT $s
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "n10000" }));
        Assert.That(vm.HeapAllocTotal, Is.GreaterThanOrEqualTo(10000), "每迭代分配新字符串");
        Assert.That(vm.HeapReleaseTotal, Is.GreaterThanOrEqualTo(9999), "旧字符串应被释放");
        Assert.That(vm.HeapLiveCount, Is.LessThan(20), "运行结束仅存活当前值");
        Assert.That(vm.HeapFreeListCount, Is.GreaterThan(0), "自由链应持有可复用下标");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void ErrorPath_MidRun_LeavesConsistentCounts()
    {
        // SimError 中途退出：帧未弹出，但计数自洽、无悬空（负计数/泄漏计数）
        var (code, vm, _) = Run("""
            $a = [1, 2, 3]
            $s = "hello" & "world"
            $x = $a[9]
            PRINT $x
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.ERR_INDEX), "越界应报 SimError");
        Assert.That(vm.HeapCountsConsistent, Is.True, "错误路径计数自洽");
        Assert.That(vm.HeapLiveCount, Is.GreaterThan(0), "错误现场对象仍在堆上（由解释器销毁统一回收）");
    }

    [Test]
    public void DeepCopy_ValueSemantics_NoDanglingAfterSourceOverwrite()
    {
        // 值语义：SetVar 深拷贝出独立容器，源变量覆盖后拷贝仍完好（句柄复用不串值）
        var (code, vm, host) = Run("""
            $a = [1, 2, 3]
            $b = $a
            $other = "now-a-string"
            $c = $b
            $b = [9, 9]
            $n = LEN($c)
            $e = $c[2]
            PRINT $n
            PRINT $e
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "3", "3" }), "拷贝链不受源覆盖影响");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }

    [Test]
    public void GlobalStoreRelease_LongRunBounded()
    {
        var (code, vm, host) = Run("""
            $g = ""
            FOR $i = 1 TO 40000
                $g = "s" & $i
            NEXT
            PRINT $g
            """);

        Assert.That(code, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(host.Lines, Is.EqualTo(new[] { "s40000" }));
        Assert.That(vm.HeapHighWater, Is.LessThan(1000), "全局槽覆盖应释放旧值");
        Assert.That(vm.HeapCountsConsistent, Is.True);
    }
}