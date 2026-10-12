using EasyCon.Core.Hosting;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public class ArgumentWindowTests
{
    internal static string Parameters(int count)
        => string.Join(", ", Enumerable.Range(0, count).Select(i => $"$p{i}:INT"));

    internal static string Arguments(int count)
        => string.Join(", ", Enumerable.Range(1, count));

    internal static string WeightedFunction(int count)
        => $"FUNC weighted({Parameters(count)}):INT\n" +
            "    RETURN " + string.Join(" + ", Enumerable.Range(0, count).Select(i => $"$p{i} * {i + 1}")) +
            "\nENDFUNC\n";

    internal static CompileResult Compile(string source, bool optimize = true)
    {
        CompileResult result = Compilation.CompileSource(source, ScriptCompileProfiles.Interactive(optimize: optimize));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty, string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    internal static void AssertResult(EcxImage image, int expected, EcxHost? host = null)
    {
        Assert.That(EcxInterpreter.Run(image, host ?? new EcxHost(), out Value result), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(result.AsInt(), Is.EqualTo(expected));
    }

    [TestCase(8, true)]
    [TestCase(8, false)]
    [TestCase(16, true)]
    [TestCase(16, false)]
    [TestCase(64, true)]
    [TestCase(64, false)]
    public void UserCall_PreservesEveryArgument_AndRoundTrips(int count, bool optimize)
    {
        CompileResult result = Compile(WeightedFunction(count) + $"RETURN weighted({Arguments(count)})\n", optimize);
        if (!optimize)
            Assert.That(result.Image!.Functions.SelectMany(f => f.Instructions)
                .Any(i => i.Op == EcsOpcode.Call && i.B == count), Is.True, "未优化用例必须执行真实的多参数 Call");
        int expected = Enumerable.Range(1, count).Sum(i => i * i);
        AssertResult(result.Image!, expected);
        AssertResult(EcsContainer.ReadImage(EcsContainer.WriteImage(result.Image!, stripDebug: true)), expected);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NestedCalls_AndParameterPermutation_PreserveCallerValues(bool optimize)
    {
        string source = WeightedFunction(8) + """
            FUNC forward($a:INT, $b:INT, $c:INT, $d:INT, $e:INT, $f:INT, $g:INT, $h:INT):INT
                $first = weighted($h, $g, $f, $e, $d, $c, $b, $a)
                $second = weighted($a, $b, $c, $d, $e, $f, $g, $h)
                RETURN $first + $second + $a + $h
            ENDFUNC
            $x = forward(1, 2, 3, 4, 5, 6, 7, 8)
            RETURN forward(weighted(1, 2, 3, 4, 5, 6, 7, 8), 2, 3, 4, 5, 6, 7, 8) + $x
            """;
        AssertResult(Compile(source, optimize).Image!, 2696);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NonTailRecursion_WithEightParameters_PreservesFrames(bool optimize)
    {
        const string source = """
            FUNC recurse($n:INT, $a:INT, $b:INT, $c:INT, $d:INT, $e:INT, $f:INT, $g:INT):INT
                IF $n == 0
                    RETURN $a + $b + $c + $d + $e + $f + $g
                ENDIF
                $r = recurse($n - 1, $g, $f, $e, $d, $c, $b, $a)
                RETURN $r + $a
            ENDFUNC
            RETURN recurse(2, 1, 2, 3, 4, 5, 6, 7)
            """;
        AssertResult(Compile(source, optimize).Image!, 36);
    }

    [Test]
    public void TailRecursion_WithEightParameters_PreservesParameterStores()
    {
        const string source = """
            FUNC accumulate($n:INT, $acc:INT, $a:INT, $b:INT, $c:INT, $d:INT, $e:INT, $f:INT):INT
                IF $n == 0
                    RETURN $acc
                ENDIF
                RETURN accumulate($n - 1, $acc + $a + $b + $c + $d + $e + $f, $f, $e, $d, $c, $b, $a)
            ENDFUNC
            RETURN accumulate(10000, 0, 1, 2, 3, 4, 5, 6)
            """;
        AssertResult(Compile(source).Image!, 210000);
    }

    [TestCase(0)]
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(64)]
    public void ExternCall_PassesEveryArgumentToNativeHost(int count)
    {
        string source = $"EXTERN FUNC probe({Parameters(count)}):INT FROM \"argument-probe\"\n" +
            $"RETURN probe({Arguments(count)})\n";
        CompileResult result = Compile(source);
        int calls = 0;
        EcxHost host = new()
        {
            Native = (_, args, _) =>
            {
                calls++;
                Assert.That(args.Select(a => a.I32), Is.EqualTo(Enumerable.Range(1, count)));
                return TaggedValue.FromInt(args.Select((a, i) => a.I32 * (i + 1)).Sum());
            },
        };
        AssertResult(EcsContainer.ReadImage(EcsContainer.WriteImage(result.Image!, stripDebug: true)),
            Enumerable.Range(1, count).Sum(i => i * i), host);
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ArrayAndStringArguments_WithEightParameters_KeepValueSemantics(bool optimize)
    {
        const string source = """
            FUNC mutate($items:INT[], $tag:STRING, $a:INT, $b:INT, $c:INT, $d:INT, $e:INT, $f:INT):INT
                $copy = [11, 12, 13, 14, 15, 16, 17, 18, 19]
                $items[0] = $items[0] + $a + $b + $c + $d + $e + $f
                $v = $items[0] + LEN($tag) + $copy[8]
                PRINT $tag
                RETURN $v
            ENDFUNC
            $original = [10, 20]
            $r = mutate($original, "abcdefgh", 1, 2, 3, 4, 5, 6)
            RETURN $r * 100 + $original[0]
            """;
        CompileResult result = Compile(source, optimize);
        EcxHost host = new();
        host.EnableRecording();
        AssertResult(EcsContainer.ReadImage(EcsContainer.WriteImage(result.Image!)), 5810, host);
        Assert.That(host.Lines, Is.EqualTo(new[] { "abcdefgh" }));
    }

    [Test]
    public void ImportedFunction_WithSixteenParameters_SurvivesDiskCache()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"EasyConArguments_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "many.ecs"), WeightedFunction(16));
            string main = Path.Combine(directory, "main.ecs");
            File.WriteAllText(main, $"IMPORT \"many.ecs\"\nRETURN weighted({Arguments(16)})\n");
            CompileOptions options = ScriptCompileProfiles.Distributable();
            options.ObjDir = Path.Combine(directory, "obj");
            for (int i = 0; i < 2; i++)
            {
                CompileResult result = Compilation.CompileFile(main, options);
                Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty, string.Join("\n", result.Diagnostics));
                AssertResult(EcsContainer.ReadImage(EcsContainer.WriteImage(result.Image!, stripDebug: true)), 1496);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ParameterCount_OverBytecodeCapacity_ReportsDiagnostic()
    {
        string source = $"FUNC tooMany({Parameters(255)}):INT\nRETURN 0\nENDFUNC\nRETURN 1\n";
        CompileResult result = Compilation.CompileSource(source, ScriptCompileProfiles.Interactive());
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ParamWindowOverflow), Is.True);
        Assert.That(result.Image, Is.Null);
    }

    [Test]
    public void WrongArity_WithEightParameterDeclaration_ReportsArgumentMismatch()
    {
        CompileResult result = Compilation.CompileSource(WeightedFunction(8) + "RETURN weighted(1, 2, 3, 4, 5, 6, 7)\n",
            ScriptCompileProfiles.Interactive());
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.FunctionArgumentCountMismatch), Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ParamWindowOverflow), Is.False);
    }

    [Test]
    public void CombinedParameterAndOutgoingWindows_OverFrameBudget_ReportsOverflow()
    {
        string forwarded = string.Join(", ", Enumerable.Range(0, 128).Select(i => $"$p{i}"));
        string source = $"FUNC relay({Parameters(128)}):INT\nRETURN relay({forwarded})\nENDFUNC\n" +
            $"RETURN relay({Arguments(128)})\n";
        CompileResult result = Compilation.CompileSource(source, ScriptCompileProfiles.Interactive(optimize: false));
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("寄存器溢出", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ParamWindowOverflow), Is.False);
        Assert.That(result.Image, Is.Null);
    }

    [Test]
    public void RepeatedStdWrappers_ReuseAnIsolatedInlineWindow()
    {
        string source = string.Join("\n", Enumerable.Repeat("PRINT \"same\"", 24)) + "\nRETURN 42\n";
        CompileResult result = Compile(source);
        Assert.That(result.Image!.MaxSlots, Is.LessThan(64), "内联壳体的临时窗口应在调用点之间复用");
        EcxHost host = new();
        host.EnableRecording();
        AssertResult(result.Image, 42, host);
        Assert.That(host.Lines, Is.EqualTo(Enumerable.Repeat("same", 24)));
    }

    [Test]
    public void TinyLeaf_DoesNotReserveUnusedArgumentOrStagingWindows()
    {
        CompileResult result = Compile("FUNC leaf($x:INT):INT\nRETURN $x + 1\nENDFUNC\nRETURN leaf(41)\n", optimize: false);
        EcsFunction leaf = result.Image!.Functions.Single(f => f.Name == "leaf");
        Assert.That(leaf.NSlots, Is.LessThan(8), "没有出站调用或数组时不应保留 STAGING 或 7 参数窗");
        AssertResult(result.Image!, 42);
    }
}