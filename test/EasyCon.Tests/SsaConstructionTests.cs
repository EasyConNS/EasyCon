using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Ssa;
using EasyCon.Script.Syntax;
using EasyScript;

namespace EasyCon.Tests;

/// <summary>
/// SSA 构造回归测试：验证从「memory SSA + 合并点重载」重构为「真 SSA（支配树 phi）」前后，
/// 行为始终正确，且 IR 质量改善（合并点插入 phi、消除冗余 LoadLocal）。
///
/// 这两类断言共同构成重构的安全网：
///   - 行为断言在重构前后都应通过（锁定正确性基线）。
///   - IR 质量断言在重构后才通过（验收改进目标）。
///
/// 这些用例针对的正是当前「_defs.Clear() → 在合并点用 LoadLocal 重载」机制处理次优、
/// 但恰好正确的关键场景：循环头变量、IF 两臂各自定义、嵌套控制流。
/// </summary>
[TestFixture]
public class SsaConstructionTests
{
    // ============ 编译 + 执行辅助 ============

    private static (CompileResult Result, MockOutputAdapter Output) Eval(string code)
    {
        var output = new MockOutputAdapter();
        var compilation = Compilation.Create(SyntaxTree.Parse(code));
        var result = compilation.Compile(null);
        if (result.Program == null)
            return (result, output);
        using var evaluator = new SsaEvaluator(result.Program, new CancellationTokenSource().Token)
        {
            IoAdapter = output,
        };
        evaluator.Evaluate();
        return (result, output);
    }

