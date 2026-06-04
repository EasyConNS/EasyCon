using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Diagnostics;

namespace EasyCon.Tests;

/// <summary>
/// 性能基准测试：验证 Evaluator 模块 7 项优化后的性能指标。
///
/// 测试维度：
///   A. 循环吞吐量（Thread.Yield 计数化）
///   B. labelIndex 缓存 + 函数调用开销
///   C. ArrayPool 消除参数分配
///   D. 尾递归优化（跳帧复用 + Value[] 缓存）
///   E. 全局变量数组化读写
///   F. 字符串操作消除 StringInfo
///   G. 综合场景：素数筛
/// </summary>
[TestFixture]
public class PerformanceBenchmarks
{
    private const int BenchmarkIterations = 5;

    /// <summary>
    /// 执行编译后的脚本，返回执行耗时(ms)和 PRINT 输出
    /// </summary>
    private static (double Ms, string[] Output) RunScript(CompileResult compileResult)
    {
        if (compileResult.Program == null)
        {
            var errors = compileResult.Diagnostics
                .Where(d => d.IsError)
                .Select(d => d.Message)
                .ToList();
            Assert.Fail($"脚本编译错误: {string.Join("; ", errors)}");
        }

        // 正式测量
        var output = new MockOutputAdapter();
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var evaluator = new SsaEvaluator(compileResult.Program, cts.Token) { IoAdapter = output };
        evaluator.Evaluate();
        sw.Stop();

        return (sw.Elapsed.TotalMilliseconds, output.Printed.ToArray());
    }

    /// <summary>
    /// 多次执行取中位数
    /// </summary>
    private static (double MedianMs, string[] Output) Benchmark(string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(code)).Compile(null);

        var times = new List<double>();
        string[]? lastOutput = null;

        for (int i = 0; i < BenchmarkIterations; i++)
        {
            var (ms, output) = RunScript(compilation);
            times.Add(ms);
            lastOutput = output;
        }

