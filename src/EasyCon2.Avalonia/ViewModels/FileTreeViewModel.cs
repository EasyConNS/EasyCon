using Avalonia.Controls;
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

    [ObservableProperty]
    private bool _areAllDirectoriesExpanded = false;

    private readonly HashSet<string> _expandedDirs = [];
    private string? _normalDirectoryPath;

    public event Action<string>? FileActivated;
    public event Action? OpenProjectRequested;
    public event Action? NewScriptRequested;
    public event Action<Window?>? OpenScriptRequested;
    public event Action<Window?>? OpenProjectFolderRequested;
    public event Action<Window?>? SaveScriptRequested;
    public event Action<Window?>? SaveScriptAsRequested;
    public event Action? CloseProjectRequested;
    public event Action<string>? FileOperationMessage;

    private string? _clipboardPath;
    private bool _isClipboardCut;

    public void LoadDirectory(string? directoryPath)
    {
        _normalDirectoryPath = directoryPath;
        LoadDirectoryCore(directoryPath);
    }

    public void ShowNormalTree()
    {
        LoadDirectoryCore(_normalDirectoryPath);
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
            RebuildFlatList(item.FullPath);
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

    [RelayCommand]
    private void NewScript()
    {
        NewScriptRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenScript(Window? window)
    {
        OpenScriptRequested?.Invoke(window);
    }

    [RelayCommand]
    private void OpenProjectFolder(Window? window)
    {
        OpenProjectFolderRequested?.Invoke(window);
    }

    [RelayCommand]
    private void SaveScript(Window? window)
    {
        SaveScriptRequested?.Invoke(window);
    }

    [RelayCommand]
    private void SaveScriptAs(Window? window)
    {
        SaveScriptAsRequested?.Invoke(window);
    }

    [RelayCommand]
    private void CloseProject()
    {
        CloseProjectRequested?.Invoke();
    }

    [RelayCommand]
    private void CreateFile()
    {
        var directoryPath = GetSelectedTargetDirectory();
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;

        try
        {
            Directory.CreateDirectory(directoryPath);
            var filePath = GetUniquePath(directoryPath, "新建文件", ".txt");
            File.WriteAllText(filePath, string.Empty);
            _expandedDirs.Add(directoryPath);
            ReloadCurrentRootPreservingState(filePath);
            FileActivated?.Invoke(filePath);
            FileOperationMessage?.Invoke($"已新建文件: {filePath}");
        }
        catch (Exception ex)
        {
            FileOperationMessage?.Invoke($"新建文件失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CreateFolder()
    {
        var directoryPath = GetSelectedTargetDirectory();
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;

        try
        {
            Directory.CreateDirectory(directoryPath);
            var folderPath = GetUniquePath(directoryPath, "新建文件夹", string.Empty);
            Directory.CreateDirectory(folderPath);
            _expandedDirs.Add(directoryPath);
            _expandedDirs.Add(folderPath);
            ReloadCurrentRootPreservingState(folderPath);
            FileOperationMessage?.Invoke($"已新建文件夹: {folderPath}");
        }
        catch (Exception ex)
        {
            FileOperationMessage?.Invoke($"新建文件夹失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshTree()
    {
        ReloadCurrentRootPreservingState(SelectedFlatItem?.FullPath);
    }

    [RelayCommand]
    private void ToggleExpandCollapseAll()
    {
        if (RootItems.Count == 0)
            return;

        if (AreAllDirectoriesExpanded)
        {
            _expandedDirs.Clear();
            _expandedDirs.Add(RootItems[0].FullPath);
        }
        else
        {
            _expandedDirs.Clear();
            foreach (var directory in EnumerateDirectories(RootItems[0]))
                _expandedDirs.Add(directory.FullPath);
        }

        RebuildFlatList(SelectedFlatItem?.FullPath);
    }

    [RelayCommand(CanExecute = nameof(CanCopySelectedItem))]
    private void CopySelectedItem()
    {
        if (SelectedFlatItem == null)
            return;

        _clipboardPath = SelectedFlatItem.FullPath;
        _isClipboardCut = false;
        NotifyFileOperationCommands();
        FileOperationMessage?.Invoke($"已复制: {_clipboardPath}");
    }

    [RelayCommand(CanExecute = nameof(CanCutSelectedItem))]
    private void CutSelectedItem()
    {
        if (SelectedFlatItem == null)
            return;

        _clipboardPath = SelectedFlatItem.FullPath;
        _isClipboardCut = true;
        NotifyFileOperationCommands();
        FileOperationMessage?.Invoke($"已剪切: {_clipboardPath}");
    }

    [RelayCommand(CanExecute = nameof(CanPasteItem))]
    private void PasteItem()
    {
        if (string.IsNullOrWhiteSpace(_clipboardPath) || !PathExists(_clipboardPath))
            return;

        var targetDirectory = GetSelectedTargetDirectory();
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            return;

        try
        {
            var sourcePath = _clipboardPath;
            var sourceIsDirectory = Directory.Exists(sourcePath);
            if (sourceIsDirectory && IsPathInsideDirectory(targetDirectory, sourcePath))
            {
                FileOperationMessage?.Invoke("无法粘贴到自身或子目录");
                return;
            }

            var targetPath = GetUniqueItemPath(targetDirectory, sourcePath);
            if (_isClipboardCut)
            {
                if (sourceIsDirectory)
                    Directory.Move(sourcePath, targetPath);
                else
                    File.Move(sourcePath, targetPath);

                _clipboardPath = null;
                _isClipboardCut = false;
            }
            else
            {
                if (sourceIsDirectory)
                    CopyDirectory(sourcePath, targetPath);
                else
                    File.Copy(sourcePath, targetPath);
            }

            _expandedDirs.Add(targetDirectory);
            ReloadCurrentRootPreservingState(targetPath);
            FileOperationMessage?.Invoke($"已粘贴: {targetPath}");
        }
        catch (Exception ex)
        {
            FileOperationMessage?.Invoke($"粘贴失败: {ex.Message}");
        }
        finally
        {
            NotifyFileOperationCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedItem))]
    private void DeleteSelectedItem()
    {
        if (SelectedFlatItem == null)
            return;

        try
        {
            var path = SelectedFlatItem.FullPath;
            var parentDirectory = Path.GetDirectoryName(path);

            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
            else
                return;

            if (string.Equals(_clipboardPath, path, StringComparison.Ordinal))
            {
                _clipboardPath = null;
                _isClipboardCut = false;
            }

            ReloadCurrentRootPreservingState(parentDirectory);
            FileOperationMessage?.Invoke($"已删除: {path}");
        }
        catch (Exception ex)
        {
            FileOperationMessage?.Invoke($"删除失败: {ex.Message}");
        }
        finally
        {
            NotifyFileOperationCommands();
        }
    }

    private bool CanCopySelectedItem()
    {
        return SelectedFlatItem != null && PathExists(SelectedFlatItem.FullPath);
    }

    private bool CanCutSelectedItem()
    {
        return SelectedFlatItem != null
            && !SelectedFlatItem.IsRootDirectory
            && PathExists(SelectedFlatItem.FullPath);
    }

    private bool CanPasteItem()
    {
        if (string.IsNullOrWhiteSpace(_clipboardPath) || !PathExists(_clipboardPath))
            return false;

        var targetDirectory = GetSelectedTargetDirectory();
        return !string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory);
    }

    private bool CanDeleteSelectedItem()
    {
        return SelectedFlatItem != null
            && !SelectedFlatItem.IsRootDirectory
            && PathExists(SelectedFlatItem.FullPath);
    }

    private void NotifyFileOperationCommands()
    {
        CopySelectedItemCommand.NotifyCanExecuteChanged();
        CutSelectedItemCommand.NotifyCanExecuteChanged();
        PasteItemCommand.NotifyCanExecuteChanged();
        DeleteSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFlatItemChanged(FileTreeDisplayItem? value)
    {
        NotifyFileOperationCommands();
    }

    private string? GetSelectedTargetDirectory()
    {
        if (SelectedFlatItem == null)
            return RootItems.Count > 0 ? RootItems[0].FullPath : _normalDirectoryPath;

        if (SelectedFlatItem.IsDirectory)
            return SelectedFlatItem.FullPath;

        return Path.GetDirectoryName(SelectedFlatItem.FullPath);
    }

    private void ReloadCurrentRootPreservingState(string? selectedPath)
    {
        if (RootItems.Count == 0)
        {
            LoadDirectoryCore(_normalDirectoryPath);
            return;
        }

        var rootPath = RootItems[0].FullPath;
        var expandedDirs = _expandedDirs
            .Where(Directory.Exists)
            .ToHashSet();

        RootItems.Clear();
        FlatItems.Clear();
        SelectedFlatItem = null;

        if (!Directory.Exists(rootPath))
        {
            HasLoadedDirectory = false;
            AreAllDirectoriesExpanded = false;
            return;
        }

        var dirInfo = new DirectoryInfo(rootPath);
        var root = new FileTreeItem(dirInfo.Name, rootPath, true);
        LoadChildren(root, depth: 0, maxDepth: 3);
        RootItems.Add(root);

        _expandedDirs.Clear();
        foreach (var directory in expandedDirs)
        {
            if (IsPathInsideDirectory(directory, rootPath))
                _expandedDirs.Add(directory);
        }

        _expandedDirs.Add(root.FullPath);
        HasLoadedDirectory = true;
        RebuildFlatList(selectedPath);
    }

    private static string GetUniquePath(string directoryPath, string baseName, string extension)
    {
        var path = Path.Combine(directoryPath, $"{baseName}{extension}");
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        for (var i = 1; ; i++)
        {
            path = Path.Combine(directoryPath, $"{baseName} {i}{extension}");
            if (!File.Exists(path) && !Directory.Exists(path))
                return path;
        }
    }

    private static string GetUniqueItemPath(string directoryPath, string sourcePath)
    {
        var name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return GetUniquePathFromName(directoryPath, name);
    }

    private static string GetUniquePathFromName(string directoryPath, string name)
    {
        var path = Path.Combine(directoryPath, name);
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var extension = Path.GetExtension(name);
        var baseName = string.IsNullOrEmpty(extension) ? name : name[..^extension.Length];
        for (var i = 1; ; i++)
        {
            path = Path.Combine(directoryPath, $"{baseName} {i}{extension}");
            if (!File.Exists(path) && !Directory.Exists(path))
                return path;
        }
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
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

    private static bool IsPathInsideDirectory(string path, string directoryPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(directoryPath, path);
            return relativePath == "."
                || (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath));
        }
        catch
        {
            return false;
        }
    }

    private void RebuildFlatList(string? selectedPath = null)
    {
        FlatItems.Clear();
        if (RootItems.Count == 0)
        {
            AreAllDirectoriesExpanded = false;
            return;
        }

        FlattenItem(RootItems[0], 0);
        SelectedFlatItem = !string.IsNullOrWhiteSpace(selectedPath)
            ? FlatItems.FirstOrDefault(item => string.Equals(item.FullPath, selectedPath, StringComparison.Ordinal))
            : null;
        UpdateExpansionState();
    }

    private void FlattenItem(FileTreeItem item, int indent)
    {
        var isExpanded = item.IsDirectory && _expandedDirs.Contains(item.FullPath);
        FlatItems.Add(new FileTreeDisplayItem(item.Name, item.FullPath, item.IsDirectory, indent, isExpanded));
        if (isExpanded)
        {
            foreach (var child in item.Children)
                FlattenItem(child, indent + 1);
        }
    }

    private void UpdateExpansionState()
    {
        AreAllDirectoriesExpanded = RootItems.Count > 0
            && EnumerateDirectories(RootItems[0]).All(directory => _expandedDirs.Contains(directory.FullPath));
    }

    private static IEnumerable<FileTreeItem> EnumerateDirectories(FileTreeItem item)
    {
        if (!item.IsDirectory)
            yield break;

        yield return item;

        foreach (var child in item.Children)
        {
            foreach (var directory in EnumerateDirectories(child))
                yield return directory;
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
