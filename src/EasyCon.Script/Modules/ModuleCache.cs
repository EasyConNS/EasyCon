using EasyCon.Script.Bytecode;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace EasyCon.Script.Modules;

/// <summary>
/// 模块缓存键与 obj/ 磁盘缓存（docs/ModuleSystem.md §7）。
///
/// cacheKey = SHA256(源码内容 ⊕ Σ直接依赖接口哈希 ⊕ 编译器版本 ⊕ 影响产物的编译选项)（§7.2）。
/// 进键的选项集中在 CompileOptions.ProductFingerprint()（当前：Optimize/LegacySyntax/ExtVars）；
/// KeepSsa 不影响序列化产物、UseDiskCache/ObjDir/GcMaxAge 与产物内容无关，均不进键。
/// 文件名 = &lt;模块名&gt;-&lt;cacheKey 前 8 位&gt;.ecm；实现体改动 → 源码变 → 新键新文件，
/// 接口未变时下游 cacheKey 不变 → 下游缓存全命中（Merkle 失效模型，§2.3）。
/// 过期文件成为垃圾，由 §7.3 GC 处理（引用闭包外 + mtime 阈值）。
/// </summary>
internal static class ModuleCacheKeys
{
    /// <summary>规范化排序后的外部变量集（ExtVars 集合序不定，进键前排序保证键稳定）。</summary>
    public static string ExtVarsKey(ImmutableHashSet<string>? extVars)
    {
        if (extVars == null || extVars.IsEmpty)
            return "";
        return string.Join("\u0001", extVars.OrderBy(v => v, StringComparer.Ordinal));
    }

    public static string Compute(string source, IReadOnlyList<string> depInterfaceHashes, string compilerVersion,
        string productFingerprint = "")
    {
        var sb = new StringBuilder();
        AppendItem(sb, source);
        foreach (var h in depInterfaceHashes)
            AppendItem(sb, h);
        AppendItem(sb, compilerVersion);
        AppendItem(sb, productFingerprint);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            hex.Append(b.ToString("x2"));
        return hex.ToString();
    }

    public static string FileName(string moduleName, string cacheKey)
        => $"{moduleName}-{cacheKey[..8]}.ecm";

    /// <summary>错误缓存 sidecar 文件名（§7.3 错误重放）：同 cacheKey 的编译失败记录。</summary>
    public static string ErrorFileName(string moduleName, string cacheKey)
        => $"{moduleName}-{cacheKey[..8]}.err";

    /// <summary>长度前缀规范项（防拼接歧义）。</summary>
    static void AppendItem(StringBuilder sb, string s)
        => sb.Append(s.Length).Append(':').Append(s);
}

/// <summary>
/// 进程级产物缓存（统一链路阶段 B）：仅作用于 UseDiskCache=false 的桌面现编路径——
/// 同源码 ⊕ 同依赖接口 ⊕ 同选项的模块免重编（std/vision 等大模块二次编译近零开销）。
/// 存 EcmFormat 序列化字节，命中即反序列化出新实例——杜绝调用方对产物的就地修改串味
/// （ModuleInterfaceTests 曾就地改产物字段，实例绝不共享）。与磁盘缓存正交（不读写 obj/）。
/// </summary>
internal static class ProcessModuleCache
{
    static readonly ConcurrentDictionary<string, byte[]> Entries = new();

    public static int Hits { get; private set; }
    public static int Misses { get; private set; }

    public static ModuleArtifact? TryLoad(string moduleName, string cacheKey)
    {
        if (!Entries.TryGetValue(ModuleCacheKeys.FileName(moduleName, cacheKey), out var bytes))
        {
            Misses++;
            return null;
        }
        try
        {
            var artifact = EcmFormat.Read(bytes);
            if (!artifact.IntegrityCheck())
            {
                Misses++;   // 损坏：按未命中重编
                return null;
            }
            Hits++;
            return artifact;
        }
        catch (BytecodeException)
        {
            Misses++;
            return null;
        }
    }

