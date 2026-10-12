using System.Text;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// <see cref="IFileSystem"/> 的默认实现：纯 System.IO 转发，语义逐一对应，不做增强。
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    public static LocalFileSystem Instance { get; } = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    public void DeleteFile(string path) => File.Delete(path);

    public IReadOnlyList<string> GetDirectories(string path) => Directory.GetDirectories(path);

    public IReadOnlyList<string> GetFiles(string path) => Directory.GetFiles(path);

    public DateTime? GetLastWriteTimeUtc(string path)
    {
        try
        {
            FileInfo fileInfo = new(path);
            fileInfo.Refresh();
            return fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null;
        }
        catch
        {
            return null;
        }
    }

    public void Move(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);

    public void MoveFile(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);

    public void CopyFile(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);

    public void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            var targetChildDirectory = Path.Combine(targetDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, targetChildDirectory);
        }
    }

    public void WriteAllText(string path, string contents, Encoding encoding) => File.WriteAllText(path, contents, encoding);

    public string GetFileName(string path) => Path.GetFileName(path);

    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    public string PathCombine(params string[] parts) => Path.Combine(parts);

    public string GetFileExtension(string path) => Path.GetExtension(path);

    public bool ArePathsEqual(string left, string right)
    {
        // 自 FileTreeViewModel 原有的 PathComparer 逻辑原样搬入
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return comparer.Equals(left, right);
    }
}