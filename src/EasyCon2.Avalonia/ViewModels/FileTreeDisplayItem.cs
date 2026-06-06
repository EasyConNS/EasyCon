using Avalonia;

namespace EasyCon2.Avalonia.ViewModels;

public class FileTreeDisplayItem
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public Thickness IndentPadding { get; }

    public FileTreeDisplayItem(string name, string fullPath, bool isDirectory, int indent)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IndentPadding = new Thickness(indent * 16, 3, 8, 3);
    }
}
