using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.FileTree;

namespace EasyCon2.Avalonia.FileTree;

public partial class FileTreeView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private FileTreeViewModel? _viewModel;
    private ScrollAnchor? _scrollAnchor;
    private int _scrollRestoreGeneration;
    private bool _isAttached;
    private bool _isViewModelSubscribed;
    private readonly List<Visual> _visibilityWatchers = [];

    private sealed record ScrollAnchor(string? FullPath, double RelativeTop, double ScrollOffset, bool WasSelectedVisible);

    public FileTreeView()
    {
        SortMenuGroupName = Guid.NewGuid().ToString("N");
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            AttachViewModel();
            AttachScrollViewer();
            AttachVisibilityWatchers();
            if (_scrollAnchor != null && FileList.IsEffectivelyVisible)
                ScheduleScrollRestore();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            _scrollRestoreGeneration++;
            DetachViewModel();
            DetachScrollViewer();
            DetachVisibilityWatchers();
        };
        FileList.Loaded += OnFileListLoaded;
        FileList.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
                HandleListVisibilityChanged();
        };
    }

    public string SortMenuGroupName { get; }

    private void AttachVisibilityWatchers()
    {
        DetachVisibilityWatchers();
        for (Visual? visual = this; visual != null; visual = visual.GetVisualParent())
        {
            visual.PropertyChanged += OnWatchedVisualPropertyChanged;
            _visibilityWatchers.Add(visual);
        }
    }

    private void DetachVisibilityWatchers()
    {
        foreach (Visual visual in _visibilityWatchers)
            visual.PropertyChanged -= OnWatchedVisualPropertyChanged;
        _visibilityWatchers.Clear();
    }

    private void OnWatchedVisualPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty)
            HandleListVisibilityChanged();
    }

    private void HandleListVisibilityChanged()
    {
        if (FileList.IsEffectivelyVisible)
        {
            if (_scrollAnchor != null)
                ScheduleScrollRestore();
            else
                UpdateStickyHeader();
        }
        else
        {
            StickyHeader.IsVisible = false;
        }
    }

    private void AttachViewModel()
    {
        FileTreeViewModel? newViewModel = DataContext as FileTreeViewModel;
        if (!ReferenceEquals(_viewModel, newViewModel))
        {
            DetachViewModel();
            _scrollRestoreGeneration++;
            _scrollAnchor = null;
            _viewModel = newViewModel;
        }

        if (!_isAttached || _viewModel == null || _isViewModelSubscribed)
            return;

        _viewModel.DisplayListRebuilding += CaptureScrollAnchor;
        _viewModel.DisplayListRebuilt += ScheduleScrollRestore;
        _isViewModelSubscribed = true;
    }

    private void DetachViewModel()
    {
        if (_viewModel != null && _isViewModelSubscribed)
        {
            _viewModel.DisplayListRebuilding -= CaptureScrollAnchor;
            _viewModel.DisplayListRebuilt -= ScheduleScrollRestore;
        }
        _isViewModelSubscribed = false;
    }

    private void OnFileListLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        AttachScrollViewer();
        if (_scrollAnchor != null && FileList.IsEffectivelyVisible)
            ScheduleScrollRestore();
    }

    private void AttachScrollViewer()
    {
        DetachScrollViewer();
        _scrollViewer = FileList.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    private void CaptureScrollAnchor()
    {
        if (_scrollAnchor != null)
            return;

        if (!_isAttached || !FileList.IsEffectivelyVisible)
            return;

        if (_viewModel == null || _scrollViewer == null)
        {
            _scrollAnchor = new ScrollAnchor(null, 0, _scrollViewer?.Offset.Y ?? 0, false);
            return;
        }

        FileTreeDisplayItem? firstVisibleItem = null;
        double firstVisibleTop = 0;
        FileTreeDisplayItem? selectedVisibleItem = null;
        double selectedVisibleTop = 0;

        foreach (FileTreeDisplayItem item in _viewModel.FlatItems)
        {
            if (FileList.ContainerFromItem(item) is not ListBoxItem container)
                continue;

            Point? point = container.TranslatePoint(new Point(0, 0), FileList);
            if (point == null)
                continue;

            double top = point.Value.Y;
            double bottom = top + container.Bounds.Height;
            if (bottom <= 0 || top >= FileList.Bounds.Height)
                continue;

            if (firstVisibleItem == null)
            {
                firstVisibleItem = item;
                firstVisibleTop = top;
            }
            if (_viewModel.SelectedFlatItem != null
                && PathsEqual(item.FullPath, _viewModel.SelectedFlatItem.FullPath))
            {
                selectedVisibleItem = item;
                selectedVisibleTop = top;
                break;
            }
        }

        bool selectedWasVisible = selectedVisibleItem != null;
        FileTreeDisplayItem? anchorItem = selectedVisibleItem ?? firstVisibleItem;
        double anchorTop = selectedWasVisible ? selectedVisibleTop : firstVisibleTop;
        _scrollAnchor = new ScrollAnchor(anchorItem?.FullPath, anchorTop, _scrollViewer.Offset.Y, selectedWasVisible);
    }

    private void ScheduleScrollRestore()
    {
        int generation = ++_scrollRestoreGeneration;
        if (!_isAttached || !FileList.IsEffectivelyVisible)
            return;

        Dispatcher.UIThread.Post(() => RestoreScrollAnchor(generation), DispatcherPriority.Loaded);
    }

    private void RestoreScrollAnchor(int generation)
    {
        if (generation != _scrollRestoreGeneration || !_isAttached || !FileList.IsEffectivelyVisible || _scrollViewer == null || _viewModel == null)
            return;

        ScrollAnchor? anchor = _scrollAnchor;
        if (anchor == null)
            return;

        if (!string.IsNullOrEmpty(anchor.FullPath))
        {
            FileTreeDisplayItem? item = _viewModel.FlatItems.FirstOrDefault(candidate => PathsEqual(candidate.FullPath, anchor.FullPath));
            if (item != null)
            {
                FileList.ScrollIntoView(item);
                FileList.UpdateLayout();
                if (FileList.ContainerFromItem(item) is ListBoxItem container)
                {
                    Point? point = container.TranslatePoint(new Point(0, 0), FileList);
                    if (point != null)
                    {
                        double correction = point.Value.Y - anchor.RelativeTop;
                        double maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
                        double offset = Math.Clamp(_scrollViewer.Offset.Y + correction, 0, maxOffset);
                        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, offset);
                    }
                }
            }
            else
            {
                RestoreOffset(anchor.ScrollOffset);
            }
        }
        else
        {
            RestoreOffset(anchor.ScrollOffset);
        }

        _scrollAnchor = null;
        UpdateStickyHeader();
    }

    private void RestoreOffset(double requestedOffset)
    {
        if (_scrollViewer == null)
            return;

        double maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Clamp(requestedOffset, 0, maxOffset));
    }

    private static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateStickyHeader();
    }

    private void UpdateStickyHeader()
    {
        if (_scrollViewer == null || !FileList.IsEffectivelyVisible || DataContext is not FileTreeViewModel vm || vm.FlatItems.Count == 0)
        {
            StickyHeader.IsVisible = false;
            return;
        }

        FileTreeDisplayItem? firstVisibleItem = null;
        int firstVisibleIndex = -1;
        for (int index = 0; index < vm.FlatItems.Count; index++)
        {
            FileTreeDisplayItem item = vm.FlatItems[index];
            if (FileList.ContainerFromItem(item) is not ListBoxItem container)
                continue;

            Point? point = container.TranslatePoint(new Point(0, 0), FileList);
            if (point == null)
                continue;

            double top = point.Value.Y;
            if (top + container.Bounds.Height <= 0 || top >= FileList.Bounds.Height)
                continue;

            firstVisibleItem = item;
            firstVisibleIndex = index;
            break;
        }

        if (firstVisibleItem == null)
        {
            StickyHeader.IsVisible = false;
            return;
        }

        FileTreeDisplayItem? stickyItem = null;

        // 只从首个可见条目的真实祖先中选择吸顶项。祖先的容器可能已被虚拟化回收，
        // 此时它位于首个可见条目之前，视为已经滚出顶部。
        for (int index = 0; index <= firstVisibleIndex; index++)
        {
            FileTreeDisplayItem item = vm.FlatItems[index];
            if (!item.IsDirectory || !item.IsExpanded || !IsAncestorOrSame(item.FullPath, firstVisibleItem.FullPath))
                continue;

            bool crossedTop;
            if (FileList.ContainerFromItem(item) is ListBoxItem container)
            {
                Point? point = container.TranslatePoint(new Point(0, 0), FileList);
                crossedTop = point != null && point.Value.Y < 0;
            }
            else
            {
                crossedTop = index < firstVisibleIndex;
            }

            if (crossedTop)
                stickyItem = item;
        }

        if (stickyItem != null)
        {
            StickyHeader.IsVisible = true;
            StickyHeaderText.Text = stickyItem.DisplayName;
            StickyHeaderText.Margin = new Thickness(stickyItem.IndentPadding.Left, 0, 0, 0);
        }
        else
        {
            StickyHeader.IsVisible = false;
        }
    }

    private static bool IsAncestorOrSame(string ancestorPath, string path)
    {
        string? currentPath = path;
        while (!string.IsNullOrEmpty(currentPath))
        {
            if (PathsEqual(ancestorPath, currentPath))
                return true;

            string? parentPath = Path.GetDirectoryName(currentPath);
            if (parentPath == currentPath)
                break;
            currentPath = parentPath;
        }

        return false;
    }

    /// <summary>
    /// 单击目录项切换展开/折叠。
    /// </summary>
    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        var listBoxItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (listBoxItem?.DataContext is FileTreeDisplayItem item && item.IsDirectory)
        {
            if (DataContext is FileTreeViewModel vm)
                vm.ItemClickCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 双击文件项打开。
    /// </summary>
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        var listBoxItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (listBoxItem?.DataContext is FileTreeDisplayItem item && !item.IsDirectory)
        {
            if (DataContext is FileTreeViewModel vm)
                vm.ItemClickCommand.Execute(item);
            e.Handled = true;
        }
    }
}