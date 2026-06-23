using EasyCon.Core.Runner;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Diagnostics;

namespace EasyCon.Tests;

[TestFixture]
public class PerformanceBenchmarks
{
    private const int BenchmarkIterations = 5;

    private static (double Ms, string[] Output) BenchmarkInterp(string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(code)).Compile(null);
        if (compilation.Program == null)
            Assert.Fail("脚本编译错误");

        var output = new MockOutputAdapter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var evaluator = new SsaEvaluator(compilation.Program, cts.Token) { IoAdapter = output };

        var times = new List<double>();
        string[]? lastOutput = null;
        for (int i = 0; i < BenchmarkIterations; i++)
        {
            output.Printed.Clear();
            var sw = Stopwatch.StartNew();
            evaluator.Evaluate();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            lastOutput = output.Printed.ToArray();
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

    [Test] public void Benchmark_LoopThroughput()
    {
        var code = @"
$count = 0
FOR $i = 1 TO 1000000
    $count = $count + 1
NEXT
PRINT $count
";
        var (interpMs, _) = BenchmarkInterp(code);
        Console.WriteLine($"[A] 循环吞吐量 100万次: 解释器={interpMs:F1}ms ({1_000_000.0 / interpMs / 1000:F1}K iter/s)");
    }

    [Test] public void Benchmark_FunctionCallOverhead()
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
        var (interpMs, _) = BenchmarkInterp(code);
        Console.WriteLine($"[B] 函数调用 10万次: 解释器={interpMs:F1}ms ({100_000.0 / interpMs:F0} calls/s)");
    }

    [Test] public void Benchmark_MultiArgFunctionCall()
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
        var (interpMs, _) = BenchmarkInterp(code);
        Console.WriteLine($"[C] 4参数调用 10万次: 解释器={interpMs:F1}ms ({100_000.0 / interpMs:F0} calls/s)");
    }

    [Test] public void Benchmark_TailRecursion_Reuse()
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
        var (interpMs, _) = BenchmarkInterp(code);
        Console.WriteLine($"[D] 尾递归 50000层: 解释器={interpMs:F1}ms");
    }

    [Test] public void Benchmark_GlobalVariableReadWrite()
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
        var (interpMs, _) = BenchmarkInterp(code);
        // 5 个变量 × 20 万次 × 2 (读+写)
        var totalOps = 5 * 200000 * 2;
        Console.WriteLine($"[E] 全局变量 5var×20万: 解释器={interpMs:F1}ms ({totalOps * 1.0 / interpMs / 1000:F1}M ops/s)");
    }

    [Test] public void Benchmark_StringLength()
    {
        var code = @"
$s = ""abcdefghij""
$len = 0
FOR $i = 1 TO 100000
    $len = LEN($s)
NEXT
PRINT $len
";
        var (interpMs, _) = BenchmarkInterp(code);
        Console.WriteLine($"[F] 字符串 LEN 10万次: 解释器={interpMs:F1}ms ({100_000.0 / interpMs:F0} calls/s)");
    }

    [Test] public void Benchmark_PrimeSieve()
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
        var (interpMs, output) = BenchmarkInterp(code);
        var result = ParseIntOutput(output);
        Console.WriteLine($"[G] 素数筛 2-10000: 解释器={interpMs:F1}ms, 找到 {result} 个素数");
    }
}
