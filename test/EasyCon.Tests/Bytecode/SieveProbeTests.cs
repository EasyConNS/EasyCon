using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using EasyScript;
using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public class SieveProbeTests
{
    [Test]
    public void Probe_Sieve4_Eval()
    {
        string nl = "\n";
        string source =
            "$sum = 0" + nl +
            "FOR $n = 2 TO 4" + nl +
            "    $isPrime = 1" + nl +
            "    FOR $d = 2 TO $n" + nl +
            "        IF $n % $d == 0 and $n != $d" + nl +
            "            $isPrime = 0" + nl +
            "        ENDIF" + nl +
            "    NEXT" + nl +
            "    IF $isPrime == 1" + nl +
            "        $sum = $sum + $n" + nl +
            "    ENDIF" + nl +
            "NEXT" + nl +
            "PRINT $sum" + nl;
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Image, Is.Not.Null);
        var io = new ProbeIo();
        EcxVm.Run(result.Image!, io, null, null, null, () => 0, null, null, null,
            new CancellationTokenSource().Token, [], result.NativeSymbols);
        TestContext.Out.WriteLine("PRINT=" + string.Join(" | ", io.Lines));
    }
}

sealed class ProbeIo : IIoAdapter
{
    public List<string> Lines = new();
    StringBuilder buf = new();
    public void Print(string message, bool newline = true) { buf.Append(message); if (newline) { Lines.Add(buf.ToString()); buf.Clear(); } }
    public void Alert(string m) { }
    public string ReadLine() => "";
    public bool TryReadLine(out string l) { l = ""; return false; }
}