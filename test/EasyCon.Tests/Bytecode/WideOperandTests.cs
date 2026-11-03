using EasyCon.Core.Hosting;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public class WideOperandTests
{
    [Test]
    public void NarrowEncoding_RemainsCompact_IncludingRegister255()
    {
        EcsInstruction[] code = [new(EcsOpcode.LoadI, 255, -2), new(EcsOpcode.Ret, 255, 0)];
        byte[] bytes = InstructionCodec.Project(code);
        Assert.That(bytes, Is.EqualTo(new byte[] { (byte)EcsOpcode.LoadI, 255, 254, 255, (byte)EcsOpcode.Ret, 255, 0, 0 }));
        Assert.That(InstructionCodec.Lift(bytes, 0, bytes.Length), Is.EqualTo(code));
    }

    [Test]
    public void WideEncoding_PreservesOperandsImmediatesAndDataWords()
    {
        EcsInstruction[] code =
        [
            new(EcsOpcode.LoadI, 256, short.MinValue),
            new(EcsOpcode.LoadK, 65534, ushort.MaxValue),
            new(EcsOpcode.AddI, 1, 256, 65534),
            new(EcsOpcode.Slice, 300, 512, 1024, 65534),
            new(EcsOpcode.Call, 512, 254, 65534, 0x12345678),
            new(EcsOpcode.Ret, 65534, 0),
        ];
        byte[] bytes = InstructionCodec.Project(code);
        Assert.That(bytes.Length, Is.EqualTo(56));
        Assert.That(bytes[..8], Is.EqualTo(new byte[] { (byte)((byte)EcsOpcode.LoadI | 0x80), 0, 0, 128, 1, 0, 0, 0 }));
        List<EcsInstruction> restored = InstructionCodec.Lift(bytes, 0, bytes.Length);
        Assert.That(restored, Is.EqualTo(code));
        Assert.That(InstructionCodec.Project(restored), Is.EqualTo(bytes));
    }

    [Test]
    public void MixedWidthJumps_RoundTrip_AndRejectTargetsInsideExtension()
    {
        EcsInstruction[] code =
        [
            new(EcsOpcode.Jmp, 0, 0, Jump: 2),
            new(EcsOpcode.LoadI, 500, -123),
            new(EcsOpcode.CmpJ, 500, 1, 0, Jump: -2),
            new(EcsOpcode.Jpt, 500, 0, Jump: -3),
            new(EcsOpcode.Jpf, 1, 0, Jump: -4),
            new(EcsOpcode.Ret, 500, 0),
        ];
        byte[] bytes = InstructionCodec.Project(code);
        Assert.That(InstructionCodec.Lift(bytes, 0, bytes.Length), Is.EqualTo(code));
        bytes[1] = 4; // target byte 8 is the LoadI operand extension, not an instruction.
        Assert.That(() => InstructionCodec.Lift(bytes, 0, bytes.Length), Throws.TypeOf<BytecodeException>());
    }

    static IEnumerable<byte[]> InvalidWideInstructions()
    {
        byte load = (byte)((byte)EcsOpcode.LoadI | 0x80);
        yield return [load, 0, 1, 0]; // missing extension
        yield return [load, 0, 1, 0, 1, 0, 0]; // truncated extension
        yield return [load, 0, 1, 0, 0, 0, 0, 0]; // redundant extension
        yield return [load, 0, 1, 0, 1, 0, 0, 1]; // reserved byte
        yield return [load, 0, 1, 0, 1, 1, 0, 0]; // Bx cannot be extended
        yield return [load, 255, 1, 0, 255, 0, 0, 0]; // register 65535
        yield return [(byte)((byte)EcsOpcode.Jmp | 0x80), 0, 0, 0, 1, 0, 0, 0];
        yield return [(byte)((byte)EcsOpcode.Call | 0x80), 0, 0, 0, 1, 0, 0, 0]; // missing data word
        yield return [255, 0, 0, 0, 1, 0, 0, 0]; // unknown base opcode
    }

    [TestCaseSource(nameof(InvalidWideInstructions))]
    public void MalformedWideEncoding_IsRejected(byte[] bytes)
        => Assert.That(() => InstructionCodec.Lift(bytes, 0, bytes.Length), Throws.TypeOf<BytecodeException>());

    [Test]
    public void MaximumFrame_RoundTripsAndRuns_OverflowIsRejected()
    {
        EcxImage image = Image(65535, [new(EcsOpcode.LoadI, 65534, 42), new(EcsOpcode.Ret, 65534, 0)]);
        ArgumentWindowTests.AssertResult(EcsContainer.ReadImage(EcsContainer.WriteImage(image)), 42);
        image.Functions[0].NSlots = 65536;
        Assert.That(() => EcsContainer.WriteImage(image), Throws.TypeOf<BytecodeException>());
    }

    [Test]
    public void PreviousFormatAndAbi_AreRejected()
    {
        byte[] bytes = EcsContainer.WriteImage(Image(1, [new(EcsOpcode.Ret0, 0, 0)]));
        bytes[4] = 1;
        EcsContainer.ResealCrc32(bytes);
        Assert.That(() => EcsContainer.ReadImage(bytes), Throws.TypeOf<BytecodeException>());
        bytes[4] = (byte)EcsContainer.FlatFormat;
        bytes[6] = 1;
        EcsContainer.ResealCrc32(bytes);
        Assert.That(() => EcsContainer.ReadImage(bytes), Throws.TypeOf<BytecodeException>());

        ModuleArtifact module = Compilation.CompileSource("RETURN 42", ScriptCompileProfiles.Interactive()).Artifacts[^1];
        byte[] cache = EcsContainer.WriteModule(module);
        cache[6] = 1;
        EcsContainer.ResealCrc32(cache);
        Assert.That(() => EcsContainer.ReadModule(cache), Throws.TypeOf<BytecodeException>());
    }

    internal static EcxImage Image(int slots, List<EcsInstruction> code) => new()
    {
        Entry = 0,
        MaxSlots = slots,
        Functions = [new EcsFunction { Name = "main", Module = "main", NSlots = slots, HasReturn = true, Instructions = code }],
    };
}

