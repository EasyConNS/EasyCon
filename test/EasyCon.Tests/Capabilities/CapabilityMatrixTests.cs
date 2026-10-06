using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;
using EasyCon.Tests.Support;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// S-21 能力矩阵四路对拍（V23_VM_REDESIGN.md §2.3）：同一能力受限镜像 ×
/// {全能力宿主, 桩宿主} 两档 × {C# 解释器, C VM} 双端。
/// 锁定三件事：① 缺能力档双端逐字锁步（C# EcsCapabilityDefaults ≡ C ecs_cap_*）；
/// ② 全能力宿主语义不受降级层影响；③ strict_caps 双态恢复响亮（C 加载期拒跑 /
/// C# 运行期 ERR）。平台差异 = 宿主能力差异，不再是 VM 语义差异。
/// </summary>
[TestFixture]
public class CapabilityMatrixTests
{
    /// <summary>跨能力族探针：NET 族（L3/Vision）+ JQ（L3）+ ENV/TIME（L2）+ 图像标签（IL）。</summary>
    const string Script = """
        $net = NET_LOAD("/m.onnx")
        PRINT $net
        $n = NET_RUN($net, [1])
        PRINT $n
        $o = NET_OUT(0)
        PRINT $o
        $q = JQ("{}", ".x")
        PRINT $q
        $e = ENV("ECX_MATRIX_UNDEF_XYZ")
        PRINT $e
        $t = TIME()
        PRINT $t
        $v = @enemy
        PRINT $v
        """;

    const string UndefEnvVar = "ECX_MATRIX_UNDEF_XYZ";

    [OneTimeSetUp]
    public void EnsureEnvAbsent()
    {
        Environment.SetEnvironmentVariable(UndefEnvVar, null);   // 探针要求该变量不存在
    }

    static (EcxImage Image, string Dir) Compile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"EcsMatrix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "matrix.ecs");
        File.WriteAllText(path, Script);
        var result = Compilation.CompileFile(path, new CompileOptions
        {
            ExtVars = ImmutableHashSet.Create("enemy"),
            UseDiskCache = false,
        });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return (result.Image!, dir);
    }

    [Test]
    public void Matrix_FourWay_Lockstep()
    {
        var (image, dir) = Compile();
        var workDir = Path.Combine(dir, "vm");
        Directory.CreateDirectory(workDir);
        try
        {
            var vmBinary = CvmRunner.EnsureBuilt(workDir);

            // ---- ① C# 桩宿主（Native=null → EcsCapabilityDefaults；录制收 PRINT 行）----
            var stub = EcsTestHost.CreateRecording();
            Assert.That(EcxInterpreter.Run(image, stub), Is.EqualTo(0), "C# 桩宿主应降级执行");
            var stubLines = new[] { "-1", "0", "0", "", "", "0", "-1" };
            Assert.That(stub.Lines, Is.EqualTo(stubLines), "缺省值表：NET 族/JQ/ENV/TIME/IL");

            if (vmBinary != null)
            {
                // ---- ② C VM 参考宿主（native=NULL → ecs_cap_*）：与 ① 逐字锁步 ----
                byte[] ecx = EcsContainer.WriteImage(image);
                var (exitCode, stdout, stderr) = CvmRunner.Run(vmBinary, ecx, "matrix-stub", workDir);
                Assert.That(exitCode, Is.EqualTo(0), $"C VM 降级执行；stderr={stderr}");
                Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(stubLines), "缺能力档双端锁步");

                // ---- ③ C VM strict：加载期恢复响亮（本镜像 IL+Vision 双缺位；IL 检查在前 → 13）----
                var (strictExit, _, strictErr) = CvmRunner.Run(vmBinary, ecx, "matrix-strict", workDir,
                    extraArgs: "--strict-caps");
                Assert.That(strictExit, Is.EqualTo(13), $"strict_caps 拒跑（ECS_ERR_IL 优先于 FEAT）；stderr={strictErr}");
            }

            // ---- ④ C# strict：运行期恢复响亮（首个 miss = NET_LOAD → ERR_NOSUCHNATIVE=10）----
            var strictHost = EcsTestHost.CreateRecording();
            strictHost.StrictCaps = true;
            Assert.That(EcxInterpreter.Run(image, strictHost),
                Is.EqualTo(EcxInterpreter.ERR_NOSUCHNATIVE), "C# strict 运行期响亮");

            // ---- ⑤ 全能力宿主（C# 侧存在形态）：语义不受降级层影响 ----
            var full = EcsTestHost.CreateRecording();
            full.Native = (name, _, ctx) => name switch
            {
                "NET_LOAD" => TaggedValue.FromInt(7),    // 会话句柄
                "NET_RUN" => TaggedValue.FromInt(1),     // 输出长度
                "NET_OUT" => TaggedValue.FromDouble(0.5),
                "JQ" => ctx.Str("hi"),
                _ => null,
            };
            full.ImgLabel = _ => 3;
            Assert.That(EcxInterpreter.Run(image, full), Is.EqualTo(0), "全能力宿主应正常执行");
            Assert.That(full.Lines, Is.EqualTo(new[] { "7", "1", "0.5", "hi", "", "0", "3" }),
                "全能力档语义不变（降级层不干预有处理器的调用）");
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }
}
