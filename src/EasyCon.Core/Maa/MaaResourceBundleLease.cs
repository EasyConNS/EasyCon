#nullable enable
namespace EasyCon.Core.Maa;

/// <summary>原生会话拥有的短路径元数据/模型副本；图像经内存装载，不复制到此目录。</summary>
internal sealed class MaaResourceBundleLease : IDisposable
{
    private readonly string _parent;
    private readonly string _name = "ec-maa-" + Guid.NewGuid().ToString("N");
    private bool _disposed;
    public string Directory { get; }

    public MaaResourceBundleLease(string source)
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        Directory = Path.Combine(_parent, _name);
        if (System.IO.Directory.Exists(Directory)) throw new IOException("Maa 会话临时目录已存在");
        System.IO.Directory.CreateDirectory(Directory);
        try
        {
            foreach (string area in new[] { "pipeline", "model" })
            {
                string input = Path.Combine(source, area);
                if (!System.IO.Directory.Exists(input)) continue;
                foreach (string file in System.IO.Directory.GetFiles(input, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    string target = Path.Combine(Directory, area, Path.GetRelativePath(input, file));
                    if (OperatingSystem.IsWindows() && target.Length >= 240)
                        throw new InvalidDataException("Maa 原生会话目录过长，请将 TEMP 配置到较短的用户目录");
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        // 只递归删除本次分配的直接子目录，不删除来源 generation 或其他 TEMP 内容。
        string target = Path.GetFullPath(Directory);
        if (Path.GetDirectoryName(target) != _parent || Path.GetFileName(target) != _name)
            throw new InvalidOperationException("Maa 会话目录释放边界不符");
        if (System.IO.Directory.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Maa 会话目录被替换为重解析点");
            System.IO.Directory.Delete(target, true);
        }
        _disposed = true;
    }
}