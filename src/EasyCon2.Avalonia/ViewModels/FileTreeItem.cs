using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.ViewModels;

public class FileTreeItem
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileTreeItem> Children { get; } = [];

    public FileTreeItem(string name, string fullPath, bool isDirectory)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
    }
}