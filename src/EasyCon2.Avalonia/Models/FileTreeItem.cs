using EasyCon2.Avalonia.Core.FileTree;
using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.Models;

public class FileTreeItem
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileTreeItem> Children { get; } = [];
    public FileTreeSortKey SortKey { get; private set; }

    public FileTreeItem(string name, string fullPath, bool isDirectory, FileTreeSortKey? sortKey = null)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        SortKey = sortKey ?? new FileTreeSortKey(
            name,
            fullPath,
            isDirectory,
            isDirectory ? string.Empty : Path.GetExtension(name),
            null);
    }

    public void UpdateLastWriteTimeUtc(DateTime? lastWriteTimeUtc)
    {
        SortKey = SortKey with { LastWriteTimeUtc = lastWriteTimeUtc };
    }
}