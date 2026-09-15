using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// ABI 一致性契约（docs/VM2.md §9.1 / VmSemanticContract §四）：
/// 1. syscall 编号稳定（发布即 ABI，只追加不回收——锁定常量值）；
/// 2. L2 全集编号化：内建不进原生名表，名表仅承载 L3（采集洞 "__xxx__" / FFI "库!导出名"）；
/// 3. 特征需求掩码：FILE/FFI/CAPTURE/IL 链接期计算，写入镜像头保留位 u16 @0x0C；
/// 4. C VM 加载期拒跑：宿主 feats 缺位 → ECS_ERR_FEAT（IL → ECS_ERR_IL 既有码）。
/// </summary>
[TestFixture]
public class AbiContractTests
{
    string _dir = null!;
    string _vmBinary = null!;
    string _workDir = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"EcsAbi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _workDir = Path.Combine(_dir, "vm");
        Directory.CreateDirectory(_workDir);
        _vmBinary = CvmRunner.EnsureBuilt(_workDir);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Test]
    public void Syscall_Numbering_IsStableAbi()
    {
        // 编号发布即 ABI：本测试锁定常量值，任何变更必须是有意为之的 ABI 破坏
        Assert.Multiple(() =>
        {
            Assert.That(EcsSyscall.FWrite, Is.EqualTo(1));
            Assert.That(EcsSyscall.FRead, Is.EqualTo(2));
            Assert.That(EcsSyscall.FOpen, Is.EqualTo(3));
            Assert.That(EcsSyscall.FClose, Is.EqualTo(4));
            Assert.That(EcsSyscall.FEof, Is.EqualTo(5));
            Assert.That(EcsSyscall.ReadFile, Is.EqualTo(6));
            Assert.That(EcsSyscall.WriteFile, Is.EqualTo(7));
            Assert.That(EcsSyscall.AppendFile, Is.EqualTo(8));
            Assert.That(EcsSyscall.FileExists, Is.EqualTo(9));
            Assert.That(EcsSyscall.Alert, Is.EqualTo(10));
            Assert.That(EcsSyscall.Arg, Is.EqualTo(11));
            Assert.That(EcsSyscall.Env, Is.EqualTo(12));
            Assert.That(EcsSyscall.App, Is.EqualTo(13));
            Assert.That(EcsSyscall.Time, Is.EqualTo(14));
            Assert.That(EcsSyscall.Beep, Is.EqualTo(15));
            Assert.That(EcsSyscall.Amiibo, Is.EqualTo(16));
            Assert.That(EcsSyscall.OcrConf, Is.EqualTo(17));
        });
    }

    [Test]
    public void FeatureMask_File_Ffi_Il()
    {
        // 文件族/PRINT → FILE；extern → FFI；图像标签 → IL（NeedIL 投影）
        var fileScript = Path.Combine(_dir, "file.ecs");
        File.WriteAllText(fileScript, "PRINT \"x\"\n");
        var fileImage = Compilation.CompileFile(fileScript, new CompileOptions { UseDiskCache = false }).Image!;
        Assert.That(fileImage.Features & EcsImageFeatures.File, Is.Not.Zero, "PRINT → FILE");
        Assert.That(fileImage.Features & (EcsImageFeatures.Ffi | EcsImageFeatures.Capture | EcsImageFeatures.Il),
            Is.Zero, "无 FFI/采集洞/IL");

        var ffiScript = Path.Combine(_dir, "ffi.ecs");
        File.WriteAllText(ffiScript, "EXTERN FUNC Sleep($ms:INT) FROM \"kernel32.dll\"\nSleep(1)\n");
        var ffiImage = Compilation.CompileFile(ffiScript, new CompileOptions { UseDiskCache = false }).Image!;
        Assert.That(ffiImage.Features & EcsImageFeatures.Ffi, Is.Not.Zero, "EXTERN → FFI");
        Assert.That(ffiImage.Natives.Select(n => n.Name), Does.Contain("kernel32.dll!Sleep"),
            "FFI 按名进名表（L3）");

        var imgScript = Path.Combine(_dir, "img.ecs");
        File.WriteAllText(imgScript, "$v = @enemy\nPRINT $v\n");
        var r = Compilation.CompileFile(imgScript, new CompileOptions
        {
            UseDiskCache = false,
            ExtVars = System.Collections.Immutable.ImmutableHashSet.Create("enemy"),
        });
        Assert.That(r.Image!.Features & EcsImageFeatures.Il, Is.Not.Zero, "图像标签 → IL");
    }

    [Test]
    public void Cvm_Load_RefusesFfiWithoutHostSupport()
    {
        // 特征位升级保证：FFI 镜像在无 FFI 能力的宿主上加载即拒跑（不再等到运行期）
        if (_vmBinary == null)
            Assert.Ignore("无 cc 编译器，跳过 C VM 侧");
        var ffiScript = Path.Combine(_dir, "ffi2.ecs");
        File.WriteAllText(ffiScript, "EXTERN FUNC Sleep($ms:INT) FROM \"kernel32.dll\"\nSleep(1)\n");
        var image = Compilation.CompileFile(ffiScript, new CompileOptions { UseDiskCache = false }).Image!;
        File.WriteAllBytes(Path.Combine(_workDir, "ffi.ecx"), EcxWriter.Write(image));

        var (exitCode, _, _) = CvmRunner.Run(_vmBinary, EcxWriter.Write(image), "abi-ffi", _workDir);
        Assert.That(exitCode, Is.EqualTo(14), "MCU 参考宿主不提供 FFI → ECS_ERR_FEAT 加载期拒跑");
    }

    [Test]
    public void Cvm_AcceptsFileFamily_AndL2StubParity()
    {
        // 宿主 feats=FILE（参考桩覆盖文件族"静默/默认值"）→ 文件脚本可加载执行；
        // TIME 恒 0（MCU 参考桩语义），双端 exit=0
        if (_vmBinary == null)
            Assert.Ignore("无 cc 编译器，跳过 C VM 侧");
        var src = Path.Combine(_dir, "mix.ecs");
        File.WriteAllText(src, "$t = TIME()\nPRINT $t\nALERT(\"a\")\n");
        var image = Compilation.CompileFile(src, new CompileOptions { UseDiskCache = false }).Image!;
        Assert.That(image.Features & EcsImageFeatures.File, Is.Not.Zero);

        // 解释器侧：参考处理器执行（TIME 默认 0）
        var host = EcsTestHost.CreateRecording();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "0" }), "EcxHost 默认 TimeMs = 0");

        // C VM 侧：桩 TIME 恒 0，逐字对齐（--print 通道开，stdout 可见）
        var (exitCode, stdout, _) = CvmRunner.Run(_vmBinary, EcxWriter.Write(image), "abi-mix", _workDir);
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(new[] { "0" }), "TIME 参考桩恒 0");
    }
}