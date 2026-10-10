using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 固定寄存器帧契约（abi_rev 4）：每帧定长 EcsRegisters.Count 槽（128 = RET + ARG 窗 + 通用区 +
/// STAGING 暂存），通用区可分配容量编译期锁定——超出即报寄存器溢出诊断（Lua 风格，不 spill）。
/// Call/CallN 的接收槽 C 恒为寄存器号（0 = RET 固定落点），无哨兵值。
/// 本套件锁：溢出编译期诊断 + RET 落点 + 指令编解码往返。
/// </summary>
[TestFixture]
public class RegisterFileContractTests
{
    static string BuildWideSource(int valueCount)
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

    [Test]
    public void WideScript_ExceedingRegisterBudget_FailsWithDiagnostic()
    {
        string source = BuildWideSource(270);
        CompileResult result = Compilation.CompileSource(source, new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = false,
        });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Not.Empty,
            "通用区容量（EcsRegisters.GenCount）超限应编译期拒绝（寄存器溢出诊断）");
        Assert.That(result.Image, Is.Null);
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
            NSlots = EcsRegisters.AllocBase,
            Instructions = [new EcsInstruction(EcsOpcode.Ret0, 0, 0, 0)],
        };
        var entry = new EcsFunction
        {
            Name = "$eval",
            Module = "main",
            NSlots = EcsRegisters.AllocBase + 1,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.LoadI, EcsRegisters.AllocBase, 7, 0),
                new EcsInstruction(EcsOpcode.Ret, EcsRegisters.AllocBase, 0, 0),
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
            NSlots = EcsRegisters.AllocBase + 1,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.Call, EcsRegisters.StagingBase, 0, EcsRegisters.Ret, 1u),
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
