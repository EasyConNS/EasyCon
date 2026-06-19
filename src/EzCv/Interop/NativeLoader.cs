using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EzCv.Interop;

/// <summary>
/// 跨平台原生库解析器。
/// <para>
/// [DllImport] 使用的逻辑库名（"ezcv_native"）由本类在模块加载时
/// 通过 <see cref="NativeLibrary.SetDllImportResolver"/> 重写为各平台真实文件名
/// （如 libezcv_native.dylib / libezcv_native.so / ezcv_native.dll）。
/// </para>
/// <para>
/// 解析顺序：① 输出目录内 ② runtimes/&lt;rid&gt;/native/ ③ 系统兜底（NativeLibrary.Load 走 ldconfig/dyld）。
/// </para>
/// </summary>
internal static class NativeLoader
{
    /// <summary>DllImport 中使用的逻辑库名。</summary>
    public const string EzCvLib = "ezcv_native";

    [ModuleInitializer]
    internal static void Init()
    {
        var asm = typeof(NativeLoader).Assembly;
        try { NativeLibrary.SetDllImportResolver(asm, Resolve); }
        catch (ArgumentException) { /* 已注册，忽略 */ }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != EzCvLib)
            return IntPtr.Zero;

        var names = Candidates();
        foreach (var name in names)
        {
            var handle = TryLoadFromAppPaths(name, assembly, searchPath);
            if (handle != IntPtr.Zero) return handle;
            try { handle = NativeLibrary.Load(name, assembly, searchPath); } catch { }
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    private static IntPtr TryLoadFromAppPaths(string fileName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var baseDir = AppContext.BaseDirectory;
        var rid = RuntimeInformation.RuntimeIdentifier;
        var dirs = new[]
        {
            baseDir,
            Path.Combine(baseDir, "runtimes", rid, "native"),
            Path.Combine(baseDir, "runtimes", ArchitectureFolder(), "native"),
        };

        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                try { return NativeLibrary.Load(path, assembly, searchPath); } catch { }
            }
            // Unix 下 soname 链：补充探测带版本号文件
            if (!OperatingSystem.IsWindows())
            {
                foreach (var found in GlobSamePrefix(dir, fileName))
                {
                    try { return NativeLibrary.Load(found, assembly, searchPath); } catch { }
                }
            }
        }
        return IntPtr.Zero;
    }

    private static string ArchitectureFolder() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    private static IEnumerable<string> GlobSamePrefix(string dir, string fileName)
    {
        if (!Directory.Exists(dir)) yield break;
        var prefix = fileName;
        var dot = fileName.IndexOf('.');
        if (dot > 0) prefix = fileName[..dot];
        foreach (var f in Directory.EnumerateFiles(dir, prefix + "*"))
        {
            if (f.EndsWith(".so", StringComparison.Ordinal) ||
                f.EndsWith(".dylib", StringComparison.Ordinal) ||
                f.Contains(".so.", StringComparison.Ordinal) ||
                f.Contains(".dylib.", StringComparison.Ordinal))
                yield return f;
        }
    }

    private static string[] Candidates()
    {
        if (OperatingSystem.IsWindows())
            return ["ezcv_native.dll"];
        if (OperatingSystem.IsMacOS())
            return ["libezcv_native.dylib"];
        // Linux
        return ["libezcv_native.so"];
    }
}