        times.Sort();
        return (times[BenchmarkIterations / 2], lastOutput!);
    }

    private static int ParseIntOutput(string[] output, int index = 0)
    {
        if (output.Length <= index)
            Assert.Fail($"PRINT 输出只有 {output.Length} 行，但尝试访问索引 {index}");
        var s = output[index].Trim('\n', '\r', ' ');
        if (!int.TryParse(s, out var val))
            Assert.Fail($"无法解析 PRINT 输出 '{output[index]}' (trimmed='{s}') 为 int");
        return val;
    }

    // ============================================================
    // A. 循环吞吐量 — Thread.Yield 计数化
    // ============================================================

    [Test]
    public void Benchmark_LoopThroughput()
    {
        // 100 万次纯计数 FOR 循环
        // 优化前：每次 goto(循环回跳) 都 Thread.Yield
        // 优化后：每 1000 次迭代才 Yield 一次
        var code = @"
$count = 0
FOR $i = 1 TO 1000000
    $count = $count + 1
NEXT
PRINT $count
";
        var (ms, output) = Benchmark(code);

        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(1000000), "正确性：累加应为 1000000");

        Console.WriteLine($"[A] 循环吞吐量: 100 万次 = {ms:F1}ms ({1_000_000.0 / ms / 1000:F1}K iter/s)");
    }

    // ============================================================
    // B. 函数调用开销 — labelIndex 缓存
    // ============================================================

    [Test]
    public void Benchmark_FunctionCallOverhead()
    {
        // 定义简单加法函数，循环调用 10 万次
        // labelIndex 缓存使每次调用不重建 label 映射
        var code = @"
FUNC add($a, $b) : int
    RETURN $a + $b
ENDFUNC

$sum = 0
FOR $i = 1 TO 100000
    $sum = add($sum, 1)
NEXT
PRINT $sum
";
        var (ms, output) = Benchmark(code);

        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(100000), "正确性：累加应为 100000");

        Console.WriteLine($"[B] 函数调用: 10 万次 = {ms:F1}ms ({100_000.0 / ms:F0} calls/s)");
    }

    // ============================================================
    // C. 多参数函数 — ArrayPool 分配优化
    // ============================================================

    [Test]
    public void Benchmark_MultiArgFunctionCall()
    {
        // 4 参数函数调用，触发 ArrayPool 租借而非 new Value[]
        var code = @"
FUNC calc($a, $b, $c, $d) : int
    RETURN $a + $b + $c + $d
ENDFUNC

$sum = 0
FOR $i = 1 TO 100000
    $sum = calc($sum, 1, 2, 3)
NEXT
PRINT $sum
";
        var (ms, output) = Benchmark(code);

        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(600000), "正确性：100000 * (1+2+3) = 600000");

        Console.WriteLine($"[C] 4 参数调用: 10 万次 = {ms:F1}ms ({100_000.0 / ms:F0} calls/s)");
    }

    // ============================================================
    // D. 尾递归 — 跳帧复用 + Value[] 缓存
    // ============================================================

    [Test]
    public void Benchmark_TailRecursion_Reuse()
    {
        // 尾递归求和：sum(50000, 0) = 50000*50001/2 = 1250025000
        // 跳帧复用：同一 EvalFrame 在 while(true) 中反复使用，避免递归栈溢出
        // Value[] 缓存：每次迭代 new Value[] 替换 _valueCache，避免残留旧值
        var code = @"
FUNC sum($n, $acc) : int
    IF $n == 0
        RETURN $acc
    ENDIF
    RETURN sum($n - 1, $acc + $n)
ENDFUNC

$r = sum(50000, 0)
PRINT $r
";
        var (ms, output) = Benchmark(code);

        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(1250025000), "正确性：50000 的累加和");

        Console.WriteLine($"[D] 尾递归 50000 层: {ms:F1}ms (跳帧复用 + Value[] 缓存)");
    }

    // ============================================================
    // E. 全局变量读写 — 数组化存储
    // ============================================================

    [Test]
    public void Benchmark_GlobalVariableReadWrite()
    {
        // 密集读写 5 个全局变量，验证数组索引 vs 哈希查找
        var code = @"
$a = 0
$b = 0
$c = 0
$d = 0
$e = 0
FOR $i = 1 TO 200000
    $a = $a + 1
    $b = $b + 2
    $c = $c + 3
    $d = $d + 4
    $e = $e + 5
NEXT
PRINT $a
PRINT $b
PRINT $c
PRINT $d
PRINT $e
";
        var (ms, output) = Benchmark(code);

        Assert.That(ParseIntOutput(output, 0), Is.EqualTo(200000));
        Assert.That(ParseIntOutput(output, 1), Is.EqualTo(400000));
        Assert.That(ParseIntOutput(output, 2), Is.EqualTo(600000));
        Assert.That(ParseIntOutput(output, 3), Is.EqualTo(800000));
        Assert.That(ParseIntOutput(output, 4), Is.EqualTo(1000000));

        var totalOps = 5 * 200000 * 2; // 读+写各算一次
        Console.WriteLine($"[E] 全局变量: 5 var × 20 万次 = {ms:F1}ms ({totalOps * 1.0 / ms / 1000:F1}M ops/s)");
    }

    // ============================================================
    // F. 字符串 LEN — 消除 StringInfo 分配
    // ============================================================

    [Test]
    public void Benchmark_StringLength()
    {
        // 反复调用 LEN()，验证不再每次 new StringInfo
        var code = @"
$s = ""abcdefghij""
$len = 0
FOR $i = 1 TO 100000
    $len = LEN($s)
NEXT
PRINT $len
";
        var (ms, output) = Benchmark(code);

        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(10), "正确性：字符串长度应为 10");

        Console.WriteLine($"[F] 字符串 LEN: 10 万次 = {ms:F1}ms ({100_000.0 / ms:F0} calls/s)");
    }

    // ============================================================
    // G. 综合场景：素数筛 — 多优化点协同
    // ============================================================

    [Test]
    public void Benchmark_PrimeSieve()
    {
        // 综合测试：FOR 循环 + WHILE 循环 + 全局变量 + IF 条件
        var code = @"
$count = 0
FOR $n = 2 TO 10000
    $is_prime = 1
    $d = 2
    WHILE $d * $d <= $n
        IF $n % $d == 0
            $is_prime = 0
        ENDIF
        $d = $d + 1
    END
    $count = $count + $is_prime
NEXT
PRINT $count
";
        var (ms, output) = Benchmark(code);

        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(1229), "正确性：10000 以内素数共 1229 个");

        Console.WriteLine($"[G] 素数筛(2-10000): {ms:F1}ms, 找到 {result} 个素数");
    }
}