[TestFixture(true)]
[TestFixture(false)]
public class WideOperandCvmTests(bool directThreading)
{
    string _directory = null!;
    string _binary = null!;

    [OneTimeSetUp]
    public void BuildVm()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"EcsWide_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        string? binary = CvmRunner.EnsureBuilt(_directory, directThreading);
        if (binary == null)
            Assert.Ignore("无 C99 编译器，跳过原生对照验证");
        _binary = binary!;
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            Assert.That(Path.GetFullPath(_directory).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.Delete(_directory, true);
        }
    }

    void AssertBoth(EcxImage image, int expected, params int[] waits)
    {
        EcxHost host = EcsTestHost.CreateRecording();
        ArgumentWindowTests.AssertResult(image, expected, host);
        Assert.That(host.WaitLog, Is.EqualTo(waits.Select(ms => $"WAIT {ms}")));
        byte[] bytes = EcsContainer.WriteImage(image);
        EcxHost restoredHost = EcsTestHost.CreateRecording();
        ArgumentWindowTests.AssertResult(EcsContainer.ReadImage(bytes), expected, restoredHost);
        Assert.That(restoredHost.WaitLog, Is.EqualTo(waits.Select(ms => $"WAIT {ms}")));
        (int exitCode, string stdout, string stderr) = CvmRunner.Run(_binary, bytes, TestContext.CurrentContext.Test.ID, _directory);
        Assert.That(exitCode, Is.EqualTo(0), stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(host.Lines));
        Assert.That(CvmRunner.ParseEventLogs(stderr), Is.EqualTo(EcsTestHost.EventLogGroups(host)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ThousandLiveValues_CompileAndRunOnBothVms(bool optimize)
    {
        CompileResult result = Compilation.CompileSource(RegisterFileContractTests.BuildWideSource(1024),
            ScriptCompileProfiles.Interactive(optimize: optimize));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty, string.Join("\n", result.Diagnostics));
        Assert.That(result.Image!.MaxSlots, Is.GreaterThan(1024));
        AssertBoth(result.Image, 100001);
    }

    [Test]
    public void WideArgumentsAndReturnRegisters_PreserveCallerAndCalleeValues()
    {
        EcxImage image = WideOperandTests.Image(600,
        [
            new(EcsOpcode.LoadI, 255, 20),
            new(EcsOpcode.LoadI, 256, 22),
            new(EcsOpcode.Call, 255, 2, 300, 1),
            new(EcsOpcode.WaitV, 300, 0),
            new(EcsOpcode.LoadI, 310, -1),
            new(EcsOpcode.CallN, 310, 1, 511, EcsSyscall.CallFlag | EcsSyscall.Arg),
            new(EcsOpcode.Len, 512, 511),
            new(EcsOpcode.WaitV, 512, 0),
            new(EcsOpcode.Ret, 300, 0),
        ]);
        image.Functions.Add(new EcsFunction
        {
            Name = "callee",
            Module = "main",
            NSlots = 600,
            NParams = 2,
            HasReturn = true,
            Instructions = [new(EcsOpcode.Move, 512, 1), new(EcsOpcode.AddI, 513, 512, 2), new(EcsOpcode.Ret, 513, 0)],
        });
        AssertBoth(image, 42, 42, 0);
    }

    [Test]
    public void WideLoopAndConditionalJumps_FollowMixedWidthLayout()
    {
        EcxImage image = WideOperandTests.Image(300,
        [
            new(EcsOpcode.LoadI, 260, 0),
            new(EcsOpcode.LoadI, 261, 3),
            new(EcsOpcode.LoadI, 262, 0),
            new(EcsOpcode.AddI, 262, 262, 260),
            new(EcsOpcode.ForStep, 260, 261, 263, Jump: 3),
            new(EcsOpcode.Move, 260, 263),
            new(EcsOpcode.Nop, 0, 0),
            new(EcsOpcode.Jmp, 0, 0, Jump: -5),
            new(EcsOpcode.CmpJ, 262, 261, 4, Jump: 1),
            new(EcsOpcode.Halt, 0, 0),
            new(EcsOpcode.Jpt, 262, 0, Jump: 1),
            new(EcsOpcode.Halt, 0, 0),
            new(EcsOpcode.LoadBool, 264, 0),
            new(EcsOpcode.Jpf, 264, 0, Jump: 1),
            new(EcsOpcode.Halt, 0, 0),
            new(EcsOpcode.WaitV, 262, 0),
            new(EcsOpcode.Ret, 262, 0),
        ]);
        AssertBoth(image, 6, 6);
    }

    [Test]
    public void WideArrayOperandsAndSliceDataWord_PreserveContainerSemantics()
    {
        EcxImage image = WideOperandTests.Image(600,
        [
            new(EcsOpcode.LoadI, 256, 10),
            new(EcsOpcode.LoadI, 257, 20),
            new(EcsOpcode.LoadI, 258, 30),
            new(EcsOpcode.NewArrV, 300, 3, 256, (uint)EcsTypeCode.Int),
            new(EcsOpcode.LoadI, 400, 1),
            new(EcsOpcode.LoadI, 401, 3),
            new(EcsOpcode.LoadI, 402, 0),
            new(EcsOpcode.Slice, 450, 300, 400, 401),
            new(EcsOpcode.GetI, 451, 450, 402),
            new(EcsOpcode.Len, 452, 450),
            new(EcsOpcode.WaitV, 451, 0),
            new(EcsOpcode.WaitV, 452, 0),
            new(EcsOpcode.NewArrE, 510, (int)EcsTypeCode.Int),
            new(EcsOpcode.Len, 511, 510),
            new(EcsOpcode.WaitV, 511, 0),
            new(EcsOpcode.Ret, 451, 0),
        ]);
        AssertBoth(image, 20, 20, 2, 0);
    }

    [Test]
    public void MalformedExtension_IsRejectedByNativeVm()
    {
        byte[] bytes = EcsContainer.WriteImage(WideOperandTests.Image(300,
            [new(EcsOpcode.LoadI, 256, 42), new(EcsOpcode.Ret, 256, 0)]), stripDebug: true);
        int codeStart = bytes.Length - (int)BitConverter.ToUInt32(bytes, 16);
        bytes[codeStart + 7] = 1;
        EcsContainer.ResealCrc32(bytes);
        (int exitCode, _, _) = CvmRunner.Run(_binary, bytes, "bad-wide", _directory);
        Assert.That(exitCode, Is.EqualTo(EcxInterpreter.ERR_OPCODE));
    }

    [Test]
    public void FrameExceedingDeviceMemory_ReportsPoolCapacity()
    {
        byte[] bytes = EcsContainer.WriteImage(WideOperandTests.Image(65535,
            [new(EcsOpcode.LoadI, 65534, 42), new(EcsOpcode.Ret, 65534, 0)]));
        (int exitCode, _, string stderr) = CvmRunner.Run(_binary, bytes, "large-frame", _directory);
        Assert.That(exitCode, Is.EqualTo(18), stderr); // ECS_ERR_POOL: native frame arena exhausted.
    }
}