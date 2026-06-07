using Avalonia;
using Avalonia.Media;

namespace EasyCon2.Avalonia.ViewModels;

public class FileTreeDisplayItem
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsRootDirectory { get; }
    public bool IsSubDirectory { get; }
    public Thickness IndentPadding { get; }
    public double DisplayFontSize { get; }
    public FontWeight DisplayFontWeight { get; }

    public FileTreeDisplayItem(string name, string fullPath, bool isDirectory, int indent)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsRootDirectory = isDirectory && indent == 0;
        IsSubDirectory = isDirectory && !IsRootDirectory;
        IndentPadding = new Thickness(indent * 16, 3, 8, 3);
        DisplayFontSize = IsRootDirectory ? 15 : 14;
        DisplayFontWeight = isDirectory ? FontWeight.SemiBold : FontWeight.Normal;
    }
}
