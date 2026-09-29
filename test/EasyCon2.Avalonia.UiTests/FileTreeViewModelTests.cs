using Avalonia;
using EasyCon2.Avalonia.Core.FileTree;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;

namespace EasyCon2.Avalonia.UiTests;

[TestFixture]
public class FileTreeViewModelTests
{
    private string _tempRoot = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
    }

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "easycon-worktree-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Test]
    public async Task SortRebuild_PreservesExpandedAndSelectedPaths_WithoutOpeningFiles()
    {
        string rootPath = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(rootPath);
        FileTreeViewModel vm = CreateViewModel(_ => CreateProjectTree(rootPath));
        int activatedCount = 0;
        vm.FileActivated += _ => activatedCount++;

        await vm.LoadDirectoryAsync(rootPath).ConfigureAwait(false);
        string nestedDirectoryPath = Path.Combine(rootPath, "nested2");
        FileTreeDisplayItem nestedDirectory = vm.FlatItems.Single(item => item.FullPath == nestedDirectoryPath);
        vm.ItemClickCommand.Execute(nestedDirectory);

        string selectedPath = Path.Combine(rootPath, "script2.ecs");
        vm.SelectedFlatItem = vm.FlatItems.Single(item => item.FullPath == selectedPath);
        vm.SelectSortModeCommand.Execute(FileTreeSortMode.NameDescending);

        Assert.Multiple(() =>
        {
            Assert.That(vm.FlatItems.Select(item => item.Name), Is.EqualTo(new[]
            {
                "project", "nested2", "nested-child.ecs", "script10.ecs", "script02.ecs", "script2.ecs"
            }));
            Assert.That(vm.SelectedFlatItem?.FullPath, Is.EqualTo(selectedPath));
            Assert.That(vm.FlatItems.Any(item => item.FullPath == Path.Combine(nestedDirectoryPath, "nested-child.ecs")), Is.True);
            Assert.That(activatedCount, Is.Zero, "重排不能触发文件打开");
        });
    }

    [Test]
    public async Task LoadCommit_UsesLatestSortModeSelectedWhileDirectoryBuildIsPending()
    {
        string rootPath = Path.Combine(_tempRoot, "pending-project");
        Directory.CreateDirectory(rootPath);
        TaskCompletionSource<FileTreeItem?> buildResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource buildStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FileTreeViewModel vm = CreateAsyncViewModel(_ =>
        {
            buildStarted.TrySetResult();
            return buildResult.Task;
        });

        Task loadTask = vm.LoadDirectoryAsync(rootPath);
        await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        vm.SelectSortModeCommand.Execute(FileTreeSortMode.ModifiedNewestFirst);
        buildResult.SetResult(CreateProjectTree(rootPath));
        await loadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.That(vm.SortMode, Is.EqualTo(FileTreeSortMode.ModifiedNewestFirst));
        Assert.That(vm.FlatItems.Select(item => item.Name), Is.EqualTo(new[]
        {
            "pending-project", "nested2", "script02.ecs", "script10.ecs", "script2.ecs"
        }));
    }

    [Test]
    public async Task SameRootRefresh_UsesLatestSelectionAndExpansionFromWhileLoading()
    {
        string rootPath = Path.Combine(_tempRoot, "refresh-project");
        Directory.CreateDirectory(rootPath);
        int buildCount = 0;
        TaskCompletionSource<FileTreeItem?> refreshedTree = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource refreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FileTreeViewModel vm = CreateAsyncViewModel(path =>
        {
            if (Interlocked.Increment(ref buildCount) == 1)
                return Task.FromResult<FileTreeItem?>(CreateProjectTree(path));

            refreshStarted.TrySetResult();
            return refreshedTree.Task;
        });

        await vm.LoadDirectoryAsync(rootPath).ConfigureAwait(false);
        string previousSelection = Path.Combine(rootPath, "script2.ecs");
        vm.SelectedFlatItem = vm.FlatItems.Single(item => item.FullPath == previousSelection);

        Task refreshTask = vm.LoadDirectoryAsync(rootPath);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        FileTreeDisplayItem nestedDirectory = vm.FlatItems.Single(item => item.FullPath == Path.Combine(rootPath, "nested2"));
        vm.ItemClickCommand.Execute(nestedDirectory);
        string latestSelection = Path.Combine(rootPath, "script10.ecs");
        vm.SelectedFlatItem = vm.FlatItems.Single(item => item.FullPath == latestSelection);
        refreshedTree.SetResult(CreateProjectTree(rootPath));
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedFlatItem?.FullPath, Is.EqualTo(latestSelection));
            Assert.That(vm.FlatItems.Any(item => item.FullPath == Path.Combine(rootPath, "nested2", "nested-child.ecs")), Is.True);
        });
    }

    [Test]
    public async Task NotifyFileSaved_UpdatesModifiedTimeSortWithoutReloadingEditor()
    {
        string rootPath = Path.Combine(_tempRoot, "modified-project");
        Directory.CreateDirectory(rootPath);
        string olderFilePath = Path.Combine(rootPath, "older.ecs");
        string newerFilePath = Path.Combine(rootPath, "newer.ecs");
        File.WriteAllText(olderFilePath, "old");
        File.WriteAllText(newerFilePath, "new");
        DateTime olderTime = DateTime.UtcNow.AddHours(-2);
        DateTime newerTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(olderFilePath, olderTime);
        File.SetLastWriteTimeUtc(newerFilePath, newerTime);

        FileTreeItem root = Item(Path.GetFileName(rootPath), rootPath, true, null);
        root.Children.Add(Item("older.ecs", olderFilePath, false, olderTime));
        root.Children.Add(Item("newer.ecs", newerFilePath, false, newerTime));
        FileTreeViewModel vm = CreateViewModel(_ => root);
        await vm.LoadDirectoryAsync(rootPath).ConfigureAwait(false);
        vm.SelectSortModeCommand.Execute(FileTreeSortMode.ModifiedNewestFirst);
        Assert.That(vm.FlatItems.Skip(1).Select(item => item.Name), Is.EqualTo(new[] { "newer.ecs", "older.ecs" }));

        File.SetLastWriteTimeUtc(olderFilePath, DateTime.UtcNow.AddHours(1));
        vm.NotifyFileSaved(olderFilePath);

        Assert.That(vm.FlatItems.Skip(1).Select(item => item.Name), Is.EqualTo(new[] { "older.ecs", "newer.ecs" }));
    }

    [Test]
    public async Task StaleProjectLoadCannotReplaceNewProject_AndCloseInvalidatesPendingLoad()
    {
        string pathA = Path.Combine(_tempRoot, "project-a");
        string pathB = Path.Combine(_tempRoot, "project-b");
        string pathC = Path.Combine(_tempRoot, "project-c");
        Directory.CreateDirectory(pathA);
        Directory.CreateDirectory(pathB);
        Directory.CreateDirectory(pathC);

        TaskCompletionSource<FileTreeItem?> buildA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<FileTreeItem?> buildB = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<FileTreeItem?> buildC = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource startedA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource startedB = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource startedC = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FileTreeViewModel vm = CreateAsyncViewModel(path =>
        {
            if (path == pathA)
            {
                startedA.TrySetResult();
                return buildA.Task;
            }
            if (path == pathB)
            {
                startedB.TrySetResult();
                return buildB.Task;
            }
            startedC.TrySetResult();
            return buildC.Task;
        });

        Task loadA = vm.LoadDirectoryAsync(pathA);
        await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Task loadB = vm.LoadDirectoryAsync(pathB);
        await startedB.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        buildB.SetResult(CreateProjectTree(pathB));
        await loadB.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        buildA.SetResult(CreateProjectTree(pathA));
        await loadA.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.That(vm.RootItems.Single().FullPath, Is.EqualTo(pathB));

        Task loadC = vm.LoadDirectoryAsync(pathC);
        await startedC.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await vm.LoadDirectoryAsync(null).ConfigureAwait(false);
        buildC.SetResult(CreateProjectTree(pathC));
        await loadC.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.RootItems, Is.Empty);
            Assert.That(vm.FlatItems, Is.Empty);
            Assert.That(vm.HasLoadedDirectory, Is.False);
        });
    }

    [Test]
    public void TwoFileTreeViews_HaveIndependentRadioGroups()
    {
        FileTreeView first = new();
        FileTreeView second = new();

        Assert.That(first.SortMenuGroupName, Is.Not.EqualTo(second.SortMenuGroupName));
    }

    private static FileTreeViewModel CreateViewModel(Func<string, FileTreeItem?> rootBuilder)
    {
        return CreateAsyncViewModel(path => Task.FromResult(rootBuilder(path)));
    }

    private static FileTreeViewModel CreateAsyncViewModel(Func<string, Task<FileTreeItem?>> loadRootAsync)
    {
        return new FileTreeViewModel(loadRootAsync, action =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    private static FileTreeItem CreateProjectTree(string rootPath)
    {
        DateTime now = new(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
        FileTreeItem root = Item(Path.GetFileName(rootPath), rootPath, isDirectory: true, null);
        string nestedPath = Path.Combine(rootPath, "nested2");
        FileTreeItem nested = Item("nested2", nestedPath, isDirectory: true, null);
        nested.Children.Add(Item("nested-child.ecs", Path.Combine(nestedPath, "nested-child.ecs"), false, now.AddMinutes(1)));
        root.Children.Add(Item("script10.ecs", Path.Combine(rootPath, "script10.ecs"), false, now.AddMinutes(10)));
        root.Children.Add(nested);
        root.Children.Add(Item("script2.ecs", Path.Combine(rootPath, "script2.ecs"), false, now.AddMinutes(2)));
        root.Children.Add(Item("script02.ecs", Path.Combine(rootPath, "script02.ecs"), false, now.AddMinutes(20)));
        return root;
    }

    private static FileTreeItem Item(string name, string path, bool isDirectory, DateTime? modified)
    {
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);
        var key = new FileTreeSortKey(name, path, isDirectory, extension, modified);
        return new FileTreeItem(name, path, isDirectory, key);
    }
}