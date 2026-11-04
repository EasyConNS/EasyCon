using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Tests.Support;

namespace EasyCon.Tests;

[TestFixture]
public sealed class SsaOptimizationParityTests
{
    private const string LookupScript = @"
FUNC target_key_value($a:INT, $b:INT, $c:INT):INT
    $prefix = $a * 400 + $b
    $high = $prefix * 256
    $low = $c * 256
    RETURN $high + $low
ENDFUNC

FUNC find_matches($a:INT, $b:INT, $target:INT):INT
    $exact = target_key_value($a, $b, $target)
    $wildcard = target_key_value($a, $b, 0)
    $matches = 0
    FOR $i = 0 TO 255
        $candidate = target_key_value($a, $b, $i)
        IF $candidate == $exact OR $candidate == $wildcard
            $matches += $i + 1
        ENDIF
    NEXT
    RETURN $matches + target_key_value($a, $b, $target) + target_key_value($b, $a, 0)
ENDFUNC

RETURN find_matches(INT(ARG(0)), INT(ARG(1)), INT(ARG(2)))
";

    [TestCase(0, 0, 0)]
    [TestCase(999, 255, 255)]
    [TestCase(1, 255, 0)]
    public void OptimizedAndUnoptimizedLookupHaveSameResult(int a, int b, int target)
    {
        int optimized = Run(LookupScript, a, b, target, optimize: true);
        int unoptimized = Run(LookupScript, a, b, target, optimize: false);

        Assert.That(optimized, Is.EqualTo(unoptimized));
    }

    private static int Run(string source, int a, int b, int target, bool optimize)
    {
        var result = Compilation.CompileSource(source, new CompileOptions
        {
            Optimize = optimize,
            UseDiskCache = false,
        });

        Assert.That(result.Diagnostics.HasErrors(), Is.False,
            $"Optimize={optimize}: {string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))}");
        Assert.That(result.Image, Is.Not.Null, $"Optimize={optimize} produced no image");

        var output = new MockOutputAdapter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var value = EcxVm.Run(result.Image!, EcsTestHost.Capabilities(output), cts.Token,
            [$"{a}", $"{b}", $"{target}"], result.NativeSymbols);
        return value.AsInt();
    }
}