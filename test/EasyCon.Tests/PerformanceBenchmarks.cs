using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Diagnostics;

namespace EasyCon.Tests;

/// <summary>
/// 性能基准测试：JIT 模式下的性能指标。
/// 所有测试均通过 SsaEvaluator.UseJit=true 使用 JIT 编译执行。
/// </summary>
[TestFixture]
public class PerformanceBenchmarks
{
    private const int BenchmarkIterations = 5;

    /// <summary>
    /// 执行编译后的脚本（JIT 模式），返回执行耗时(ms)和 PRINT 输出
    /// </summary>
    private static (double Ms, string[] Output) RunScript(SsaEvaluator evaluator, MockOutputAdapter output)
    {
        output.Printed.Clear();
        var sw = Stopwatch.StartNew();
        evaluator.Evaluate();
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, output.Printed.ToArray());
    }

    private static (double MedianMs, string[] Output) Benchmark(string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(code)).Compile(null);
        if (compilation.Program == null)
        {
            var errors = compilation.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
            Assert.Fail($"脚本编译错误: {string.Join("; ", errors)}");
        }

        var output = new MockOutputAdapter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var evaluator = new SsaEvaluator(compilation.Program, cts.Token) { IoAdapter = output, UseJit = true };

        var times = new List<double>();
        string[]? lastOutput = null;

        for (int i = 0; i < BenchmarkIterations; i++)
        {
            var (ms, output_) = RunScript(evaluator, output);
            times.Add(ms);
            lastOutput = output_;
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
    // A. 循环吞吐量
    // ============================================================

    [Test]
    public void Benchmark_LoopThroughput()
    {
        var code = @"
$count = 0
FOR $i = 1 TO 1000000
    $count = $count + 1
NEXT
PRINT $count
";
        var (ms, output) = Benchmark(code);
        var result = ParseIntOutput(output);
        Assert.That(result, Is.EqualTo(1000000));
        Console.WriteLine($"[A] 循环吞吐量: 100 万次 = {ms:F1}ms ({1_000_000.0 / ms / 1000:F1}K iter/s)");
    }

    // ============================================================
    // B. 函数调用开销
    // ============================================================

    [Test]
    public void Benchmark_FunctionCallOverhead()
    {
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
        Assert.That(result, Is.EqualTo(100000));
        Console.WriteLine($"[B] 函数调用: 10 万次 = {ms:F1}ms ({100_000.0 / ms:F0} calls/s)");
    }

    // ============================================================
    // C. 多参数函数调用
    // ============================================================

    [Test]
    public void Benchmark_MultiArgFunctionCall()
    {
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
        Assert.That(result, Is.EqualTo(600000));
        Console.WriteLine($"[C] 4 参数调用: 10 万次 = {ms:F1}ms ({100_000.0 / ms:F0} calls/s)");
    }

    // ============================================================
    // D. 尾递归
    // ============================================================

    [Test]
    public void Benchmark_TailRecursion_Reuse()
    {
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
        Assert.That(result, Is.EqualTo(1250025000));
        Console.WriteLine($"[D] 尾递归 50000 层: {ms:F1}ms");
    }

    // ============================================================
    // E. 全局变量读写
    // ============================================================

    [Test]
    public void Benchmark_GlobalVariableReadWrite()
    {
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
        var totalOps = 5 * 200000 * 2;
        Console.WriteLine($"[E] 全局变量: 5 var × 20 万次 = {ms:F1}ms ({totalOps * 1.0 / ms / 1000:F1}M ops/s)");
    }

    // ============================================================
    // F. 字符串 LEN
    // ============================================================

    [Test]
    public void Benchmark_StringLength()
    {
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
        Assert.That(result, Is.EqualTo(10));
        Console.WriteLine($"[F] 字符串 LEN: 10 万次 = {ms:F1}ms ({100_000.0 / ms:F0} calls/s)");
    }

    // ============================================================
    // G. 综合场景：素数筛
    // ============================================================

    [Test]
    public void Benchmark_PrimeSieve()
    {
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
        Assert.That(result, Is.EqualTo(1229));
        Console.WriteLine($"[G] 素数筛(2-10000): {ms:F1}ms, 找到 {result} 个素数");
    }
}