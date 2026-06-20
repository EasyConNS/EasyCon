using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EzCv.Interop;

/// <summary>
/// 跨平台原生库解析器。
/// <para>
/// 参考 OpenCvSharp NativeMethods.LoadLibraries 模式：
///   - Windows：通过 DllImportResolver 在输出目录/runtimes 中查找并加载 DLL。
///   - macOS / Linux：不干预库加载，交给系统动态链接器（dyld / ld.so）。
///     库放在输出目录中，依赖 @rpath（@loader_path）自动解析。
/// </para>
/// <para>
/// 注意：macOS 上 NativeLibrary.Load(path, assembly, searchPath) 会触发
/// 递归依赖加载（libopencv_world → AppKit/Cocoa/AVFoundation），在 .NET 进程
/// 上下文下可能导致 malloc 冲突。因此 Unix 上不注册 resolver，与 OpenCvSharp 一致。
/// </para>
/// </summary>
internal static class NativeLoader
{
    /// <summary>DllImport 中使用的逻辑库名。</summary>
    public const string EzCvLib = "ezcv_native";

    [ModuleInitializer]
    internal static void Init()
    {
        // 仅在 Windows 上注册 resolver；Unix 上交给系统处理（与 OpenCvSharp 一致）
        if (!OperatingSystem.IsWindows())
            return;

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
            var handle = TryLoadFromAppPaths(name);
            if (handle != IntPtr.Zero) return handle;
            try { handle = NativeLibrary.Load(name, assembly, searchPath); } catch { }
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    private static IntPtr TryLoadFromAppPaths(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var dirs = new[]
        {
            baseDir,
            Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"),
            Path.Combine(baseDir, "runtimes", ArchitectureFolder(), "native"),
        };

        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                if (NativeLibrary.TryLoad(path, out var handle))
                    return handle;
            }
            if (!OperatingSystem.IsWindows())
            {
                foreach (var found in GlobSamePrefix(dir, fileName))
                {
                    if (NativeLibrary.TryLoad(found, out var handle))
                        return handle;
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
        return ["libezcv_native.so"];
    }
}