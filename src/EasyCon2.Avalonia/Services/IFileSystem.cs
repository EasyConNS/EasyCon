using System.Text;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 文件系统端口：FileTreeViewModel 所需的最小 System.IO 面。
/// 成员与对应 System.IO API 语义逐一对应，不做增强；生产用 <see cref="LocalFileSystem"/>，测试可注入替身。
/// 注意与 EasyCon.Core/Capabilities/IFileSystem.cs（句柄语义）无任何关系，仅同名。
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    void CreateDirectory(string path);

    /// <summary>递归删除目录（对应 Directory.Delete(path, recursive: true)）。</summary>
    void DeleteDirectory(string path);

    void DeleteFile(string path);

    /// <summary>对应 Directory.GetDirectories(path)，返回直接子目录全路径。</summary>
    IReadOnlyList<string> GetDirectories(string path);

    /// <summary>对应 Directory.GetFiles(path)，返回直接子文件全路径。</summary>
    IReadOnlyList<string> GetFiles(string path);

    /// <summary>取最后写入时间（UTC）；不存在或读取失败返回 null。</summary>
    DateTime? GetLastWriteTimeUtc(string path);

    /// <summary>目录移动（对应 Directory.Move）。</summary>
    void Move(string sourcePath, string destinationPath);

    /// <summary>文件移动（对应 File.Move）。</summary>
    void MoveFile(string sourcePath, string destinationPath);

    void CopyFile(string source, string destination, bool overwrite);

    /// <summary>递归拷贝整个目录（源实现自 FileTreeViewModel 原样搬入）。</summary>
    void CopyDirectory(string source, string target);

    void WriteAllText(string path, string contents, Encoding encoding);

    string GetFileName(string path);

    string? GetDirectoryName(string path);

    string PathCombine(params string[] parts);

    /// <summary>取扩展名（保留原大小写，对应 Path.GetExtension）。</summary>
    string GetFileExtension(string path);

    /// <summary>路径相等判断：Windows 用 OrdinalIgnoreCase，其余用 Ordinal。</summary>
    bool ArePathsEqual(string left, string right);
}
