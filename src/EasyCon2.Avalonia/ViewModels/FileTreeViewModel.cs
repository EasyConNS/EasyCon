using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon2.Avalonia.Core.FileTree;
using EasyCon2.Avalonia.Models;
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
    private bool _isLoadingDirectory;

    [ObservableProperty]
    private bool _areAllDirectoriesExpanded = false;

    [ObservableProperty]
    private FileTreeSortMode _sortMode = FileTreeSortMode.NameAscending;

    private readonly HashSet<string> _expandedDirs = new(PathComparer);
    private readonly Func<string, Task<FileTreeItem?>> _loadRootAsync;
    private readonly Func<Action, Task> _invokeOnUiThreadAsync;
    private string? _normalDirectoryPath;
    // 目录加载代数：快速连续切换目录时，过期加载的结果不得覆盖最新一次
    private int _loadGeneration;

    public event Action<string>? FileActivated;
    public event Action? OpenProjectRequested;
    public event Action? NewScriptRequested;
    public event Action? OpenScriptRequested;
    public event Action? OpenProjectFolderRequested;
    public event Action? SaveScriptRequested;
    public event Action? SaveScriptAsRequested;
    public event Action? CloseProjectRequested;
    public event Action<string>? FileOperationMessage;
    public event Action? SortModeChanged;
    public event Action? DisplayListRebuilding;
    public event Action? DisplayListRebuilt;

    public bool IsSortMenuEnabled => HasLoadedDirectory;
    public bool IsNameAscendingSelected => SortMode == FileTreeSortMode.NameAscending;
    public bool IsNameDescendingSelected => SortMode == FileTreeSortMode.NameDescending;
    public bool IsModifiedNewestSelected => SortMode == FileTreeSortMode.ModifiedNewestFirst;
    public bool IsModifiedOldestSelected => SortMode == FileTreeSortMode.ModifiedOldestFirst;
    public bool IsExtensionAscendingSelected => SortMode == FileTreeSortMode.ExtensionAscending;
    public bool IsExtensionDescendingSelected => SortMode == FileTreeSortMode.ExtensionDescending;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private string? _clipboardPath;
    private bool _isClipboardCut;

    public FileTreeViewModel()
        : this(directoryPath => Task.Run(() => TryBuildRoot(directoryPath)), DispatchToUiThreadAsync)
    {
    }

    internal FileTreeViewModel(Func<string, Task<FileTreeItem?>> loadRootAsync, Func<Action, Task> invokeOnUiThreadAsync)
    {
        _loadRootAsync = loadRootAsync;
        _invokeOnUiThreadAsync = invokeOnUiThreadAsync;
    }

    public void LoadDirectory(string? directoryPath)
    {
        _ = LoadDirectoryAsync(directoryPath);
    }

    public Task LoadDirectoryAsync(string? directoryPath)
    {
        _normalDirectoryPath = directoryPath;
        return LoadDirectoryCoreAsync(directoryPath);
    }

    public void ShowNormalTree()
    {
        _ = LoadDirectoryCoreAsync(_normalDirectoryPath);
    }

    private Task LoadDirectoryCoreAsync(string? directoryPath)
    {
        int loadId = ++_loadGeneration;

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            RootItems.Clear();
            _expandedDirs.Clear();
            FlatItems = [];
            SelectedFlatItem = null;
            HasLoadedDirectory = false;
            AreAllDirectoriesExpanded = false;
            IsLoadingDirectory = false;
            return Task.CompletedTask;
        }

        string? existingRootPath = RootItems.Count > 0 ? RootItems[0].FullPath : null;
        bool sameRoot = existingRootPath != null && PathsEqual(existingRootPath, directoryPath);
        string? selectedPath = sameRoot ? SelectedFlatItem?.FullPath : null;

        if (!sameRoot)
        {
            // 切换项目时清掉旧树，避免旧项目仍被当成当前操作目标。
            RootItems.Clear();
            _expandedDirs.Clear();
            FlatItems = [];
            SelectedFlatItem = null;
            HasLoadedDirectory = false;
            AreAllDirectoriesExpanded = false;
        }

        IsLoadingDirectory = true;

        // 目录枚举（递归 3 层）在大目录上耗时数百毫秒，放线程池执行；
        // 构建的是尚未挂入任何可观察集合的普通对象树，离线安全
        return LoadRootInBackgroundAsync(directoryPath, loadId, sameRoot, selectedPath);
    }

    private async Task LoadRootInBackgroundAsync(string directoryPath, int loadId, bool sameRoot, string? selectedPath)
    {
        FileTreeItem? root;
        try
        {
            root = await _loadRootAsync(directoryPath).ConfigureAwait(false);
        }
        catch
        {
            root = null;
        }

        await _invokeOnUiThreadAsync(() =>
        {
            if (loadId != _loadGeneration || !PathsEqual(directoryPath, _normalDirectoryPath ?? string.Empty))
                return;

            IsLoadingDirectory = false;

            if (root == null)
            {
                RootItems.Clear();
                _expandedDirs.Clear();
                FlatItems = [];
                SelectedFlatItem = null;
                HasLoadedDirectory = false;
                AreAllDirectoriesExpanded = false;
                return;
            }

            if (!sameRoot)
            {
                _expandedDirs.Clear();
            }
            else
            {
                RemoveMissingExpandedDirectories(root);
            }

            RootItems.Clear();
            RootItems.Add(root);

            // 根目录默认展开
            _expandedDirs.Add(root.FullPath);
            string? preferredSelection = sameRoot
                ? SelectedFlatItem?.FullPath ?? selectedPath
                : selectedPath;
            RebuildFlatList(preferredSelection, preserveViewport: sameRoot);
            HasLoadedDirectory = true;
        }).ConfigureAwait(false);
    }

    /// <summary>在工作线程上构建根节点树；失败返回 null。只做枚举，不触碰可观察状态。</summary>
    private static FileTreeItem? TryBuildRoot(string directoryPath)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            directoryInfo.Refresh();
            if (!directoryInfo.Exists)
                return null;

            var root = CreateTreeItem(directoryPath, isDirectory: true);
            LoadChildren(root, depth: 0, maxDepth: 3);
            return root;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void SelectSortMode(FileTreeSortMode mode)
    {
        if (!Enum.IsDefined(mode) || SortMode == mode)
            return;

        SortMode = mode;
    }

    partial void OnSortModeChanged(FileTreeSortMode value)
    {
        NotifySortSelectionProperties();
        if (RootItems.Count > 0)
            RebuildFlatList(SelectedFlatItem?.FullPath, preserveViewport: true);
        SortModeChanged?.Invoke();
    }

    partial void OnHasLoadedDirectoryChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSortMenuEnabled));
        NotifyFileOperationCommands();
    }

    private void NotifySortSelectionProperties()
    {
        OnPropertyChanged(nameof(IsNameAscendingSelected));
        OnPropertyChanged(nameof(IsNameDescendingSelected));
        OnPropertyChanged(nameof(IsModifiedNewestSelected));
        OnPropertyChanged(nameof(IsModifiedOldestSelected));
        OnPropertyChanged(nameof(IsExtensionAscendingSelected));
        OnPropertyChanged(nameof(IsExtensionDescendingSelected));
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
    private void OpenScript()
    {
        OpenScriptRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenProjectFolder()
    {
        OpenProjectFolderRequested?.Invoke();
    }

    [RelayCommand]
    private void SaveScript()
    {
        SaveScriptRequested?.Invoke();
    }

    [RelayCommand]
    private void SaveScriptAs()
    {
        SaveScriptAsRequested?.Invoke();
    }

    [RelayCommand]
    private void CloseProject()
    {
        CloseProjectRequested?.Invoke();
    }

    [RelayCommand]
    private void CreateFile()
    {
        if (!HasLoadedDirectory || RootItems.Count == 0)
            return;

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
        if (!HasLoadedDirectory || RootItems.Count == 0)
            return;

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
        ReloadCurrentRootPreservingState(selectedPath: null);
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
        if (!HasLoadedDirectory || string.IsNullOrWhiteSpace(_clipboardPath) || !PathExists(_clipboardPath))
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
        int loadId = ++_loadGeneration;
        if (RootItems.Count == 0)
        {
            _ = LoadDirectoryCoreAsync(_normalDirectoryPath);
            return;
        }

        var rootPath = RootItems[0].FullPath;

        if (!Directory.Exists(rootPath))
        {
            RootItems.Clear();
            _expandedDirs.Clear();
            FlatItems = [];
            SelectedFlatItem = null;
            HasLoadedDirectory = false;
            AreAllDirectoriesExpanded = false;
            IsLoadingDirectory = false;
            return;
        }

        // 枚举期间保留旧树和当前交互状态；提交时恢复当下仍然有效的状态。
        IsLoadingDirectory = true;
        _ = ReloadRootInBackgroundAsync(rootPath, selectedPath, loadId);
    }

    private async Task ReloadRootInBackgroundAsync(string rootPath, string? preferredSelectedPath, int loadId)
    {
        FileTreeItem? root;
        try
        {
            root = await _loadRootAsync(rootPath).ConfigureAwait(false);
        }
        catch
        {
            root = null;
        }

        await _invokeOnUiThreadAsync(() =>
        {
            if (loadId != _loadGeneration || !PathsEqual(rootPath, _normalDirectoryPath ?? string.Empty))
                return;

            IsLoadingDirectory = false;

            if (root == null)
            {
                RootItems.Clear();
                _expandedDirs.Clear();
                FlatItems = [];
                SelectedFlatItem = null;
                HasLoadedDirectory = false;
                AreAllDirectoriesExpanded = false;
                return;
            }

            RemoveMissingExpandedDirectories(root);
            RootItems.Clear();
            RootItems.Add(root);
            _expandedDirs.Add(root.FullPath);
            HasLoadedDirectory = true;
            RebuildFlatList(preferredSelectedPath ?? SelectedFlatItem?.FullPath, preserveViewport: true);
        }).ConfigureAwait(false);
    }

    private void RemoveMissingExpandedDirectories(FileTreeItem root)
    {
        var existingDirectories = EnumerateDirectories(root)
            .Select(directory => directory.FullPath)
            .ToHashSet(PathComparer);
        _expandedDirs.RemoveWhere(path => !existingDirectories.Contains(path));
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
                || (relativePath != ".."
                    && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
                    && !Path.IsPathRooted(relativePath));
        }
        catch
        {
            return false;
        }
    }

    private void RebuildFlatList(string? selectedPath = null, bool preserveViewport = false)
    {
        if (RootItems.Count == 0)
        {
            FlatItems = [];
            SelectedFlatItem = null;
            AreAllDirectoriesExpanded = false;
            return;
        }

        if (preserveViewport)
            DisplayListRebuilding?.Invoke();

        string? selectionPath = selectedPath ?? SelectedFlatItem?.FullPath;
        var displayItems = new List<FileTreeDisplayItem>();
        var comparer = new FileTreeSortComparer(SortMode);
        FlattenItem(RootItems[0], 0, comparer, displayItems);
        FlatItems = new ObservableCollection<FileTreeDisplayItem>(displayItems);

        SelectedFlatItem = FindSelectionWithParentFallback(displayItems, selectionPath);
        UpdateExpansionState();

        if (preserveViewport)
            DisplayListRebuilt?.Invoke();
    }

    private FileTreeDisplayItem? FindSelectionWithParentFallback(List<FileTreeDisplayItem> displayItems, string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return null;

        string? candidatePath = selectedPath;
        while (!string.IsNullOrWhiteSpace(candidatePath))
        {
            FileTreeDisplayItem? candidate = displayItems.FirstOrDefault(item => PathsEqual(item.FullPath, candidatePath));
            if (candidate != null)
                return candidate;
            candidatePath = Path.GetDirectoryName(candidatePath);
        }

        return null;
    }

    private void FlattenItem(FileTreeItem item, int indent, FileTreeSortComparer comparer, List<FileTreeDisplayItem> displayItems)
    {
        var isExpanded = item.IsDirectory && _expandedDirs.Contains(item.FullPath);
        displayItems.Add(new FileTreeDisplayItem(item.Name, item.FullPath, item.IsDirectory, indent, isExpanded));
        if (isExpanded)
        {
            foreach (FileTreeItem child in item.Children.OrderBy(child => child.SortKey, comparer))
                FlattenItem(child, indent + 1, comparer, displayItems);
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

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(parent.FullPath);
        }
        catch
        {
            directories = [];
        }

        foreach (string directory in directories)
        {
            try
            {
                string directoryName = Path.GetFileName(directory);
                if (directoryName.StartsWith('.')) continue;

                FileTreeItem child = CreateTreeItem(directory, isDirectory: true);
                LoadChildren(child, depth + 1, maxDepth);
                parent.Children.Add(child);
            }
            catch
            {
                // 单个目录失效不影响同级节点。
            }
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(parent.FullPath);
        }
        catch
        {
            files = [];
        }

        foreach (string file in files)
        {
            try
            {
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith('.')) continue;

                parent.Children.Add(CreateTreeItem(file, isDirectory: false));
            }
            catch
            {
                // 单个文件失效不影响同级节点。
            }
        }
    }

    public void NotifyFileSaved(string fullPath)
    {
        if (!HasLoadedDirectory || RootItems.Count == 0 || !IsPathInsideDirectory(fullPath, RootItems[0].FullPath))
            return;

        FileTreeItem? savedItem = FindTreeItem(RootItems[0], fullPath);
        if (savedItem == null)
        {
            ReloadCurrentRootPreservingState(selectedPath: null);
            return;
        }

        savedItem.UpdateLastWriteTimeUtc(TryGetLastWriteTimeUtc(fullPath));
        if (SortMode is FileTreeSortMode.ModifiedNewestFirst or FileTreeSortMode.ModifiedOldestFirst)
            RebuildFlatList(SelectedFlatItem?.FullPath, preserveViewport: true);
    }

    private static FileTreeItem? FindTreeItem(FileTreeItem item, string fullPath)
    {
        if (PathsEqual(item.FullPath, fullPath))
            return item;

        foreach (FileTreeItem child in item.Children)
        {
            FileTreeItem? match = FindTreeItem(child, fullPath);
            if (match != null)
                return match;
        }

        return null;
    }

    private static FileTreeItem CreateTreeItem(string fullPath, bool isDirectory)
    {
        string name = isDirectory
            ? new DirectoryInfo(fullPath).Name
            : Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(name))
            name = Path.GetPathRoot(fullPath) ?? fullPath;
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);
        DateTime? lastWriteTimeUtc = null;

        try
        {
            if (isDirectory)
            {
                DirectoryInfo directoryInfo = new(fullPath);
                directoryInfo.Refresh();
                name = directoryInfo.Name;
            }
            else
            {
                FileInfo fileInfo = new(fullPath);
                fileInfo.Refresh();
                name = fileInfo.Name;
                extension = fileInfo.Extension;
                if (fileInfo.Exists)
                    lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            }
        }
        catch
        {
            // 节点名和扩展名仍可从路径取得；缺失时间会稳定地排在修改时间组末尾。
        }

        var sortKey = new FileTreeSortKey(name, fullPath, isDirectory, extension, lastWriteTimeUtc);
        return new FileTreeItem(name, fullPath, isDirectory, sortKey);
    }

    private static DateTime? TryGetLastWriteTimeUtc(string fullPath)
    {
        try
        {
            FileInfo fileInfo = new(fullPath);
            fileInfo.Refresh();
            return fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task DispatchToUiThreadAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static bool PathsEqual(string left, string right) => PathComparer.Equals(left, right);
}