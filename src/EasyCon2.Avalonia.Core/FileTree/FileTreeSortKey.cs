namespace EasyCon2.Avalonia.Core.FileTree;

public readonly record struct FileTreeSortKey(
    string Name,
    string FullPath,
    bool IsDirectory,
    string Extension,
    DateTime? LastWriteTimeUtc);