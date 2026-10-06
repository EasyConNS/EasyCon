using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 槽位宽度契约（v3 定长编码修订）：指令操作数槽位 = u8（≤254，255 = 无接收哨兵），
/// 函数槽位上限随之 ≤254——与 C VM 宿主档案 ECS_MAX_SLOTS=255 对齐（宽槽镜像自 C VM
/// 诞生起即不可装载；v3 把这条容量线前移到编译期，响亮拒绝而非链接/装载期失败）。
/// 原 R-5「>255 槽可编译」能力随 varint 基流退役；恢复路径 = 每函数宽窄双布局（挂账）。
/// 本套件锁：宽槽脚本编译期明确诊断 + 哨兵/高槽边界语义。
/// </summary>
[TestFixture]
public class PcWideSlotTests
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
    public void WideScript_ExceedingV3SlotCap_FailsWithDiagnostic()
    {
        string source = BuildWideSource(270);
        CompileResult result = Compilation.CompileSource(source, new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = false,
        });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Not.Empty,
            "v3 定长编码下 >254 槽函数应编译期拒绝（槽位 u8 ≤254，255 = 无接收哨兵）");
        Assert.That(result.Image, Is.Null);
    }

    [Test]
    public void ProcessCache_HitsOnRepeatedNarrowCompile()
    {
        // 档位塌缩的附带收益：桌面路径进程级产物缓存（同源码⊕同选项必命中）
        ModuleProjectResult first = ProjectCompiler.CompileProject(SyntaxTree.Parse(BuildWideSource(250)), new CompileOptions
        {
            UseDiskCache = false,
            UseProcessCache = true,
        });
        ModuleProjectResult second = ProjectCompiler.CompileProject(SyntaxTree.Parse(BuildWideSource(250)), new CompileOptions
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
    public void Linker_PrependsModuleInitializersToWideEntry()
    {
        var init = new EcsFunction
        {
            Name = "$eval",
            Module = "lib",
            NSlots = 1,
            Instructions = [new EcsInstruction(EcsOpcode.Ret0, 0, 0, 0)],
        };
        var entry = new EcsFunction
        {
            Name = "$eval",
            Module = "main",
            NSlots = 255,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.LoadI, 254, 7, 0),
                new EcsInstruction(EcsOpcode.Ret, 254, 0, 0),
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
        Assert.That(linkedEntry.Instructions[0].C, Is.EqualTo(EcsInstruction.NoSlot));
        Assert.That(linkedEntry.LineTable, Is.EqualTo(new[] { 1, 1 }), "行号表随前插平移（指令下标单位）");
        Assert.That(EcxInterpreter.Run(image, new EcxHost(), out Value value), Is.EqualTo(EcxInterpreter.OK));
        Assert.That(value.AsInt(), Is.EqualTo(7));
    }

    [Test]
    public void Call_HighSlotReturnDestination_AndNoSlotSentinel()
    {
        // 槽位上界 254（255 = 无接收哨兵）：高槽返回目的地 + NoSlot 往返（线上 255 ↔ 内存 -1）
        var caller = new EcsFunction
        {
            Name = "caller",
            Module = "main",
            NSlots = 255,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.Call, 0, 0, 254, 1u),
                new EcsInstruction(EcsOpcode.Ret, 254, 0, 0),
            ],
        };
        var callee = new EcsFunction
        {
            Name = "callee",
            Module = "main",
            NSlots = 1,
            HasReturn = true,
            Instructions =
            [
                new EcsInstruction(EcsOpcode.LoadI, 0, 9, 0),
                new EcsInstruction(EcsOpcode.Ret, 0, 0, 0),
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
        Assert.That(value.AsInt(), Is.EqualTo(9));

        // 序列化往返：NoSlot（-1）↔ 线上 255
        var bytes = InstructionCodec.Project(image.Functions[image.Entry].Instructions);
        var lifted = InstructionCodec.Lift(bytes, 0, bytes.Length, "sentinel");
        Assert.That(lifted, Is.EqualTo(image.Functions[image.Entry].Instructions), "哨兵与高槽往返逐指令等价");
    }
}
