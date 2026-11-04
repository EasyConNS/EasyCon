using System.Text;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 文件存储端口：整文件字节/文本读写的最小 System.IO 面。
/// 成员与对应 System.IO API 语义逐一对应，不做增强；生产用 <see cref="LocalFileStorage"/>，测试可注入替身。
/// 目录/存在性/枚举等能力复用 <see cref="IFileSystem"/>；注意与 EasyCon.Core/Capabilities/IFileSystem.cs（句柄语义）无任何关系。
/// </summary>
public interface IFileStorage
{
    /// <summary>对应 File.ReadAllBytes(path)。</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>对应 File.ReadAllText(path)。</summary>
    string ReadAllText(string path);

    /// <summary>对应 File.WriteAllText(path, contents, encoding)。</summary>
    void WriteAllText(string path, string contents, Encoding encoding);
}