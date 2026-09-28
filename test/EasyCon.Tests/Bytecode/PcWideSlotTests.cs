using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>PC 宽槽位与冻结 ECX/MCU ABI 的边界验证。</summary>
[TestFixture]
public class PcWideSlotTests
{
    static string BuildWideSource()
    {
        const int valueCount = 270;
        var source = new StringBuilder();
        source.AppendLine("_wideGlobal = 100000");
        source.AppendLine("FUNC bump($x:int) : int");
        source.AppendLine("    RETURN $x + 1");
        source.AppendLine("ENDFUNC");
        for (int i = 0; i < valueCount; i++)
            source.AppendLine($"$v{i} = RAND(1)");
        source.AppendLine("$sum = 0");
        for (int i = 0; i < valueCount; i++)
            source.AppendLine($"$sum += $v{i}");
        source.AppendLine("$sum += bump(_wideGlobal)");
        source.AppendLine("PRINT $sum");
        source.AppendLine("RETURN $sum");
        return source.ToString();
    }

    [Test]
    public void WideScript_DefaultEcxModeRejects_ButPcModeRunsHighSlots()
    {
        string source = BuildWideSource();
        CompileResult narrow = Compilation.CompileSource(source, new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = false,
        });
        Assert.That(narrow.Diagnostics.Any(d => d.IsError && d.Message.Contains("255", StringComparison.Ordinal)),
            Is.True, "ECX/MCU 编译模式必须继续拒绝超过 255 的帧槽位");

