using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.Core.FileTree;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;

namespace EasyCon2.Avalonia.UiTests;

[TestFixture]
public class FileTreeViewModelTests
{
    private static bool _stylesInitialized;
    private string _tempRoot = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();

        if (_stylesInitialized)
            return;

        if (!Application.Current!.Styles.OfType<FluentTheme>().Any())
            Application.Current.Styles.Add(new FluentTheme());
        AddStyleIfMissing("avares://EasyCon2.Avalonia/Resources/Styles/EasyConWorkbenchTheme.axaml");
        _stylesInitialized = true;
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
    public void SortRebuild_PreservesFirstVisibleRowPixelOffsetWithoutSelection()
    {
        string rootPath = Path.Combine(_tempRoot, "scroll-project");
        Directory.CreateDirectory(rootPath);
        FileTreeItem root = Item(Path.GetFileName(rootPath), rootPath, true, null);
        for (int index = 0; index < 80; index++)
        {
            string name = $"script{index:D3}.ecs";
            root.Children.Add(Item(name, Path.Combine(rootPath, name), false, null));
        }

        FileTreeViewModel vm = CreateViewModel(_ => root);
        Assert.That(vm.LoadDirectoryAsync(rootPath).IsCompletedSuccessfully, Is.True);
        vm.SelectedFlatItem = null;

        FileTreeView view = new() { DataContext = vm };
        Window window = new() { Width = 300, Height = 240, Content = view };
        try
        {
            window.Show();
            PumpUntil(window, () => view.FindControl<ListBox>("FileList")?.FindDescendantOfType<ScrollViewer>() != null,
                "工作树滚动视图没有加载");

            ListBox list = view.FindControl<ListBox>("FileList")!;
            ScrollViewer scrollViewer = list.FindDescendantOfType<ScrollViewer>()!;
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 600);
            (FileTreeDisplayItem Item, double Top) firstVisible = default;
            PumpUntil(window, () =>
            {
                (FileTreeDisplayItem Item, double Top)? current = FindFirstVisibleRow(list, vm);
                if (current == null)
                    return false;
                firstVisible = current.Value;
                return true;
            }, "滚动后没有可见文件项");

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y + firstVisible.Top + 5);
            PumpUntil(window, () =>
            {
                (FileTreeDisplayItem Item, double Top)? current = FindFirstVisibleRow(list, vm);
                if (current == null)
                    return false;
                firstVisible = current.Value;
                return Math.Abs(firstVisible.Top + 5) < 1.0;
            }, "无法将首个可见项定位到 -5 px");

            string anchorPath = firstVisible.Item!.FullPath;
            const double expectedTop = -5;
            vm.SelectSortModeCommand.Execute(FileTreeSortMode.NameDescending);
            PumpUntil(window, () =>
            {
                double? top = GetRowTop(list, vm, anchorPath);
                return top.HasValue && Math.Abs(top.Value - expectedTop) < 1.0;
            }, "排序后首个可见项的像素偏移没有保留");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SortWhileLayoutIsHidden_RestoresItsPreviouslyCapturedAnchorWhenShown()
    {
        string rootPath = Path.Combine(_tempRoot, "layout-scroll-project");
        Directory.CreateDirectory(rootPath);
        FileTreeItem root = Item(Path.GetFileName(rootPath), rootPath, true, null);
        for (int index = 0; index < 80; index++)
        {
            string name = $"script{index:D3}.ecs";
            root.Children.Add(Item(name, Path.Combine(rootPath, name), false, null));
        }

        FileTreeViewModel vm = CreateViewModel(_ => root);
        Assert.That(vm.LoadDirectoryAsync(rootPath).IsCompletedSuccessfully, Is.True);

        FileTreeView classicView = new() { DataContext = vm };
        FileTreeView cardView = new() { DataContext = vm, IsVisible = false };
        Grid layoutHost = new();
        layoutHost.Children.Add(classicView);
        layoutHost.Children.Add(cardView);
        Window window = new() { Width = 300, Height = 240, Content = layoutHost };
        try
        {
            window.Show();
            PumpUntil(window, () => classicView.FindControl<ListBox>("FileList")?.FindDescendantOfType<ScrollViewer>() != null,
                "经典布局工作树没有加载");
            ListBox list = classicView.FindControl<ListBox>("FileList")!;
            ScrollViewer scrollViewer = list.FindDescendantOfType<ScrollViewer>()!;
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 600);

            (FileTreeDisplayItem Item, double Top) firstVisible = default;
            PumpUntil(window, () =>
            {
                (FileTreeDisplayItem Item, double Top)? current = FindFirstVisibleRow(list, vm);
                if (current == null)
                    return false;
                firstVisible = current.Value;
                return true;
            }, "经典布局滚动后没有可见文件项");

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y + firstVisible.Top + 5);
            PumpUntil(window, () =>
            {
                (FileTreeDisplayItem Item, double Top)? current = FindFirstVisibleRow(list, vm);
                if (current == null)
                    return false;
                firstVisible = current.Value;
                return Math.Abs(firstVisible.Top + 5) < 1.0;
            }, "经典布局无法将锚点定位到 -5 px");
            string anchorPath = firstVisible.Item!.FullPath;

