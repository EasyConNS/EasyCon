using System.Diagnostics;
using System.Text;

namespace EasyCon.Tests.Support;

/// <summary>
/// C VM 测试运行器——三份 fixture 拷贝（CvmCrossValidation / CorpusCrossValidation /
/// FullChainVerification）的单一事实源（docs/Pipeline.md）：
/// cc/native 目录环境探测、<see cref="EnsureBuilt"/> 现场编译 ecs-vm
/// （ci/build-vm.sh 同机制；无 cc 返回 null 由调用方 Ignore）、<see cref="Run"/>
/// 执行镜像采集 stdout/stderr、TSV 事件解析（分组语义与 EcxHost.EnableRecording 日志一致）。
/// </summary>
internal static class CvmRunner
{
    static string? _ccPath;

    public static string? FindCc()
    {
        // 环境变量显式指定优先（CI 可注入工具链路径）
        var env = Environment.GetEnvironmentVariable("ECSVM_CC");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        if (!OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[] { "/usr/bin/cc", "/usr/bin/clang", "/usr/bin/gcc" })
                if (File.Exists(candidate)) return candidate;
        }

        // PATH 逐目录扫描（覆盖 Windows 的 MinGW/LLVM/WinLibs 等安装）
        var names = OperatingSystem.IsWindows()
            ? new[] { "clang.exe", "gcc.exe", "cc.exe" }
            : new[] { "cc", "clang", "gcc" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var name in names)
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }

        // Windows 常见固定安装位置兜底
        if (OperatingSystem.IsWindows())
            foreach (var candidate in new[]
                     {
                         @"C:\Program Files\LLVM\bin\clang.exe",
                         @"C:\msys64\ucrt64\bin\gcc.exe",
                         @"C:\msys64\mingw64\bin\gcc.exe",
                         @"C:\Strawberry\c\bin\gcc.exe",
                         @"C:\ProgramData\chocolatey\bin\gcc.exe",
                     })
                if (File.Exists(candidate)) return candidate;

        return null;
    }

    public static string? FindNativeDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "EasyCon.Vm", "native");
            if (File.Exists(Path.Combine(candidate, "ecs_vm.c")))
                return candidate;
        }
        return null;
    }

    /// <summary>在 workDir 现场编译 ecs-vm（幂等：二进制已存在即复用）；无 cc 返回 null。</summary>
    public static string? EnsureBuilt(string workDir)
    {
        _ccPath ??= FindCc();
        if (_ccPath == null)
            return null;
        var nativeDir = FindNativeDir();
        Assert.That(nativeDir, Is.Not.Null, "未找到 ecs_vm.c 源目录");
        var binary = Path.Combine(workDir, "ecs-vm");
        if (File.Exists(binary))
            return binary;
        var psi = new ProcessStartInfo
        {
            FileName = _ccPath,
            Arguments = $"-O2 -std=c99 -Wall -Wextra -o \"{binary}\" " +
                        $"\"{Path.Combine(nativeDir!, "ecs_vm.c")}\" \"{Path.Combine(nativeDir!, "ecs_main.c")}\" -lm",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var build = Process.Start(psi)!;
        var buildErrTask = build.StandardError.ReadToEndAsync();   // 异步排空，避免管道写满互锁
        if (!build.WaitForExit(60000))
        {
            try { build.Kill(true); } catch { /* 已退出 */ }
            throw new TimeoutException("C VM 构建超时（60s）");
        }
        var buildErr = buildErrTask.Result;
        Assert.That(build.ExitCode, Is.EqualTo(0), "C VM 构建失败：" + buildErr);
        return binary;
    }

    /// <summary>执行镜像并采集 stdout（--print）/stderr（--trace 事件 TSV）；extraArgs 追加宿主旗标（如 --strict-caps）。</summary>
    public static (int ExitCode, string Stdout, string Stderr) Run(string vmBinary, byte[] ecx, string tag,
        string workDir, int timeoutMs = 120000, string? extraArgs = null)
    {
        var ecxPath = Path.Combine(workDir, $"{tag}.ecx");
        File.WriteAllBytes(ecxPath, ecx);
        var psi = new ProcessStartInfo
        {
            FileName = vmBinary,
            Arguments = $"run \"{ecxPath}\" --print --trace{(string.IsNullOrEmpty(extraArgs) ? "" : $" {extraArgs}")}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();   // 双流并发排空，避免 stderr TSV 写满管道互锁
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { /* 已退出 */ }
            p.WaitForExit(5000);
        }
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    /// <summary>stdout 行切分（约定以换行收尾，末空行不计为一行）。</summary>
    public static List<string> SplitLines(string stdout)
    {
        var lines = stdout.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>C stderr TSV 事件按标签分组并按存储序拼接
    /// （KEY → KEYST → STICK → STICKC → WAIT → AMIIBO → BEEP，与 EcsTestHost.AllEvents 同序）。</summary>
    public static List<string> ParseEventTsv(string stderr)
    {
        var all = new List<string>();
        foreach (var group in ParseEventLogs(stderr))
            all.AddRange(group);
        return all;
    }

    /// <summary>事件 TSV 按七个标签分组（组序 = EcxHost 日志属性序）。</summary>
    public static List<List<string>> ParseEventLogs(string stderr)
    {
        var byTag = new Dictionary<string, List<string>>();
        foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');
            var space = trimmed.IndexOf(' ');
            if (space <= 0) continue;
            var tag = trimmed[..space];
            if (!byTag.TryGetValue(tag, out var list))
                byTag[tag] = list = new List<string>();
            list.Add(trimmed);
        }
        return new List<List<string>>
        {
            byTag.GetValueOrDefault("KEY", new List<string>()),
            byTag.GetValueOrDefault("KEYST", new List<string>()),
            byTag.GetValueOrDefault("STICK", new List<string>()),
            byTag.GetValueOrDefault("STICKC", new List<string>()),
            byTag.GetValueOrDefault("WAIT", new List<string>()),
            byTag.GetValueOrDefault("AMIIBO", new List<string>()),
            byTag.GetValueOrDefault("BEEP", new List<string>()),
        };
    }

    /// <summary>事件 TSV 中指定标签的行（FullChain 逐标签对拍用；与分组语义一致）。</summary>
    public static List<string> ParseTsvByTag(string stderr, string tag)
        => stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith(tag + " ", StringComparison.Ordinal))
            .ToList();
}