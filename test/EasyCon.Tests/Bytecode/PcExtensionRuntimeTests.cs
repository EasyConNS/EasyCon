using EasyCon.Core.Hosting;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Symbols;
using System.Text;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public sealed class PcExtensionRuntimeTests
{
    static string WideFunction(int count)
    {
        StringBuilder source = new("FUNC wide($offset:INT):INT\n");
        for (int i = 0; i < count; i++)
            source.AppendLine($"$v{i} = RAND(1000) + $offset + {i}");
        source.AppendLine("$sum = 0");
        for (int i = 0; i < count; i++)
            source.AppendLine($"$sum += $v{i}");
        source.AppendLine("RETURN $sum\nENDFUNC\nRETURN wide(7) + wide(11)");
        return source.ToString();
    }

    static CompileResult CompilePc(string source, bool optimize = true)
    {
        CompileResult result = Compilation.CompileSource(source, ScriptCompileProfiles.Interactive(optimize: optimize));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty, string.Join("\n", result.Diagnostics));
        Assert.That(result.Image!.Target, Is.EqualTo(ScriptTarget.Desktop));
        return result;
    }

    [TestCase(256, true)]
    [TestCase(270, true)]
    [TestCase(1024, true)]
    [TestCase(270, false)]
    public void WideFunction_CompilesAndExecutesTwice_WithoutTruncatingSlots(int count, bool optimize)
    {
        CompileResult compiled = CompilePc(WideFunction(count), optimize);
        Assert.That(compiled.Image!.Functions.Single(f => f.Name == "wide").NSlots, Is.GreaterThan(255));
        EcxHost host = new() { Rand = _ => 3 };
        int status = PcInterpreter.Run(compiled.Image, host, out Value result);
        Assert.That(status, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(result.AsInt(), Is.EqualTo(count * (count - 1) + count * 24));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PcPlan_CannotBeWrittenAsEcx_EvenWhenSmall(bool stripDebug)
    {
        CompileResult small = CompilePc("RETURN 42\n");
        CompileResult wide = CompilePc(WideFunction(270));
        Assert.Throws<BytecodeException>(() => EcsContainer.WriteImage(small.Image!, stripDebug));
        Assert.Throws<BytecodeException>(() => EcsContainer.WriteImage(wide.Image!, stripDebug));
        foreach (ModuleArtifact artifact in wide.Artifacts.Where(a => a.RequiresPcExecution))
        {
            Assert.Throws<BytecodeException>(() => EcsContainer.WriteModule(artifact));
        }
        Assert.Throws<BytecodeException>(() => EcxPipeline.Link(wide.Artifacts, false, false));
    }

    [Test]
    public void McuExport_StillRejectsWideFrames_AndSmallContainerIsUnchanged()
    {
        EcxExportResult wide = EcxExportService.Export(WideFunction(270), null, []);
        Assert.That(wide.Success, Is.False);
        Assert.That(wide.Bytes, Is.Null);
        Assert.That(wide.Diagnostics.Any(d => d.Message.Contains("寄存器溢出")), Is.True);
        EcxExportResult small = EcxExportService.Export("RETURN 42\n", null, []);
        Assert.That(small.Success, Is.True);
        EcxImage roundTrip = EcsContainer.ReadImage(small.Bytes!);
        Assert.That(roundTrip.Target, Is.EqualTo(ScriptTarget.Portable));
        Assert.That(EcxInterpreter.Run(roundTrip, new EcxHost(), out Value result), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(result.AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void PcPlan_DoesNotCreateDiskCache_EvenIfCallerRequestsIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"EasyConPcCache_{Guid.NewGuid():N}");
        CompileOptions options = ScriptCompileProfiles.Interactive();
        options.UseDiskCache = true;
        options.ObjDir = directory;
        for (int i = 0; i < 2; i++)
        {
            CompileResult result = Compilation.CompileSource(WideFunction(270), options);
            Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
            Assert.That(result.Image!.MaxSlots, Is.GreaterThan(255));
        }
        Assert.That(Directory.Exists(directory), Is.False);
    }

    [Test]
    public void PcExecution_PreservesCancellationAndErrorSourceLocation()
    {
        CompileResult loop = CompilePc("FOR $i = 1 TO 2000000000\n$x = RAND(2)\nNEXT\n");
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));
        Assert.That(PcInterpreter.Run(loop.Image!, new EcxHost(), out _, cancellation.Token), Is.EqualTo(EcxInterpreter.CANCELLED));
        CompileResult fault = CompilePc("$zero = RAND(1)\nRETURN 8 / $zero\n");
        int code = PcInterpreter.Run(fault.Image!, new EcxHost(), out _, out int errorFunc, out int errorPc, out _);
        Assert.That(code, Is.EqualTo(EcxInterpreter.ERR_DIVZERO));
        Assert.That(fault.Image!.Functions[errorFunc].LineAt(errorPc), Is.EqualTo(2));
    }

    [Test]
    public void PcEntry_RejectsPortableImages()
    {
        CompileResult result = Compilation.CompileSource("RETURN 1\n");
        Assert.That(PcInterpreter.Run(result.Image!, new EcxHost(), out _), Is.EqualTo(EcxInterpreter.ERR_IMAGE));
    }

    [Test]
    public void CoreRuntime_ExecutesWidePlanWithTheAssembledHost()
    {
        CompileResult compiled = CompilePc(WideFunction(270).Replace("RAND(1000)", "RAND(1)"));
        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            EnableOcr = false,
            EnableInference = false,
        });
        Value result = EcxVm.Run(compiled.Image!, lease.Capabilities, CancellationToken.None, nativeSymbols: compiled.NativeSymbols);
        Assert.That(result.AsInt(), Is.EqualTo(270 * 269 + 270 * 18));
    }

    [Test]
    public void WideLibrary_LinksAndInitializesOnce_DespiteCachedMcuFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"EasyConPcModules_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "lib"));
        try
        {
            string library = WideFunction(270).Replace("RETURN wide(7) + wide(11)", "");
            library += "\n$count = 0\n$count += 1\nFUNC initCount():INT\nRETURN $count\nENDFUNC\n";
            File.WriteAllText(Path.Combine(directory, "lib", "wide.ecs"), library);
            string path = Path.Combine(directory, "main.ecs");
            File.WriteAllText(path, "RETURN wide(7) + wide(11) + initCount()\n");
            CompileResult portable = Compilation.CompileFile(path, ScriptCompileProfiles.Distributable());
            Assert.That(portable.Diagnostics.Any(d => d.Message.Contains("寄存器溢出")), Is.True, string.Join("\n", portable.Diagnostics));
            string[] diskFiles = Directory.GetFiles(Path.Combine(directory, "obj"));
            Assert.That(diskFiles.Any(p => p.EndsWith(".err", StringComparison.Ordinal)), Is.True);
            for (int i = 0; i < 2; i++)
            {
                CompileResult pc = Compilation.CompileFile(path, ScriptCompileProfiles.Interactive());
                Assert.That(pc.Diagnostics.Where(d => d.IsError), Is.Empty, string.Join("\n", pc.Diagnostics));
                Assert.That(PcInterpreter.Run(pc.Image!, new EcxHost { Rand = _ => 3 }, out Value result), Is.EqualTo(EcxInterpreter.OK));
                Assert.That(result.AsInt(), Is.EqualTo(270 * 269 + 270 * 24 + 1));
            }
            Assert.That(Directory.GetFiles(Path.Combine(directory, "obj")), Is.EquivalentTo(diskFiles));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}