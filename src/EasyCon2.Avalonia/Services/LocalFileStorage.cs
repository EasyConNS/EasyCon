using System.Text;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// <see cref="IFileStorage"/> 的默认实现：纯 System.IO 转发，语义逐一对应，不做增强。
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents, Encoding encoding) => File.WriteAllText(path, contents, encoding);
}