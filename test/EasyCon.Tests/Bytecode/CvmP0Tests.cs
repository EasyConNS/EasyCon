using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;
using EasyCon.Tests.Support;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Tests.Bytecode;

// Both dispatch implementations execute the same public ABI/resource/continuation tests.
[TestFixture(false)]
[TestFixture(true)]
public class CvmP0Tests
{
    readonly bool _portable;
    string _directory = null!;
    string _normal = null!;
    string _budget = null!;

    public CvmP0Tests(bool portable) => _portable = portable;

    [OneTimeSetUp]
    public async Task BuildProbes()
    {
        if (CvmRunner.FindCc() == null)
        {
            if (Environment.GetEnvironmentVariable("ECSVM_REQUIRE_CC") == "1")
                Assert.Fail("P0 验收需要 C99 编译器，不能跳过");
            Assert.Ignore("无 C99 编译器，P0 原生验收未执行");
        }
        _directory = Path.Combine(Path.GetTempPath(), $"EasyConP0_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _normal = await BuildProbe("normal", false);
        _budget = await BuildProbe("budget", true);
    }

    async Task<string> BuildProbe(string name, bool budget)
    {
        string native = CvmRunner.FindNativeDir()!;
        string binary = Path.Combine(_directory, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        ProcessStartInfo start = new(CvmRunner.FindCc()!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        List<string> arguments = ["-O2", "-std=c99", "-Wall", "-Wextra", "-Werror", "-I", native];
        if (_portable) arguments.Add("-DECS_NO_DIRECT_THREADING=1");
        if (budget) arguments.AddRange(["-DECS_ARENA_BYTES=768", "-DECS_FRAME_SEG_BYTES=384"]);
        arguments.AddRange([Path.Combine(CorpusAssert.RepoRoot(), "test", "EasyCon.Tests", "Bytecode", "native", "p0_probe.c"),
            Path.Combine(native, "ecs_vm.c"), "-lm", "-o", binary]);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        (int exit, string stdout, string stderr) = await Execute(start);
        Assert.That(exit, Is.Zero, stdout + stderr);
        Assert.That(stderr, Is.Empty, "P0 原生构建不允许警告");
        return binary;
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (_directory != null && Directory.Exists(_directory))
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            Assert.That(Path.GetFullPath(_directory).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.Delete(_directory, true);
        }
    }

    static async Task<(int Exit, string Stdout, string Stderr)> Execute(ProcessStartInfo start)
    {
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            Assert.Fail("P0 原生探针超时");
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    Task<(int Exit, string Stdout, string Stderr)> Run(string binary, string mode, EcxImage? image = null)
    {
        return RunBytes(binary, mode, image == null ? null : EcsContainer.WriteImage(image, stripDebug: true));
    }

    async Task<(int Exit, string Stdout, string Stderr)> RunBytes(string binary, string mode, byte[]? image)
    {
        ProcessStartInfo start = new(binary)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(mode);
        if (image != null)
        {
            string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".ecx");
            File.WriteAllBytes(path, image);
            start.ArgumentList.Add(path);
        }
        return await Execute(start);
    }

    static EcxImage Image(IEnumerable<EcsInstruction> instructions, params EcsConst[] constants) => new()
    {
        Consts = [.. constants],
        Modules = ["p0"],
        Natives = [new EcsNative { Name = "probe" }],
        Functions = [new EcsFunction { Name = "<main>", Module = "p0", NSlots = 12, Instructions = [.. instructions] }],
        MaxSlots = 12,
        MaxDepth = 1,
    };

    static List<string> Reference(EcxImage image)
    {
        List<string> lines = [];
        EcxHost host = new()
        {
            Native = (_, args, context) =>
            {
                if (args.Length > 0)
                {
                    TaggedValue value = args[0];
                    ulong bits = value.Tag is EcsTag.Double or EcsTag.UInt64 or EcsTag.Ptr
                        ? unchecked((ulong)value.I64) : unchecked((uint)value.I32);
                    lines.Add(value.Tag == EcsTag.String ? "str:" + context.Str(value) : $"{value.Tag}:{bits:x16}");
                }
                return TaggedValue.Void;
            },
        };
        Assert.That(EcxInterpreter.Run(image, host), Is.Zero, "C# 参考镜像必须可执行");
        return lines;
    }

    static IEnumerable<TestCaseData> ArithmeticCases()
    {
        foreach (EcsOpcode op in new[] { EcsOpcode.AddU, EcsOpcode.SubU, EcsOpcode.MulU, EcsOpcode.DivU, EcsOpcode.ModU,
            EcsOpcode.AddL, EcsOpcode.SubL, EcsOpcode.MulL, EcsOpcode.DivL, EcsOpcode.ModL,
            EcsOpcode.AddD, EcsOpcode.SubD, EcsOpcode.MulD, EcsOpcode.DivD, EcsOpcode.EqD, EcsOpcode.LtD, EcsOpcode.LtL })
            foreach (int destination in new[] { 1, 2 })
                yield return new TestCaseData(op, destination);
    }

    [TestCaseSource(nameof(ArithmeticCases))]
    public async Task Arithmetic_WithAliasedDestination_MatchesCSharp(EcsOpcode op, int destination)
    {
        bool floating = op.ToString().EndsWith('D');
        bool unsigned = op.ToString().EndsWith('U');
        EcsConst left = floating ? EcsConst.FromDouble(7.5) : unsigned ? EcsConst.FromUInt(7) : EcsConst.FromUInt64(7);
        EcsConst right = floating ? EcsConst.FromDouble(2.5) : unsigned ? EcsConst.FromUInt(3) : EcsConst.FromUInt64(3);
        EcxImage image = Image([new(EcsOpcode.LoadK, 1, 0), new(EcsOpcode.LoadK, 2, 1), new(op, destination, 1, 2),
            new(EcsOpcode.CallN, destination, 1, 0), new(EcsOpcode.Ret0, 0, 0)], left, right);
        (int exit, string stdout, string stderr) = await Run(_normal, "run", image);
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(Reference(image)));
    }

    [Test]
    public async Task LayoutAndPublicWideHelpers_PreserveSentinelsAndRejectExhaustion()
    {
        foreach (string probe in new[] { _normal, _budget })
        {
            (int exit, string stdout, string stderr) = await Run(probe, "api");
            Assert.That(exit, Is.Zero, stdout + stderr);
            Assert.That(stdout, Does.Contain("value=8 pointer="));
            if (probe == _budget)
            {
                Assert.That(stdout, Does.Contain("blocks=3"));
                int instance = int.Parse(System.Text.RegularExpressions.Regex.Match(stdout, @"instance=(\d+)").Groups[1].Value);
                Assert.That(instance, Is.LessThanOrEqualTo(1024));
            }
        }
    }

    [Test]
    public async Task WideConstants_KeepFullBits_IncludingNaNInfinityAndInvalidPointer()
    {
        EcsConst[] constants = [EcsConst.FromUInt64(ulong.MaxValue), EcsConst.FromUInt64(1UL << 63),
            EcsConst.FromPtr(-1), EcsConst.FromDouble(-0.0), EcsConst.FromDouble(double.NaN),
            EcsConst.FromDouble(double.PositiveInfinity), EcsConst.FromDouble(double.NegativeInfinity)];
        foreach (EcsConst constant in constants)
        {
            EcxImage image = Image([new(EcsOpcode.LoadK, 1, 0), new(EcsOpcode.CallN, 1, 1, 0),
                new(EcsOpcode.Ret0, 0, 0)], constant);
            (int exit, string stdout, string stderr) = await Run(_budget, "run", image);
            Assert.That(exit, Is.Zero, stderr);
            Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(Reference(image)));
        }
    }

    [TestCase(EcsConvKind.DoubleToInt)]
    [TestCase(EcsConvKind.DoubleToUInt64)]
    [TestCase(EcsConvKind.UInt64ToDouble)]
    [TestCase(EcsConvKind.PtrToInt)]
    public async Task Conversion_WithAliasedSource_MatchesCSharp(EcsConvKind conversion)
    {
        EcsConst value = conversion == EcsConvKind.UInt64ToDouble ? EcsConst.FromUInt64(1UL << 63)
            : conversion == EcsConvKind.PtrToInt ? EcsConst.FromPtr(-1) : EcsConst.FromDouble(double.PositiveInfinity);
        EcxImage image = Image([new(EcsOpcode.LoadK, 1, 0), new(EcsOpcode.Conv, 1, 1, (int)conversion),
            new(EcsOpcode.CallN, 1, 1, 0), new(EcsOpcode.Ret0, 0, 0)], value);
        (int exit, string stdout, string stderr) = await Run(_budget, "run", image);
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(Reference(image)));
    }

