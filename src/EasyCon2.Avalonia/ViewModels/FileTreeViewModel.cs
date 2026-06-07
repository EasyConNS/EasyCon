using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace EasyCon2.Avalonia.ViewModels;

public partial class FileTreeViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<FileTreeItem> _rootItems = [];

    [ObservableProperty]
    private ObservableCollection<FileTreeDisplayItem> _flatItems = [];

    [ObservableProperty]
    private FileTreeDisplayItem? _selectedFlatItem;

    [ObservableProperty]
    private bool _hasLoadedDirectory = false;

    private readonly HashSet<string> _expandedDirs = [];
    private string? _normalDirectoryPath;

    public event Action<string>? FileActivated;
    public event Action? OpenProjectRequested;

    public void LoadDirectory(string? directoryPath)
    {
        _normalDirectoryPath = directoryPath;
        LoadDirectoryCore(directoryPath);
    }

    public void ShowNormalTree()
    {
        LoadDirectoryCore(_normalDirectoryPath);
    }

    public void ShowImgLabelTree(string? baseDirectoryPath)
    {
        RootItems.Clear();
        _expandedDirs.Clear();
        FlatItems.Clear();
        SelectedFlatItem = null;

        var labelDirectoryPath = string.IsNullOrWhiteSpace(baseDirectoryPath)
            ? "ImgLabel"
            : Path.Combine(baseDirectoryPath, "ImgLabel");

        var root = new FileTreeItem("ImgLabel", labelDirectoryPath, true);
        if (Directory.Exists(labelDirectoryPath))
            LoadChildren(root, depth: 0, maxDepth: 3);

        RootItems.Add(root);
        _expandedDirs.Add(root.FullPath);
        RebuildFlatList();
        HasLoadedDirectory = true;
    }

    private void LoadDirectoryCore(string? directoryPath)
    {
        RootItems.Clear();
        _expandedDirs.Clear();
        FlatItems.Clear();
        SelectedFlatItem = null;

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            HasLoadedDirectory = false;
            return;
        }

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            var root = new FileTreeItem(dirInfo.Name, directoryPath, true);
            LoadChildren(root, depth: 0, maxDepth: 3);
            RootItems.Add(root);

            // 根目录默认展开
            _expandedDirs.Add(root.FullPath);
            RebuildFlatList();
            HasLoadedDirectory = true;
        }
        catch
        {
            HasLoadedDirectory = false;
        }
    }

    /// <summary>
    /// 切换目录展开/折叠，或打开文件。
    /// </summary>
    [RelayCommand]
    private void ItemClick(FileTreeDisplayItem? item)
    {
        if (item == null) return;

        if (item.IsDirectory)
        {
            if (_expandedDirs.Contains(item.FullPath))
                _expandedDirs.Remove(item.FullPath);
            else
                _expandedDirs.Add(item.FullPath);
            RebuildFlatList();
        }
        else
        {
            FileActivated?.Invoke(item.FullPath);
        }
    }

    [RelayCommand]
    private void OpenProject()
    {
        OpenProjectRequested?.Invoke();
    }

    private void RebuildFlatList()
    {
        FlatItems.Clear();
        if (RootItems.Count == 0) return;
        FlattenItem(RootItems[0], 0);
    }

    private void FlattenItem(FileTreeItem item, int indent)
    {
        FlatItems.Add(new FileTreeDisplayItem(item.Name, item.FullPath, item.IsDirectory, indent));
        if (item.IsDirectory && _expandedDirs.Contains(item.FullPath))
        {
            foreach (var child in item.Children)
                FlattenItem(child, indent + 1);
        }
    }

    private static void LoadChildren(FileTreeItem parent, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(parent.FullPath)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.')) continue;

                var child = new FileTreeItem(dirName, dir, true);
                LoadChildren(child, depth + 1, maxDepth);
                parent.Children.Add(child);
            }

            foreach (var file in Directory.GetFiles(parent.FullPath)
                         .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith('.')) continue;

                parent.Children.Add(new FileTreeItem(fileName, file, false));
            }
        }
        catch
        {
            // 权限不足等异常静默忽略
        }
    }
}