        CompileResult wide = Compilation.CompileSource(source, new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = false,
            EnablePcWideSlots = true,
        });
        Assert.That(wide.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", wide.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        Assert.That(wide.Image, Is.Not.Null);
        Assert.That(wide.Image!.MaxSlots, Is.GreaterThan(255));

        EcsFunction wideFunction = wide.Image.Functions.Single(f => f.PcCode != null);
        Assert.That(wideFunction.PcCode!.Any(i => i.A > 255 || i.B > 255 || i.C > 255), Is.True,
            "PC 指令流必须保留高槽位编号，不能发生 8 位截断");

        var host = new EcxHost();
        host.EnableRecording();
        int exitCode = EcxInterpreter.Run(wide.Image, host, out Value result);
        Assert.That(exitCode, Is.EqualTo(EcxInterpreter.OK));
        Assert.That(result.AsInt(), Is.EqualTo(100001));
        Assert.That(host.Lines, Is.EqualTo(new[] { "100001" }));

        Assert.That(() => EcxWriter.Write(wide.Image), Throws.TypeOf<BytecodeException>(),
            "PC 宽镜像不得写成 MCU ECX");
        ModuleArtifact wideArtifact = wide.Artifacts.Single(a => a.Functions.Any(f => f.PcCode != null));
        Assert.That(() => EcmFormat.Write(wideArtifact), Throws.TypeOf<BytecodeException>(),
            "PC 宽模块不得进入旧 ECM 缓存格式");
    }

    [Test]
    public void PcWideMode_DoesNotUseLegacyModuleCaches()
    {
        ModuleProjectResult first = ProjectCompiler.CompileProject(SyntaxTree.Parse(BuildWideSource()), new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = true,
            EnablePcWideSlots = true,
        });
        ModuleProjectResult second = ProjectCompiler.CompileProject(SyntaxTree.Parse(BuildWideSource()), new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = true,
            EnablePcWideSlots = true,
        });

        Assert.That(first.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(second.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(first.ProcessCacheHits, Is.Zero);
        Assert.That(first.ProcessCacheMisses, Is.Zero);
        Assert.That(second.ProcessCacheHits, Is.Zero);
        Assert.That(second.ProcessCacheMisses, Is.Zero);
        Assert.That(first.Image, Is.Not.Null);
        Assert.That(second.Image, Is.Not.Null);
        Assert.That(first.Image!.Functions.Any(f => f.PcCode != null), Is.True);
        Assert.That(second.Image!.Functions.Any(f => f.PcCode != null), Is.True);
    }

    [Test]
    public void PcMode_KeepsNarrowFunctionsOnFrozenInstructionStream()
    {
        CompileResult result = Compilation.CompileSource("RETURN 42", new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = false,
            EnablePcWideSlots = true,
        });

        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(result.Image, Is.Not.Null);
        Assert.That(result.Image!.Functions.All(f => f.PcCode == null), Is.True);
        Assert.That(EcxInterpreter.Run(result.Image, new EcxHost(), out Value value),
            Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(42));
        Assert.That(EcxWriter.Write(result.Image), Is.Not.Empty);
    }

    [Test]
    public void Linker_PrependsModuleInitializersToWideEntry()
    {
        var init = new EcsFunction
        {
            Name = "$eval",
            Module = "lib",
            NSlots = 1,
            Code = [(uint)EcsOpcode.Ret0],
        };
        var entry = new EcsFunction
        {
            Name = "$eval",
            Module = "main",
            NSlots = 301,
            HasReturn = true,
            Code =
            [
                (uint)EcsOpcode.LoadI | (uint)(300 & 0xFF) << 8 | 7u << 16,
                (uint)EcsOpcode.Ret | (uint)(300 & 0xFF) << 8,
            ],
            PcCode =
            [
                new EcsPcInstruction(EcsOpcode.LoadI, 300, 7),
                new EcsPcInstruction(EcsOpcode.Ret, 300),
            ],
            LineTable = [0, 1],
            PcLineTable = [0, 1],
        };

        ModuleArtifact Library() => new()
        {
            Name = "lib",
            Functions = [init],
            Pool = new ModulePool(),
            Imports = [],
            Exports = [],
            Globals = [],
            Natives = [],
            Structs = [],
            ILNames = [],
            HasInit = true,
            InitFid = 0,
        };
        ModuleArtifact Main() => new()
        {
            Name = "main",
            Functions = [entry],
            Pool = new ModulePool(),
            Imports = [],
            Exports = [new EcsExport { Name = "$eval", LocalFid = 0 }],
            Globals = [],
            Natives = [],
            Structs = [],
            ILNames = [],
            HasEval = true,
            HasInit = true,
            InitFid = 0,
        };

        EcxImage image = EcxPipeline.Link([Library(), Main()], keyAction: false, needIL: false);
        EcsFunction linkedEntry = image.Functions[image.Entry];
        Assert.That(linkedEntry.PcCode, Is.Not.Null);
        Assert.That(linkedEntry.PcCode![0].Op, Is.EqualTo(EcsOpcode.Call));
        Assert.That(linkedEntry.PcCode[0].C, Is.EqualTo(-1));
        Assert.That(linkedEntry.PcLineTable, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(EcxInterpreter.Run(image, new EcxHost(), out Value value), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(7));
    }

    [Test]
    public void WideCall_CanUseSlot255AsReturnDestination()
    {
        var caller = new EcsFunction
        {
            Name = "caller",
            Module = "main",
            NSlots = 256,
            HasReturn = true,
            Code =
            [
                (uint)EcsOpcode.Call | 255u << 24,
                1,
                (uint)EcsOpcode.Ret | 255u << 8,
            ],
            PcCode =
            [
                new EcsPcInstruction(EcsOpcode.Call, c: 255, ext: 1),
                new EcsPcInstruction(EcsOpcode.Ret, 255),
            ],
        };
        var callee = new EcsFunction
        {
            Name = "callee",
            Module = "main",
            NSlots = 1,
            HasReturn = true,
            Code =
            [
                (uint)EcsOpcode.LoadI | 9u << 16,
                (uint)EcsOpcode.Ret,
            ],
        };
        var image = new EcxImage
        {
            Functions = [caller, callee],
            Entry = 0,
            MaxSlots = 256,
        };

        Assert.That(EcxInterpreter.Run(image, new EcxHost(), out Value value), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(9));
        Assert.That(() => EcxWriter.Write(image), Throws.TypeOf<BytecodeException>());
    }
}