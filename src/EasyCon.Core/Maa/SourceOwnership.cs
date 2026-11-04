#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace EasyCon.Core.Maa;

/// <summary>进程间独占来源。句柄随实际读取/写入所有者释放，不使用有线程亲和性的 Mutex。</summary>
internal sealed class SourceOwnership : IDisposable
{
    private readonly FileStream _file;

    public SourceOwnership(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "EasyCon", "ownership");
        Directory.CreateDirectory(root);
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        try
        {
            _file = new FileStream(Path.Combine(root, name + ".lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new IOException($"busy: 来源已由另一进程占用 ({source})", ex);
        }
    }

    public void Dispose() => _file.Dispose();
}