            vm.SelectSortModeCommand.Execute(FileTreeSortMode.NameDescending);
            classicView.IsVisible = false;
            cardView.IsVisible = true;
            PumpUntil(window, () => cardView.FindControl<ListBox>("FileList")!.IsEffectivelyVisible,
                "卡片布局未切换为可见");

            classicView.IsVisible = true;
            cardView.IsVisible = false;
            PumpUntil(window, () =>
            {
                double? top = GetRowTop(list, vm, anchorPath);
                return top.HasValue && Math.Abs(top.Value + 5) < 1.0;
            }, "经典布局隐藏期间捕获的锚点没有在重新显示后恢复");
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StickyHeader_UsesExpandedPathAncestorAfterItsRowLeavesViewport(bool nested)
    {
        string rootPath = Path.Combine(_tempRoot, nested ? "sticky-nested-project" : "sticky-root-project");
        Directory.CreateDirectory(rootPath);
        FileTreeItem root = Item(Path.GetFileName(rootPath), rootPath, true, null);
        FileTreeItem expectedStickyItem = root;

        FileTreeItem parent = root;
        if (nested)
        {
            string nestedPath = Path.Combine(rootPath, "nested");
            parent = Item("nested", nestedPath, true, null);
            root.Children.Add(parent);
            expectedStickyItem = parent;
        }

        for (int index = 0; index < 160; index++)
        {
            string name = $"script{index:D3}.ecs";
            parent.Children.Add(Item(name, Path.Combine(parent.FullPath, name), false, null));
        }

        FileTreeViewModel vm = CreateViewModel(_ => root);
        Assert.That(vm.LoadDirectoryAsync(rootPath).IsCompletedSuccessfully, Is.True);
        if (nested)
            vm.ItemClickCommand.Execute(vm.FlatItems.Single(item => item.FullPath == parent.FullPath));

        FileTreeView view = new() { DataContext = vm };
        Window window = new() { Width = 300, Height = 240, Content = view };
        try
        {
            window.Show();
            PumpUntil(window, () => view.FindControl<ListBox>("FileList")?.FindDescendantOfType<ScrollViewer>() != null,
                "工作树滚动视图没有加载");
            ListBox list = view.FindControl<ListBox>("FileList")!;
            ScrollViewer scrollViewer = list.FindDescendantOfType<ScrollViewer>()!;

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 1_200);
            Border stickyHeader = view.FindControl<Border>("StickyHeader")!;
            TextBlock stickyText = view.FindControl<TextBlock>("StickyHeaderText")!;
            PumpUntil(window, () => stickyHeader.IsVisible && stickyText.Text == $"📁 {expectedStickyItem.Name}",
                $"滚出视口后没有显示预期的吸顶目录 {expectedStickyItem.Name}");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public async Task RefreshDuringProjectSwitch_UsesTheRequestedProjectInsteadOfTheOldRoot()
    {
        string pathA = Path.Combine(_tempRoot, "project-a");
        string pathB = Path.Combine(_tempRoot, "project-b");
        Directory.CreateDirectory(pathA);
        Directory.CreateDirectory(pathB);
        File.WriteAllText(Path.Combine(pathA, "script2.ecs"), "clipboard source");

        TaskCompletionSource<FileTreeItem?> firstBResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<FileTreeItem?> refreshedBResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstBStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource refreshedBStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource refreshedBCommitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> requestedPaths = [];
        int bLoadCount = 0;
        FileTreeViewModel? vm = null;

        vm = new FileTreeViewModel(path =>
        {
            requestedPaths.Add(path);
            if (path == pathA)
                return Task.FromResult<FileTreeItem?>(CreateProjectTree(pathA));

            if (path == pathB && Interlocked.Increment(ref bLoadCount) == 1)
            {
                firstBStarted.TrySetResult();
                return firstBResult.Task;
            }

            if (path == pathB)
            {
                refreshedBStarted.TrySetResult();
                return refreshedBResult.Task;
            }

            // The old implementation refreshes RootItems[0] here, which is still project A.
            return Task.FromResult<FileTreeItem?>(CreateProjectTree(path));
        }, action =>
        {
            action();
            FileTreeViewModel? current = vm;
            if (current != null
                && current.RootItems.Count == 1
                && current.RootItems[0].FullPath == pathB
                && !current.IsLoadingDirectory)
            {
                refreshedBCommitted.TrySetResult();
            }
            return Task.CompletedTask;
        });

        await vm.LoadDirectoryAsync(pathA).ConfigureAwait(false);
        vm.SelectedFlatItem = vm.FlatItems.Single(item => item.FullPath == Path.Combine(pathA, "script2.ecs"));
        vm.CopySelectedItemCommand.Execute(null);
        Assert.That(vm.PasteItemCommand.CanExecute(null), Is.True);

        Task firstBLoad = vm.LoadDirectoryAsync(pathB);
        await firstBStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsLoadingDirectory, Is.True);
            Assert.That(vm.HasLoadedDirectory, Is.False);
            Assert.That(vm.RootItems, Is.Empty);
            Assert.That(vm.FlatItems, Is.Empty);
            Assert.That(vm.SelectedFlatItem, Is.Null);
            Assert.That(vm.PasteItemCommand.CanExecute(null), Is.False);
        });

        vm.CreateFileCommand.Execute(null);
        vm.CreateFolderCommand.Execute(null);
        Assert.That(Directory.GetFileSystemEntries(pathB), Is.Empty,
            "项目切换期间不能向目标目录执行文件树操作");

        vm.RefreshTreeCommand.Execute(null);
        await refreshedBStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        refreshedBResult.SetResult(CreateProjectTree(pathB));
        await refreshedBCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Let the superseded B request finish after the current refresh committed.
        firstBResult.SetResult(CreateProjectTree(pathB));
        await firstBLoad.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(requestedPaths, Is.EqualTo(new[] { pathA, pathB, pathB }));
            Assert.That(vm.RootItems.Single().FullPath, Is.EqualTo(pathB));
            Assert.That(vm.HasLoadedDirectory, Is.True);
            Assert.That(vm.IsLoadingDirectory, Is.False);
        });
    }

    [Test]
    public void TwoFileTreeViews_HaveIndependentRadioGroups()
    {
        FileTreeViewModel sharedViewModel = new();
        FileTreeView first = new() { DataContext = sharedViewModel };
        FileTreeView second = new() { DataContext = sharedViewModel };
        MenuItem[] firstItems = GetSortMenuItems(first);
        MenuItem[] secondItems = GetSortMenuItems(second);
        sharedViewModel.SelectSortModeCommand.Execute(FileTreeSortMode.ExtensionDescending);
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(first.SortMenuGroupName, Is.Not.EqualTo(second.SortMenuGroupName));
            Assert.That(firstItems.Select(item => item.GroupName), Is.All.EqualTo(first.SortMenuGroupName));
            Assert.That(secondItems.Select(item => item.GroupName), Is.All.EqualTo(second.SortMenuGroupName));
            Assert.That(firstItems.Select(item => item.IsChecked), Is.EqualTo(new bool?[] { false, false, false, false, false, true }));
            Assert.That(secondItems.Select(item => item.IsChecked), Is.EqualTo(new bool?[] { false, false, false, false, false, true }));
        });
    }

    private static MenuItem[] GetSortMenuItems(FileTreeView view) =>
    [
        view.FindControl<MenuItem>("SortNameAscendingMenuItem")!,
        view.FindControl<MenuItem>("SortNameDescendingMenuItem")!,
        view.FindControl<MenuItem>("SortModifiedNewestMenuItem")!,
        view.FindControl<MenuItem>("SortModifiedOldestMenuItem")!,
        view.FindControl<MenuItem>("SortExtensionAscendingMenuItem")!,
        view.FindControl<MenuItem>("SortExtensionDescendingMenuItem")!
    ];

    private static (FileTreeDisplayItem Item, double Top)? FindFirstVisibleRow(ListBox list, FileTreeViewModel vm)
    {
        foreach (FileTreeDisplayItem item in vm.FlatItems)
        {
            if (list.ContainerFromItem(item) is not ListBoxItem container)
                continue;

            Point? point = container.TranslatePoint(new Point(0, 0), list);
            if (point == null)
                continue;

            double top = point.Value.Y;
            if (top + container.Bounds.Height <= 0 || top >= list.Bounds.Height)
                continue;

            return (item, top);
        }

        return null;
    }

    private static double? GetRowTop(ListBox list, FileTreeViewModel vm, string path)
    {
        FileTreeDisplayItem? item = vm.FlatItems.FirstOrDefault(candidate => candidate.FullPath == path);
        if (item == null || list.ContainerFromItem(item) is not ListBoxItem container)
            return null;

        return container.TranslatePoint(new Point(0, 0), list)?.Y;
    }

    private static void PumpUntil(Window window, Func<bool> condition, string failureMessage)
    {
        System.Diagnostics.Stopwatch timeout = System.Diagnostics.Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return;
            Thread.Yield();
        }

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.That(condition(), Is.True, failureMessage);
    }

    private static void AddStyleIfMissing(string uri)
    {
        if (Application.Current!.Styles.OfType<StyleInclude>().Any(include => include.Source?.AbsoluteUri == uri))
            return;

        Application.Current.Styles.Add(new StyleInclude(new Uri("avares://EasyCon2.Avalonia/"))
        {
            Source = new Uri(uri)
        });
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