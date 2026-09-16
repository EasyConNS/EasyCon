using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using System.Text;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public class FrameSlotCompactionTests
{
    [Test]
    public void MoreThan255DeadLocalHomes_CompileAndExecute()
    {
        var source = new StringBuilder();
        source.AppendLine("FUNC select($pick:INT):INT");
        for (int i = 0; i < 300; i++)
            source.AppendLine($"    $v{i} = {i}");
        source.AppendLine("    IF $pick == 0");
        source.AppendLine("        RETURN $v0");
        source.AppendLine("    ENDIF");
        source.AppendLine("    RETURN $v299");
        source.AppendLine("ENDFUNC");
        source.AppendLine("$a = select(0)");
        source.AppendLine("PRINT $a");
        source.AppendLine("$b = select(1)");
        source.AppendLine("PRINT $b");

        var result = Compilation.CompileSource(source.ToString(), new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var function = result.Image!.Functions.Single(f => f.Name == "select");
        Assert.That(function.NParams, Is.EqualTo(1));
        Assert.That(function.NSlots, Is.LessThan(255));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "0", "299" }));
    }

    [Test]
    public void BranchLoopAndNestedMultiplication_PreserveValues()
    {
        const string source = """
            FUNC key($version:INT, $dex:INT, $method:INT, $location:INT):INT
                RETURN (($version * 400 + $dex) * 256 + $method) * 256 + $location
            ENDFUNC
            FUNC sumOdd($limit:INT):INT
                $sum = 0
                FOR $i = 0 TO $limit
                    IF $i % 2 == 1
                        $sum = $sum + $i
                    ENDIF
                NEXT
                RETURN $sum
            ENDFUNC
            $key = key(1, 147, 2, 7)
            PRINT $key
            $sum = sumOdd(9)
            PRINT $sum
            """;

        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "35848711", "25" }));
    }

    [Test]
    public void MoreThan255ConstantsInOneBlock_UseBoundedSlots()
    {
        var source = new StringBuilder();
        for (int i = 0; i < 300; i++)
            source.AppendLine($"$g{i} = {i}");
        source.AppendLine("FUNC total():INT");
        source.AppendLine("    $sum = 0");
        for (int i = 0; i < 300; i++)
            source.AppendLine($"    $sum = $sum + $g{i}");
        source.AppendLine("    RETURN $sum");
        source.AppendLine("ENDFUNC");
        source.AppendLine("$result = total()");
        source.AppendLine("PRINT $result");

        var result = Compilation.CompileSource(source.ToString(), new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var function = result.Image!.Functions.Single(f => f.Name == "total");
        Assert.That(function.NSlots, Is.LessThan(255));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "44850" }));
    }

    [Test]
    public void MoreThan255ArrayElements_AreChunkedInOrder()
    {
        string tail = string.Join(",", Enumerable.Range(1, 299));
        string source = $"""
            FUNC barrier($value:INT):INT
                RETURN $value
            ENDFUNC
            $head = barrier(0)
            $items = [$head,{tail}]
            $items2 = [$head,{tail}]
            $first = $items[0]
            PRINT $first
            $middle = $items[127]
            PRINT $middle
            $last = $items[299]
            PRINT $last
            $length = LEN($items)
            PRINT $length
            $secondLast = $items2[299]
            PRINT $secondLast
            """;

        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        Assert.That(result.Image!.MaxSlots, Is.LessThan(255));
        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "0", "127", "299", "300", "299" }));
    }

    [Test]
    public void CrossBlockCse_RewritesUsesBeyondImmediateSuccessor()
    {
        const string source = """
            FUNC score($a:INT, $b:INT, $flag:INT):INT
                $base = $a * $b
                IF $flag > 0
                    $same = $a * $b
                    IF $flag > 1
                        $marker = 1
                    ENDIF
                    RETURN $same + 1
                ENDIF
                RETURN $base
            ENDFUNC
            $zero = score(6, 7, 0)
            PRINT $zero
            $one = score(6, 7, 1)
            PRINT $one
            $two = score(6, 7, 2)
            PRINT $two
            """;

        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "42", "43", "43" }));
    }

    [Test]
    public void AlgebraicReplacement_PreservesLiveOperandUseCount()
    {
        const string source = """
            FUNC incrementProduct($a:INT, $b:INT):INT
                $product = $a * $b
                $same = $product + 0
                RETURN $same + 1
            ENDFUNC
            $result = incrementProduct(6, 7)
            PRINT $result
            """;

        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "43" }));
    }

    [Test]
    public void DeadPhiArm_IsReleasedExactlyOnce()
    {
        var symbol = new FunctionSymbol("deadPhi", [], ScriptType.Void);
        var function = new SsaFunction(symbol);
        var entry = new SsaBlock(0);
        var exit = new SsaBlock(1) { IsReturn = true };
        function.Blocks.Add(entry);
        function.Blocks.Add(exit);

        var constant = new SsaValue(0, SsaOp.ConstInt, ScriptType.Int) { Block = entry };
        constant.Const.SetInt(7);
        entry.Instructions.Add(constant);

        var deadPhi = new SsaValue(1, SsaOp.Phi, ScriptType.Int)
        {
            Block = exit,
            ExtraArgs = [constant],
        };
        exit.Phis.Add(deadPhi);
        entry.JumpTarget = exit;
        exit.Predecessors.Add(entry);

        var context = new ModuleEncodeContext
        {
            Program = null!,
            ModuleName = "test",
            LocalFuncIds = new() { [symbol] = 0 },
            Imports = new(),
            ImportIds = new(),
            Globals = new(),
            GlobalSlots = new(),
            Natives = new(),
            NativeIds = new(),
            StructIds = new(),
            Pool = new(),
        };

        Assert.DoesNotThrow(() => BytecodeEncoder.Encode(function, symbol, 0, context));
    }

    [Test]
    public void ComplexMain_KeepsSingleFunctionGlobalsOutOfFrameSlots()
    {
        var source = new StringBuilder();
        source.AppendLine("FUNC barrier($value:INT):INT");
        source.AppendLine("    IF $value == 0");
        source.AppendLine("        RETURN 0");
        source.AppendLine("    ENDIF");
        source.AppendLine("    RETURN $value");
        source.AppendLine("ENDFUNC");
        source.AppendLine("$guard = barrier(0)");
        for (int i = 0; i < 55; i++)
            source.AppendLine($"$g{i} = {i}");
        for (int i = 0; i < 55; i++)
        {
            source.AppendLine($"IF $guard == {i}");
            source.AppendLine($"    $g{i} = $g{i} + 1");
            source.AppendLine("ENDIF");
        }
        source.AppendLine("$sum = 0");
        for (int i = 0; i < 55; i++)
            source.AppendLine($"$sum = $sum + $g{i}");
        source.AppendLine("PRINT $sum");

        var result = Compilation.CompileSource(source.ToString(), new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        Assert.That(result.Image!.Globals.Count, Is.GreaterThanOrEqualTo(55));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "1486" }));
    }

    [Test]
    public void ImmediateAndRegisterUseOfSameConstant_AreCountedSeparately()
    {
        const string source = """
            FUNC check($value:INT):INT
                WAIT 50
                IF $value == 50
                    RETURN 1
                ENDIF
                RETURN 0
            ENDFUNC
            $result = check(50)
            PRINT $result
            """;

        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        var host = new EcxHost();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "1" }));
    }
}