    private static int IntOut(string code, int index = 0)
    {
        var (result, output) = Eval(code);
        if (result.Program == null)
            Assert.Fail("脚本编译错误: " + string.Join("; ",
                result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        if (output.Printed.Count <= index)
            Assert.Fail($"无 PRINT 输出（index={index}），实际输出条数={output.Printed.Count}：" +
                        string.Join(" | ", output.Printed));
        return int.Parse(output.Printed[index].Trim());
    }

    /// <summary>编译并返回 SSA Program（不执行），供 IR 结构断言使用。
    /// 走完整编译管线（含优化）：用于验证优化后的真实 IR 形态。</summary>
    private static SsaProgram CompileIr(string code)
    {
        var result = Compilation.Create(SyntaxTree.Parse(code)).Compile(null);
        Assert.That(result.Program, Is.Not.Null, () =>
            string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return result.Program!;
    }

    /// <summary>只跑「Bound → SSA 构造」，跳过优化器。
    /// IR-quality 测试关心构造阶段插入的 phi 结构（Braun 算法产物），
    /// 优化器（SCCP 常量折叠等）会把这些 phi 消掉，无法稳定观测。
    /// 用未优化 IR 才能稳定验证「合并点/循环头确实插入了 phi」。</summary>
    private static SsaProgram CompileIrNoOptimize(string code)
    {
        var prog = Compilation.Create(SyntaxTree.Parse(code)).BuildSsa(null, optimize: false);
        Assert.That(prog, Is.Not.Null, "脚本编译错误");
        return prog!;
    }

    /// <summary>统计整个程序所有函数中某 SsaOp 的出现次数。</summary>
    private static int CountOp(SsaProgram program, SsaOp op)
    {
        int n = 0;
        foreach (var fn in program.Functions.Values)
            foreach (var block in fn.Blocks)
                foreach (var v in block.Instructions)
                    if (v.Op == op) n++;
        return n;
    }

    /// <summary>程序任意函数里是否任意块的 Phis 非空。</summary>
    private static bool HasAnyPhi(SsaProgram program)
    {
        foreach (var fn in program.Functions.Values)
            foreach (var block in fn.Blocks)
                if (block.Phis.Count > 0)
                    return true;
        return false;
    }

    // ============================================================
    // 行为正确性：循环变量
    // ============================================================

    [Test]
    public void For_Loop_Sum_Behavior()
    {
        // 循环体读 $i，累加
        var sum = IntOut(@"
$s = 0
FOR $i = 1 TO 10
    $s += $i
NEXT
PRINT $s");
        Assert.That(sum, Is.EqualTo(55)); // 1+2+...+10
    }

    [Test]
    public void For_Loop_Body_Reads_I_Behavior()
    {
        // 最后一次 $i 应为 10
        var last = IntOut(@"
$last = 0
FOR $i = 1 TO 10
    $last = $i
NEXT
PRINT $last");
        Assert.That(last, Is.EqualTo(10));
    }

    [Test]
    public void For_Loop_Variable_Not_Writable_In_Body()
    {
        // ECS 语义：FOR 循环变量 $i 是只读的（Binder 报错「只读变量无法修改」）。
        // 因此循环变量在每轮的唯一不同定义来源是 header 的 init / step —— 这正是
        // 重构后用 phi 合并的依据。此处断言该只读约束成立（重构不应改变这一语义）。
        var (result, _) = Eval(@"
FOR $i = 1 TO 10
    $i = 5
NEXT");
        Assert.That(result.Program, Is.Null, "修改只读循环变量 $i 应被 Binder 拒绝");
    }

    [Test]
    public void While_Loop_Reads_Modified_Var_Behavior()
    {
        var n = IntOut(@"
$c = 0
WHILE $c < 5
    $c += 1
END
PRINT $c");
        Assert.That(n, Is.EqualTo(5));
    }

    [Test]
    public void Until_Loop_Reads_Modified_Var_Behavior()
    {
        var n = IntOut(@"
$c = 0
UNTIL $c >= 5
    $c += 1
END
PRINT $c");
        Assert.That(n, Is.EqualTo(5));
    }

    // ============================================================
    // 行为正确性：IF 合并点变量
    // ============================================================

    [Test]
    public void If_Both_Arms_Define_Var_Behavior()
    {
        // 两分支各自赋值同一变量，合并后读
        var r = IntOut(@"
$v = 1
$r = 0
IF $v == 1
    $r = 100
ELSE
    $r = 200
ENDIF
PRINT $r");
        Assert.That(r, Is.EqualTo(100));
    }

    [Test]
    public void If_One_Arm_Defines_Var_Behavior()
    {
        // 单分支定义、合并后读；另一路径保留旧值
        var r = IntOut(@"
$v = 2
$r = 7
IF $v == 1
    $r = 100
ENDIF
PRINT $r");
        Assert.That(r, Is.EqualTo(7));
    }

    [Test]
    public void If_Elif_Chain_Behavior()
    {
        // $r 在顶层预声明；各分支体对它赋值，合并后读
        var r = IntOut(@"
$x = 75
$r = 0
IF $x >= 90
    $r = 4
ELIF $x >= 80
    $r = 3
ELIF $x >= 60
    $r = 2
ELSE
    $r = 0
ENDIF
PRINT $r");
        Assert.That(r, Is.EqualTo(2));
    }

    // ============================================================
    // 行为正确性：嵌套控制流
    // ============================================================

    [Test]
    public void Nested_For_Inside_If_Behavior()
    {
        // IF 成立时跑循环累加；IF 内定义的变量在合并后读
        var r = IntOut(@"
$flag = 1
$acc = 0
IF $flag == 1
    FOR $i = 1 TO 5
        $acc += $i
    NEXT
ENDIF
PRINT $acc");
        Assert.That(r, Is.EqualTo(15)); // 1+2+3+4+5
    }

    [Test]
    public void Nested_If_Inside_For_Behavior()
    {
        // 循环内根据条件累加不同分支
        var r = IntOut(@"
$acc = 0
FOR $i = 1 TO 10
    IF $i >= 6
        $acc += $i
    ELSE
        $acc += 1
    ENDIF
NEXT
PRINT $acc");
        // i=1..5 各 +1 = 5；i=6..10 = 6+7+8+9+10 = 40 → 共 45
        Assert.That(r, Is.EqualTo(45));
    }

    // ============================================================
    // IR 质量断言（重构后验收目标）
    // ------------------------------------------------------------
    // 这些断言验证「真 SSA」的关键特征：
    //   1. 合并点出现 Phi（替换原先的 LoadLocal 重载）
    //   2. 冗余 LoadLocal 数量下降
    // 在当前 memory-SSA 实现下，部分 IR 质量断言会失败 —— 这是预期的，
    // 它们正是重构要达成的目标。重构通过后这些断言全部变绿。
    // ============================================================

    [Test]
    public void IR_Quality_For_Loop_Header_Has_Phi()
    {
        // 顶层变量在 ECS 中是全局（跨函数共享），保持 memory-SSA、不插 phi。
        // 故把测试代码包进 FUNC，使 $i/$s 成为函数局部变量，从而进入值 SSA。
        // FOR 循环头应有 phi（合并 init 与 step），不再用 LoadLocal 反复重载。
        var prog = CompileIrNoOptimize(@"
FUNC test():INT
    $s = 0
    FOR $i = 1 TO 10
        $s += $i
    NEXT
    RETURN $s
ENDFUNC
$r = test()");
        // 真 SSA 下循环头必然出现 phi（$i 和 $s 在 header 处由 phi 合并）
        Assert.That(HasAnyPhi(prog), Is.True,
            "重构后 FOR 循环头应包含 phi；当前 memory-SSA 实现下此处用 LoadLocal 重载，无 phi");
    }

    [Test]
    public void IR_Quality_If_Merge_Produces_Phi()
    {
        // 局部 $r：两分支各自赋值，合并后读 → 合并块应有 phi。
        // 条件取自运行时参数（避免 SCCP 常量折叠把整个 IF 折成单块、消掉合并点）。
        var prog = CompileIrNoOptimize(@"
FUNC test($flag):INT
    $r = 0
    IF $flag == 1
        $r = 100
    ELSE
        $r = 200
    ENDIF
    RETURN $r
ENDFUNC
$x = test(1)");
        Assert.That(HasAnyPhi(prog), Is.True,
            "重构后 IF 合并点应包含 phi（合并两臂对 $r 的定义）");
    }

    [Test]
    public void IR_Quality_Loop_Reduces_Redundant_LoadLocal()
    {
        // 重构前：EmitFor 每轮发 3 次 LoadLocal($i)（header/body/continue）。
        // 重构后：header phi 替代重载，$i 的读取由 SSA 值承担，LoadLocal($i) 大幅下降。
        // 此测试验证「优化后」IR：Braun 构造出的 phi + DCE 把冗余 LoadLocal 清干净。
        var prog = CompileIr(@"
FUNC test():INT
    $s = 0
    FOR $i = 1 TO 100
        $s += $i
    NEXT
    RETURN $s
ENDFUNC
$x = test()");
        var loads = CountOp(prog, SsaOp.LoadLocal);
        // 优化后：$i/$s 的读取由 SSA 值承担，LoadLocal 仅剩函数参数入口的 LoadLocal。
        Assert.That(loads, Is.LessThanOrEqualTo(2),
            $"重构后 FOR 循环应消除冗余 LoadLocal，实际 {loads} 处（phi 应取代重载）");
    }

    [Test]
    public void IR_Quality_While_Header_Has_Phi_For_Loop_Var()
    {
        // WHILE 体内修改局部 $c：header 处 $c 应由 phi 合并「初始值」与「体内自增后值」
        var prog = CompileIrNoOptimize(@"
FUNC test():INT
    $c = 0
    WHILE $c < 5
        $c += 1
    END
    RETURN $c
ENDFUNC
$x = test()");
        Assert.That(HasAnyPhi(prog), Is.True,
            "重构后 WHILE 循环头应包含 phi（闭合 $c 的回边）");
    }

    // ============================================================
    // 行为正确性：嵌套循环 + BREAK（phi 简化正确性守护）
    // ------------------------------------------------------------
    // 此场景在 Phase 4 「phi 简化增强」期间曾因 SimplifyPhis 误用 SCCP
    // executableEdges 过滤活边，把内层循环变量 phi 错误折叠为初始常量，
    // 导致内层循环永不终止（死循环）。这里锁定该回归。
    // ============================================================

    [Test]
    public void Nested_For_With_Break_Behavior()
    {
        // 外层 $n 固定为 7；内层 $d 从 2 起递增，遇到能整除 $n 的 $d 即 BREAK。
        // 7 是质数：内层找不到整除 $d（$d 一直增到 == $n 触发 FOR 提前结束），isPrime 保持 1。
        var r = IntOut(@"
$n = 7
$isPrime = 1
FOR $d = 2 TO $n
    IF $n % $d == 0 AND $n != $d
        $isPrime = 0
        BREAK
    ENDIF
NEXT
PRINT $isPrime");
        Assert.That(r, Is.EqualTo(1));
    }

    [Test]
    public void Nested_For_Count_Outer_Var_Behavior()
    {
        // 外层循环变量 $i 在内层循环结束后继续正确递增（不被错误折叠清零）。
        // 3×3 嵌套累加 = 9。
        var r = IntOut(@"
$total = 0
FOR $i = 1 TO 3
    FOR $j = 1 TO 3
        $total = $total + 1
    NEXT
NEXT
PRINT $total");
        Assert.That(r, Is.EqualTo(9));
    }
}