    public static void Store(ModuleArtifact artifact, string cacheKey)
    {
        try
        {
            Entries[ModuleCacheKeys.FileName(artifact.Name, cacheKey)] = EcmFormat.Write(artifact);
        }
        catch (BytecodeException)
        {
            // 序列化失败（不应发生）：进程缓存降级为未命中
        }
    }

    /// <summary>测试隔离用：清空全部条目与统计。</summary>
    public static void Clear()
    {
        Entries.Clear();
        Hits = 0;
        Misses = 0;
    }
}

/// <summary>
/// obj/ 目录的模块产物存取（§7.2/§7.3）：读时完整性校验；写走 temp+rename 原子替换；
/// 编译失败的诊断持久化为 .err sidecar（错误缓存），修复后随新 cacheKey 自然失效。
/// </summary>
internal sealed class ModuleCache
{
    readonly string _objDir;

    public ModuleCache(string objDir)
    {
        _objDir = objDir;
        Directory.CreateDirectory(objDir);
    }

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    /// <summary>错误缓存命中次数（编译失败记录重放）。</summary>
    public int ErrorHits { get; private set; }

    /// <summary>按 cacheKey 定位并加载模块产物；命中后做完整性校验（接口哈希自洽）。</summary>
    public ModuleArtifact? TryLoad(string moduleName, string cacheKey)
    {
        var path = Path.Combine(_objDir, ModuleCacheKeys.FileName(moduleName, cacheKey));
        if (!File.Exists(path))
        {
            Misses++;
            return null;
        }
        try
        {
            var artifact = EcmFormat.Read(File.ReadAllBytes(path));
            if (!artifact.IntegrityCheck())
            {
                Misses++;   // 损坏/被篡改：按未命中重编
                return null;
            }
            Hits++;
            return artifact;
        }
        catch (BytecodeException)
        {
            Misses++;
            return null;
        }
    }

    /// <summary>错误缓存重放（§7.3）：同 cacheKey 的历史编译失败诊断，命中即快速失败。</summary>
    public List<string>? TryLoadErrors(string moduleName, string cacheKey)
    {
        var path = Path.Combine(_objDir, ModuleCacheKeys.ErrorFileName(moduleName, cacheKey));
        if (!File.Exists(path))
            return null;
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            return null;
        ErrorHits++;
        return lines;
    }

    /// <summary>写入错误缓存（编译失败不写 .ecm，只记诊断）。</summary>
    public void StoreErrors(string moduleName, string cacheKey, IReadOnlyList<string> diagnostics)
    {
        var path = Path.Combine(_objDir, ModuleCacheKeys.ErrorFileName(moduleName, cacheKey));
        File.WriteAllLines(path, diagnostics);
    }

    /// <summary>原子写入产物；同键的历史错误 sidecar 一并清除。</summary>
    public void Store(ModuleArtifact artifact, string cacheKey)
    {
        var bytes = EcmFormat.Write(artifact);
        var finalPath = Path.Combine(_objDir, ModuleCacheKeys.FileName(artifact.Name, cacheKey));
        var tempPath = Path.Combine(_objDir, $".{artifact.Name}-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, finalPath, overwrite: true);

        var errPath = Path.Combine(_objDir, ModuleCacheKeys.ErrorFileName(artifact.Name, cacheKey));
        if (File.Exists(errPath))
            File.Delete(errPath);
    }

    /// <summary>
    /// 缓存 GC（§7.3）：删除引用闭包之外且 mtime 早于 &lt;maxAge&gt; 的产物/错误文件。
    /// 返回删除的文件数。当前运行仍在引用的文件（keep）永不删除。
    /// </summary>
    public int GarbageCollect(IReadOnlySet<string> keepFileNames, TimeSpan maxAge)
    {
        int removed = 0;
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var file in Directory.EnumerateFiles(_objDir))
        {
            var ext = Path.GetExtension(file);
            if (ext is not (".ecm" or ".err"))
                continue;
            if (keepFileNames.Contains(Path.GetFileName(file)))
                continue;
            if (File.GetLastWriteTimeUtc(file) > cutoff)
                continue;
            File.Delete(file);
            removed++;
        }
        return removed;
    }
}