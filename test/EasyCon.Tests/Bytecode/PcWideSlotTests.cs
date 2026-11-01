using EasyCon.Core.Hosting;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 每函数寄存器帧契约：参数和出站窗口按需分配，NSlots ≤ MaxRegisters；
/// 寄存器预算超出即报溢出诊断（Lua 风格，不 spill）。
/// Call/CallN 的接收槽 C 恒为寄存器号（0 = RET 固定落点），无哨兵值。
/// 本套件锁：大帧编译运行 + 缓存/镜像往返 + RET 落点。
/// </summary>
[TestFixture]
public class RegisterFileContractTests
{
    internal static string BuildWideSource(int valueCount)
    {
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

    [TestCase(270, true)]
    [TestCase(270, false)]
    [TestCase(1024, true)]
    [TestCase(1024, false)]
    public void WideScript_CompilesRunsAndSurvivesImageAndModuleRoundTrips(int count, bool optimize)
    {
        CompileResult result = Compilation.CompileSource(BuildWideSource(count), ScriptCompileProfiles.Interactive(optimize: optimize));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty, string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        Assert.That(result.Image!.MaxSlots, Is.GreaterThan(255));
        ArgumentWindowTests.AssertResult(result.Image, 100001);
        foreach (bool stripDebug in new[] { false, true })
        {
            EcxImage restored = EcsContainer.ReadImage(EcsContainer.WriteImage(result.Image, stripDebug));
            ArgumentWindowTests.AssertResult(restored, 100001);
            for (int i = 0; i < restored.Functions.Count; i++)
            {
                Assert.That(restored.Functions[i].Instructions, Is.EqualTo(result.Image.Functions[i].Instructions));
                if (!stripDebug)
                    Assert.That(restored.Functions[i].LineTable, Is.EqualTo(result.Image.Functions[i].LineTable));
            }
        }
        List<ModuleArtifact> modules = result.Artifacts.Select(m => EcsContainer.ReadModule(EcsContainer.WriteModule(m))).ToList();
        for (int m = 0; m < modules.Count; m++)
            for (int f = 0; f < modules[m].Functions.Count; f++)
                Assert.That(modules[m].Functions[f].LineTable, Is.EqualTo(result.Artifacts[m].Functions[f].LineTable));
        ArgumentWindowTests.AssertResult(EcxPipeline.Link(modules, false, false), 100001);
    }

    [Test]
    public void ProcessCache_HitsOnRepeatedCompile()
    {
        // 桌面路径进程级产物缓存（同源码⊕同选项必命中）
        ModuleProjectResult first = ProjectCompiler.CompileProject(SyntaxTree.Parse(BuildWideSource(80)), new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = true,
        });
        ModuleProjectResult second = ProjectCompiler.CompileProject(SyntaxTree.Parse(BuildWideSource(80)), new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = true,
        });

        Assert.That(first.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(second.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(first.Image, Is.Not.Null);
        Assert.That(second.Image, Is.Not.Null);
        Assert.That(second.ProcessCacheHits, Is.GreaterThan(0),
            "同源码⊕同选项编译应命中进程级产物缓存");
    }

    [Test]
    public void NarrowFunctions_RemainOnSingleStream()
    {
        CompileResult result = Compilation.CompileSource("RETURN 42", new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = false,
        });

        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
        Assert.That(result.Image, Is.Not.Null);
        Assert.That(EcxInterpreter.Run(result.Image, new EcxHost(), out Value value),
            Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(42));
        Assert.That(EcsContainer.WriteImage(result.Image), Is.Not.Empty);
    }

    [Test]
    public void Linker_PrependsModuleInitializersToEntry()
    {
        var init = new EcsFunction
        {
            Name = "$eval",
            Module = "lib",
            NSlots = EcsRegisters.ArgBase,
            Instructions = [new EcsInstruction(EcsOpcode.Ret0, 0, 0, 0)],
        };
        var entry = new EcsFunction
        {
            Name = "$eval",
            Module = "main",
            NSlots = EcsRegisters.ArgBase + 1,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.LoadI, EcsRegisters.ArgBase, 7, 0),
                new EcsInstruction(EcsOpcode.Ret, EcsRegisters.ArgBase, 0, 0),
            ],
            LineTable = [0, 1],
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
        Assert.That(linkedEntry.Instructions[0].Op, Is.EqualTo(EcsOpcode.Call));
        Assert.That(linkedEntry.Instructions[0].C, Is.EqualTo(EcsRegisters.Ret), "init 调用接收槽 = RET 固定落点");
        Assert.That(linkedEntry.LineTable, Is.EqualTo(new[] { 1, 1 }), "行号表随前插平移（指令下标单位）");
        Assert.That(EcxInterpreter.Run(image, new EcxHost(), out Value value), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(7));
    }

    [Test]
    public void Call_ReturnLandsInReceiverRegister_AndRoundTrips()
    {
        // 接收槽恒为寄存器（无哨兵）：接收寄存器往返 + RET 固定落点
        var caller = new EcsFunction
        {
            Name = "caller",
            Module = "main",
            NSlots = EcsRegisters.ArgBase + 1,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.Call, EcsRegisters.Ret, 0, EcsRegisters.Ret, 1u),
                new EcsInstruction(EcsOpcode.Ret, EcsRegisters.Ret, 0, 0),
            ],
        };
        var callee = new EcsFunction
        {
            Name = "callee",
            Module = "main",
            NSlots = EcsRegisters.ArgBase + 1,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.LoadI, EcsRegisters.ArgBase, 9, 0),
                new EcsInstruction(EcsOpcode.Ret, EcsRegisters.ArgBase, 0, 0),
            ],
        };

        ModuleArtifact Caller() => new()
        {
            Name = "main",
            Functions = [caller],
            Pool = new ModulePool(),
            Imports = [],
            Exports = [new EcsExport { Name = "$eval", LocalFid = 0 }],
            Globals = [],
            Natives = [],
            Structs = [],
            ILNames = [],
            HasEval = true,
        };
        ModuleArtifact Callee() => new()
        {
            Name = "main",
            Functions = [callee],
            Pool = new ModulePool(),
            Imports = [],
            Exports = [],
            Globals = [],
            Natives = [],
            Structs = [],
            ILNames = [],
        };

        EcxImage image = EcxPipeline.Link([Caller(), Callee()], keyAction: false, needIL: false);
        Assert.That(EcxInterpreter.Run(image, new EcxHost(), out Value value), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(9), "返回值落入 RET（C=0）固定落点");

        // 序列化往返：寄存器操作数逐指令等价
        var bytes = InstructionCodec.Project(image.Functions[image.Entry].Instructions);
        var lifted = InstructionCodec.Lift(bytes, 0, bytes.Length, "roundtrip");
        Assert.That(lifted, Is.EqualTo(image.Functions[image.Entry].Instructions), "寄存器操作数往返逐指令等价");
    }
}