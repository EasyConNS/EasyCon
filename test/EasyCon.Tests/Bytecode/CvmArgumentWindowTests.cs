using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using System.Diagnostics;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public class CvmArgumentWindowTests
{
    string _directory = null!;
    string _vm = null!;
    string _nativeProbe = null!;

    [OneTimeSetUp]
    public void BuildVms()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"EasyConCArguments_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        string? vm = CvmRunner.EnsureBuilt(_directory);
        if (vm == null)
        {
            Assert.Ignore("无 C99 编译器，跳过 C VM 参数窗回归");
            return;
        }
        _vm = vm;
        _nativeProbe = Path.Combine(_directory, OperatingSystem.IsWindows() ? "native-probe.exe" : "native-probe");
        string harness = Path.Combine(_directory, "native-probe.c");
        File.WriteAllText(harness, """
            #include "ecs_vm.h"
            #include <stdio.h>
            #include <stdlib.h>

            static int probe(void *ud, const char *name, ecs_value *args, int32_t nargs, ecs_value *ret)
            {
                (void)ud; (void)name;
                int32_t sum = 0;
                for (int32_t i = 0; i < nargs; i++)
                {
                    if (args[i].tag != ECS_INT || args[i].i32 != i + 1) return 1;
                    sum += args[i].i32 * (i + 1);
                }
                *ret = (ecs_value){0};
                ret->tag = ECS_INT; ret->i32 = sum;
                printf("%d:%d\n", (int)nargs, (int)sum);
                return 0;
            }

            int main(int argc, char **argv)
            {
                if (argc < 3) return 1;
                FILE *file = fopen(argv[2], "rb");
                if (!file) return 2;
                fseek(file, 0, SEEK_END);
                long size = ftell(file);
                rewind(file);
                if (size <= 0) { fclose(file); return 3; }
                uint8_t *image = (uint8_t *)malloc((size_t)size);
                if (!image) { fclose(file); return 4; }
                if (fread(image, 1, (size_t)size, file) != (size_t)size)
                { fclose(file); free(image); return 5; }
                fclose(file);
                ecs_host host = {0};
                host.feats = ECS_FEAT_FFI; host.native = probe;
                ecs_vm *vm = ecs_vm_new(&host);
                if (!vm) { free(image); return 6; }
                int rc = ecs_vm_load(vm, image, (size_t)size);
                if (rc == ECS_OK) rc = ecs_vm_run(vm);
                ecs_vm_free(vm);
                free(image);
                return rc;
            }
            """);
        string nativeDirectory = CvmRunner.FindNativeDir()!;
        ProcessStartInfo start = new(CvmRunner.FindCc()!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-O2", "-std=c99", "-Wall", "-Wextra", "-I", nativeDirectory,
            harness, Path.Combine(nativeDirectory, "ecs_vm.c"), "-lm", "-o", _nativeProbe })
            start.ArgumentList.Add(argument);
        using Process build = Process.Start(start)!;
        Task<string> stdout = build.StandardOutput.ReadToEndAsync();
        Task<string> stderr = build.StandardError.ReadToEndAsync();
        if (!build.WaitForExit(60000))
        {
            build.Kill(true);
            Assert.Fail("C VM 原生参数探针构建超时");
        }
        Assert.That(build.ExitCode, Is.EqualTo(0), stdout.Result + stderr.Result);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [TestCase(8)]
    [TestCase(16)]
    [TestCase(64)]
    public void UserCall_WithManyArguments_MatchesInterpreter(int count)
    {
        string source = ArgumentWindowTests.WeightedFunction(count) +
            $"$r = weighted({ArgumentWindowTests.Arguments(count)})\nPRINT $r\n";
        // 保留真实 Call 指令，避免全常量调用被纯函数求值折叠后绕过 C VM 参数复制路径。
        CompileResult result = ArgumentWindowTests.Compile(source, optimize: false);
        Assert.That(result.Image!.Functions.SelectMany(f => f.Instructions)
            .Any(i => i.Op == EcsOpcode.Call && i.B == count), Is.True, "必须执行真实的多参数 Call");
        EcxHost host = new();
        host.EnableRecording();
        Assert.That(EcxInterpreter.Run(result.Image!, host), Is.EqualTo(EcxInterpreter.OK));
        byte[] image = EcsContainer.WriteImage(result.Image!, stripDebug: true);
        (int exit, string stdout, string stderr) = CvmRunner.Run(_vm, image, $"user-{count}", _directory);
        Assert.That(exit, Is.EqualTo(0), stderr);
        Assert.That(CvmRunner.SplitLines(stdout), Is.EqualTo(host.Lines));
        Assert.That(host.Lines, Is.EqualTo(new[] { Enumerable.Range(1, count).Sum(i => i * i).ToString() }));
    }

    [TestCase(8)]
    [TestCase(16)]
    [TestCase(64)]
    public void NativeCall_WithManyArguments_ReceivesCompleteSnapshot(int count)
    {
        string source = $"EXTERN FUNC probe({ArgumentWindowTests.Parameters(count)}):INT FROM \"argument-probe\"\n" +
            $"RETURN probe({ArgumentWindowTests.Arguments(count)})\n";
        CompileResult result = ArgumentWindowTests.Compile(source);
        (int exit, string stdout, string stderr) = CvmRunner.Run(_nativeProbe,
            EcsContainer.WriteImage(result.Image!, stripDebug: true), $"native-{count}", _directory);
        Assert.That(exit, Is.EqualTo(0), stderr);
        Assert.That(CvmRunner.SplitLines(stdout),
            Is.EqualTo(new[] { $"{count}:{Enumerable.Range(1, count).Sum(i => i * i)}" }));
    }
}