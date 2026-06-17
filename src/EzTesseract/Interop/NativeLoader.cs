using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EzTesseract.Interop;

/// <summary>
/// 跨平台原生库解析器。
/// <para>
/// [DllImport] 使用的逻辑库名（"tesseract" / "leptonica"）由本类在模块加载时
/// 通过 <see cref="NativeLibrary.SetDllImportResolver"/> 重写为各平台真实文件名
/// （如 libtesseract.so.5 / libtesseract.dylib / tesseract55.dll）。
/// </para>
/// <para>
/// 解析顺序：① 输出目录内 ② runtimes/&lt;rid&gt;/native/ ③ tools 内置库 ④ 系统兜底（NativeLibrary.Load 走 ldconfig/dyld）。
/// </para>
/// </summary>
internal static class NativeLoader
{
    /// <summary>DllImport 中使用的逻辑库名（resolver 据此分发到 tesseract/leptonica 候选名清单）。</summary>
    public const string TesseractLib = "tesseract";
    public const string LeptonicaLib = "leptonica";

    [ModuleInitializer]
    internal static void Init()
    {
        var asm = typeof(NativeLoader).Assembly;
        // 多次注册同一程序集会抛异常，幂等守卫
        try { NativeLibrary.SetDllImportResolver(asm, Resolve); }
        catch (ArgumentException) { /* 已注册，忽略 */ }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!Candidates.TryGetValue(libraryName, out var names))
            return IntPtr.Zero;

        foreach (var name in names)
        {
            // 1. 应用目录及子目录下按真实文件名查找
            var handle = TryLoadFromAppPaths(name, assembly, searchPath);
            if (handle != IntPtr.Zero) return handle;
            // 2. 系统兜底（自动处理 lib 前缀与版本号后缀）
            try { handle = NativeLibrary.Load(name, assembly, searchPath); } catch { }
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    private static IntPtr TryLoadFromAppPaths(string fileName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var baseDir = AppContext.BaseDirectory;
        // 候选目录：输出目录、runtimes/<rid>/native、tools 内置库
        var rid = RuntimeInformation.RuntimeIdentifier;
        var dirs = new[]
        {
            baseDir,
            Path.Combine(baseDir, "runtimes", rid, "native"),
            Path.Combine(baseDir, "runtimes", ArchitectureFolder(), "native"),
        };

        foreach (var dir in dirs)
        {
            // 精确文件名
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                try { return NativeLibrary.Load(path, assembly, searchPath); } catch { }
            }
            // Linux 下 soname 链：libtesseract.so.5 可能只存在 libtesseract.so.5.0.5，补充探测带版本号文件
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

    /// <summary>在同目录下查找同前缀（去掉扩展名后）的所有共享库文件，覆盖 libtesseract.so.5.0.5 这类带版本号的文件。</summary>
    private static IEnumerable<string> GlobSamePrefix(string dir, string fileName)
    {
        if (!Directory.Exists(dir)) yield break;
        var prefix = fileName;
        var dot = fileName.IndexOf('.');
        if (dot > 0) prefix = fileName[..dot];
        foreach (var f in Directory.EnumerateFiles(dir, prefix + "*"))
        {
            // 仅匹配共享库扩展形态（.so / .so.N[.N...] / .dylib / .dylib.N）
            if (f.EndsWith(".so", StringComparison.Ordinal) ||
                f.EndsWith(".dylib", StringComparison.Ordinal) ||
                f.Contains(".so.", StringComparison.Ordinal) ||
                f.Contains(".dylib.", StringComparison.Ordinal))
                yield return f;
        }
    }

    /// <summary>按平台列出候选真实库名。靠前的优先。</summary>
    private static readonly Dictionary<string, string[]> Candidates = new(StringComparer.Ordinal)
    {
        [TesseractLib] = TesseractCandidates(),
        [LeptonicaLib] = LeptonicaCandidates(),
    };

    private static string[] TesseractCandidates()
    {
        if (OperatingSystem.IsWindows())
            return ["tesseract55.dll", "tesseract50.dll", "libtesseract.dll"];
        if (OperatingSystem.IsMacOS())
            return ["libtesseract.5.dylib", "libtesseract.dylib", "libtesseract.4.dylib"];
        // Linux：优先输出目录里的 libtesseract.so（内置复制），其次系统库
        return ["libtesseract.so", "libtesseract.so.5", "libtesseract.so.5.0.5", "libtesseract.so.4"];
    }

    private static string[] LeptonicaCandidates()
    {
        if (OperatingSystem.IsWindows())
            return ["leptonica-1.85.0.dll", "leptonica-1.82.0.dll", "liblept.dll", "libleptonica.dll"];
        if (OperatingSystem.IsMacOS())
            return ["liblept.5.dylib", "liblept.dylib", "libleptonica.dylib"];
        // Linux：优先输出目录 liblept.so（若有内置），其次系统库
        return ["liblept.so", "liblept.so.5", "libleptonica.so"];
    }
}