    [TestCase("host-wide")]
    [TestCase("host-double")]
    [TestCase("host-string")]
    public async Task NativeBornReferences_AreReclaimed_Across2000CallsInThreeBlocks(string mode)
    {
        List<EcsInstruction> instructions = [.. Enumerable.Repeat(new EcsInstruction(EcsOpcode.CallN, 0, 0, 1), 2000),
            new(EcsOpcode.Ret0, 0, 0)];
        (int exit, _, string stderr) = await Run(_budget, mode, Image(instructions));
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(stderr, Does.Contain("calls=2000"));
    }

    [Test]
    public async Task WideArrayTemporaries_AreReclaimed_AcrossCopiesReadsContainsAndFormatting()
    {
        List<EcsInstruction> instructions = [new(EcsOpcode.LoadK, 1, 0), new(EcsOpcode.LoadK, 2, 1),
            new(EcsOpcode.NewArrV, 3, 2, 1, EcsTag.Double), new(EcsOpcode.LoadI, 1, 0), new(EcsOpcode.LoadI, 2, 0),
            new(EcsOpcode.LoadI, 4, 0)];
        for (int i = 0; i < 500; i++)
            instructions.AddRange([new(EcsOpcode.LoadI, 2, 0), new(EcsOpcode.LoadI, 5, 0),
                new(EcsOpcode.Slice, 5, 3, 4, uint.MaxValue), new(EcsOpcode.LoadI, 5, 0),
                new(EcsOpcode.Cat, 5, 3, 3), new(EcsOpcode.LoadI, 5, 0),
                new(EcsOpcode.LoadK, 2, 0), new(EcsOpcode.Append, 5, 3, 2), new(EcsOpcode.LoadI, 5, 0),
                new(EcsOpcode.Cont, 5, 2, 3), new(EcsOpcode.GetI, 5, 3, 4), new(EcsOpcode.LoadI, 5, 0),
                new(EcsOpcode.Conv, 5, 3, (int)EcsConvKind.ToStr)]);
        instructions.AddRange([new(EcsOpcode.CallN, 5, 1, 0), new(EcsOpcode.Ret0, 0, 0)]);
        EcxImage image = Image(instructions, EcsConst.FromDouble(1.5), EcsConst.FromDouble(2.5));
        (int exit, string stdout, string stderr) = await Run(_budget, "run", image);
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(Reference(image)));
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "str:[1.5, 2.5]" }));
    }

    [Test]
    public async Task GetI_OverwritingItsContainer_PreservesBorrowedChild()
    {
        EcxImage image = Image([new(EcsOpcode.CallN, 0, 0, 1), new(EcsOpcode.NewArrV, 3, 1, 1, EcsTag.String),
            new(EcsOpcode.LoadI, 1, 0), new(EcsOpcode.LoadI, 4, 0), new(EcsOpcode.GetI, 3, 3, 4),
            new(EcsOpcode.CallN, 3, 1, 0), new(EcsOpcode.Ret0, 0, 0)]);
        (int exit, _, string stderr) = await Run(_budget, "host-string", image);
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(stderr, Does.Contain("calls=2"));
    }

    [TestCase("ignore-pool", 18)]
    [TestCase("host-double", 18)]
    [TestCase("host-string", 18)]
    [TestCase("bad-return", 6)]
    public async Task NativeAllocationFailure_IsLoud_EvenWhenHostIgnoresHelperStatus(string mode, int expected)
    {
        EcxImage image = Image([new(EcsOpcode.NewArrE, 1, EcsTag.Int), new(EcsOpcode.NewArrE, 2, EcsTag.Int),
            new(EcsOpcode.NewArrE, 3, EcsTag.Int), new(EcsOpcode.CallN, 0, 0, 4), new(EcsOpcode.KeyI, 5, 0, Ext: 1),
            new(EcsOpcode.Ret0, 0, 0)]);
        (int exit, _, string stderr) = await Run(_budget, mode, image);
        Assert.That(exit, Is.EqualTo(expected), stderr);
        Assert.That(stderr, Does.Contain("func=0 pc=3"));
        Assert.That(stderr, Does.Contain("keys=0"));
    }

    [Test]
    public async Task BoxLoadAndContainerCopyExhaustion_ReportPoolAtFailingInstruction()
    {
        EcxImage boxImage = Image([new(EcsOpcode.LoadK, 1, 0), new(EcsOpcode.LoadK, 2, 0),
            new(EcsOpcode.LoadK, 3, 0), new(EcsOpcode.LoadK, 4, 0), new(EcsOpcode.Ret0, 0, 0)], EcsConst.FromDouble(1));
        EcxImage copyImage = Image([new(EcsOpcode.NewArrE, 1, EcsTag.Int), new(EcsOpcode.Move, 2, 1),
            new(EcsOpcode.NewArrE, 3, EcsTag.Int), new(EcsOpcode.NewArrE, 4, EcsTag.Int),
            new(EcsOpcode.SetVar, 5, 1), new(EcsOpcode.Ret0, 0, 0)]);
        foreach ((EcxImage image, int pc) in new[] { (boxImage, 3), (copyImage, 4) })
        {
            (int exit, _, string stderr) = await Run(_budget, "run", image);
            Assert.That(exit, Is.EqualTo(18), stderr);
            Assert.That(stderr, Does.Contain($"func=0 pc={pc}"));
        }
    }

    [Test]
    public async Task NestedViewAndDeepCopy_PreserveWideFieldValues()
    {
        EcxImage image = Image([new(EcsOpcode.NewSt, 1, 1), new(EcsOpcode.GetF, 3, 1, 0),
            new(EcsOpcode.LoadK, 2, 0), new(EcsOpcode.PutF, 2, 3, 0), new(EcsOpcode.GetF, 4, 1, 0),
            new(EcsOpcode.GetF, 5, 4, 0), new(EcsOpcode.CallN, 5, 1, 0), new(EcsOpcode.Move, 7, 1),
            new(EcsOpcode.SetVar, 6, 1),
            new(EcsOpcode.LoadK, 2, 1), new(EcsOpcode.PutF, 2, 3, 0), new(EcsOpcode.GetF, 4, 6, 0),
            new(EcsOpcode.GetF, 5, 4, 0), new(EcsOpcode.CallN, 5, 1, 0), new(EcsOpcode.Ret0, 0, 0)],
            EcsConst.FromDouble(3.5), EcsConst.FromDouble(9.5));
        image.Structs = [new EcsStructLayout { Name = "Inner", SlotCount = 1,
            Fields = [new EcsFieldLayout { Name = "value", Kind = EcsFieldKind.Scalar, Type = EcsTypeCode.Double }] },
            new EcsStructLayout { Name = "Outer", SlotCount = 1,
            Fields = [new EcsFieldLayout { Name = "inner", Kind = EcsFieldKind.NestedStruct, Type = EcsTypeCode.Struct, NestedSid = 0 }] }];
        (int exit, string stdout, string stderr) = await Run(_normal, "run", image);
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(Reference(image)));
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "6:400c000000000000", "6:400c000000000000" }));
    }

    static EcxImage WaitImage() => Image([new(EcsOpcode.WaitI, 0, 0, Ext: 10), new(EcsOpcode.LoadI, 1, 20),
        new(EcsOpcode.WaitV, 1, 0), new(EcsOpcode.KeyI, 5, 0, Ext: 1), new(EcsOpcode.Ret0, 0, 0)]);

    [Test]
    public async Task WaitYield_SuspendsBothWaitOpcodes_AndResumesAfterEachDeadline()
    {
        (int exit, string stdout, string stderr) = await Run(_budget, "wait", WaitImage());
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "WAIT 10", "SUSPEND waits=1 keys=0",
            "WAIT 20", "SUSPEND waits=2 keys=0", "KEY 5 1" }));
    }

    [TestCase("wait-cancel", 1)]
    [TestCase("wait-pre-cancel", 0)]
    [TestCase("wait-callback-cancel", 1)]
    public async Task WaitCancellation_IsTerminal_AndNeverRunsFollowingInput(string mode, int waits)
    {
        (int exit, string stdout, string stderr) = await Run(_budget, mode, WaitImage());
        Assert.That(exit, Is.EqualTo(2), stderr);
        Assert.That(stderr, Does.Contain($"waits={waits} keys=0"));
        Assert.That(stdout, Does.Not.Contain("KEY"));
    }

    [Test]
    public async Task WaitYield_DefaultOff_KeepsSingleRunDesktopBehavior()
    {
        (int exit, string stdout, string stderr) = await Run(_budget, "blocking", WaitImage());
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "WAIT 10", "WAIT 20", "KEY 5 1" }));
    }

    [Test]
    public async Task FrameArena_ReclaimsPoppedFrames_AndReportsNestedOverflow()
    {
        EcxImage reusable = Image([.. Enumerable.Repeat(new EcsInstruction(EcsOpcode.Call, 0, 0, Ext: 1), 1000),
            new(EcsOpcode.Ret0, 0, 0)]);
        reusable.Functions.Add(new EcsFunction
        {
            Name = "leaf",
            Module = "p0",
            NSlots = 4,
            Instructions = [new(EcsOpcode.Ret0, 0, 0)]
        });
        (int exit, _, string stderr) = await Run(_budget, "run", reusable);
        Assert.That(exit, Is.Zero, stderr);

        reusable.Functions[1].Instructions = [new(EcsOpcode.Call, 0, 0, Ext: 1), new(EcsOpcode.Ret0, 0, 0)];
        (int overflow, _, string error) = await Run(_budget, "run", reusable);
        Assert.That(overflow, Is.EqualTo(18), error);
        Assert.That(error, Does.Contain("func=1 pc=0"));
    }

    [Test]
    public async Task MissingFileCapability_PreservesMinusOne_AndPoolFailureRemainsAnError()
    {
        EcxImage sentinel = Image([new(EcsOpcode.CallN, 0, 0, 1, EcsSyscall.CallFlag | (uint)EcsSyscall.FOpen),
            new(EcsOpcode.CallN, 1, 1, 0), new(EcsOpcode.Ret0, 0, 0)]);
        (int exit, string stdout, string stderr) = await Run(_budget, "run", sentinel);
        Assert.That(exit, Is.Zero, stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "10:ffffffffffffffff" }));
        sentinel.Functions[0].Instructions.InsertRange(0, [new(EcsOpcode.NewArrE, 2, EcsTag.Int),
            new(EcsOpcode.NewArrE, 3, EcsTag.Int), new(EcsOpcode.NewArrE, 4, EcsTag.Int)]);
        (int pool, _, string error) = await Run(_budget, "run", sentinel);
        Assert.That(pool, Is.EqualTo(18), error);
        Assert.That(error, Does.Contain("func=0 pc=3"));
    }

    [Test]
    public async Task WideDivisionByZero_PreservesErrorCodeAndInstructionIndex()
    {
        EcxImage image = Image([new(EcsOpcode.LoadK, 1, 0), new(EcsOpcode.LoadK, 2, 1),
            new(EcsOpcode.DivL, 1, 1, 2), new(EcsOpcode.Ret0, 0, 0)], EcsConst.FromUInt64(1), EcsConst.FromUInt64(0));
        (int exit, _, string stderr) = await Run(_budget, "run", image);
        Assert.That(exit, Is.EqualTo(8), stderr);
        Assert.That(stderr, Does.Contain("func=0 pc=2"));
    }

    [Test]
    public async Task FixedLegacyEcx_ExecutesUnchangedOnBothInterpretersAndBudget()
    {
        string path = Path.Combine(CorpusAssert.RepoRoot(), "test", "EasyCon.Tests", "Bytecode", "native", "p0-legacy.ecx");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            Is.EqualTo("681429131EB83F03762413542F2C5B8BC113AA681365C3D4614AB693E0654089"));
        EcxImage image = EcsContainer.ReadImage(bytes);
        List<string> expected = Reference(image);
        Assert.That(expected, Is.EqualTo(new[] { "6:4010000000000000", "5:ffffffffffffffff" }));
        foreach (string probe in new[] { _normal, _budget })
        {
            (int exit, string stdout, string stderr) = await RunBytes(probe, "run", bytes);
            Assert.That(exit, Is.Zero, stderr);
            Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(expected));
        }
    }
}