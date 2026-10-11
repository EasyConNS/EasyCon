using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EasyCon2.Avalonia.Models;

namespace EasyCon2.Avalonia.FileTree;

/// <summary>
/// 滚动锚定宿主：为 <see cref="FileTreeScrollAnchorBehavior"/> 提供控件访问。
/// 由 <see cref="FileTreeView"/> 实现——ListBox/ScrollViewer/StickyHeader 的视觉树查询必须留在控件内部。
/// </summary>
public interface IScrollAnchorHost
{
    ListBox FileList { get; }

    ScrollViewer? ScrollViewer { get; }

    /// <summary>吸顶目录容器。</summary>
    Border StickyHeader { get; }

    /// <summary>吸顶目录文本。</summary>
    TextBlock StickyHeaderText { get; }

    FileTreeViewModel? ViewModel { get; }
}

/// <summary>
/// 工作树滚动锚定 + 吸顶目录行为（算法体自 FileTreeView.axaml.cs 原样搬入）。
/// 项目未引用 Avalonia.Xaml.Behaviors 包，故实现为普通类：
/// 由 <see cref="FileTreeView"/> 持有，在挂入/离开视觉树时机调用 <see cref="Attach"/>/<see cref="Detach"/>，
/// Behavior 只持有 Host，不反向持有 VM。
/// </summary>
public sealed class FileTreeScrollAnchorBehavior
{
    private sealed record ScrollAnchor(string? FullPath, double RelativeTop, double ScrollOffset, bool WasSelectedVisible);

    private readonly IScrollAnchorHost _host;
    private ScrollAnchor? _scrollAnchor;
    private int _scrollRestoreGeneration;
    private bool _isAttached;
    private bool _isViewModelSubscribed;
    private FileTreeViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;

    public FileTreeScrollAnchorBehavior(IScrollAnchorHost host)
    {
        _host = host;
    }

    /// <summary>控件挂入视觉树时调用。</summary>
    public void Attach()
    {
        _isAttached = true;
    }

    /// <summary>控件离开视觉树时调用：作废未完成的恢复并完整退订 VM 事件与 ScrollViewer。</summary>
    public void Detach()
    {
        _isAttached = false;
        _scrollRestoreGeneration++;
        DetachViewModel();
        DetachScrollViewer();
    }

    public void AttachViewModel()
    {
        FileTreeViewModel? newViewModel = _host.ViewModel;
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

    public void DetachViewModel()
    {
        if (_viewModel != null && _isViewModelSubscribed)
        {
            _viewModel.DisplayListRebuilding -= CaptureScrollAnchor;
            _viewModel.DisplayListRebuilt -= ScheduleScrollRestore;
        }
        _isViewModelSubscribed = false;
    }

    public void AttachScrollViewer(ScrollViewer? scrollViewer)
    {
        DetachScrollViewer();
        _scrollViewer = scrollViewer;
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    public void DetachScrollViewer()
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    /// <summary>列表重新可见时调用：有锚点待恢复则调度恢复，否则刷新吸顶头。</summary>
    public void ScheduleScrollRestoreIfPending()
    {
        if (_scrollAnchor != null && _host.FileList.IsEffectivelyVisible)
            ScheduleScrollRestore();
    }

    /// <summary>列表可见性变化时调用：可见走锚点/吸顶刷新，不可见时隐藏吸顶头。</summary>
    public void HandleListVisibilityChanged()
    {
        if (_host.FileList.IsEffectivelyVisible)
        {
            if (_scrollAnchor != null)
                ScheduleScrollRestore();
            else
                UpdateStickyHeader();
        }
        else
        {
            _host.StickyHeader.IsVisible = false;
        }
    }

    public void CaptureScrollAnchor()
    {
        if (_scrollAnchor != null)
            return;

        if (!_isAttached || !_host.FileList.IsEffectivelyVisible)
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
            if (_host.FileList.ContainerFromItem(item) is not ListBoxItem container)
                continue;

            Point? point = container.TranslatePoint(new Point(0, 0), _host.FileList);
            if (point == null)
                continue;

            double top = point.Value.Y;
            double bottom = top + container.Bounds.Height;
            if (bottom <= 0 || top >= _host.FileList.Bounds.Height)
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

    public void ScheduleScrollRestore()
    {
        int generation = ++_scrollRestoreGeneration;
        if (!_isAttached || !_host.FileList.IsEffectivelyVisible)
            return;

        Dispatcher.UIThread.Post(() => RestoreScrollAnchor(generation), DispatcherPriority.Loaded);
    }

    private void RestoreScrollAnchor(int generation)
    {
        if (generation != _scrollRestoreGeneration || !_isAttached || !_host.FileList.IsEffectivelyVisible || _scrollViewer == null || _viewModel == null)
            return;

        ScrollAnchor? anchor = _scrollAnchor;
        if (anchor == null)
            return;

        if (!string.IsNullOrEmpty(anchor.FullPath))
        {
            FileTreeDisplayItem? item = _viewModel.FlatItems.FirstOrDefault(candidate => PathsEqual(candidate.FullPath, anchor.FullPath));
            if (item != null)
            {
                _host.FileList.ScrollIntoView(item);
                _host.FileList.UpdateLayout();
                if (_host.FileList.ContainerFromItem(item) is ListBoxItem container)
                {
                    Point? point = container.TranslatePoint(new Point(0, 0), _host.FileList);
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

    public void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateStickyHeader();
    }

    private void UpdateStickyHeader()
    {
        if (_scrollViewer == null || !_host.FileList.IsEffectivelyVisible || _host.ViewModel is not FileTreeViewModel vm || vm.FlatItems.Count == 0)
        {
            _host.StickyHeader.IsVisible = false;
            return;
        }

        FileTreeDisplayItem? firstVisibleItem = null;
        int firstVisibleIndex = -1;
        for (int index = 0; index < vm.FlatItems.Count; index++)
        {
            FileTreeDisplayItem item = vm.FlatItems[index];
            if (_host.FileList.ContainerFromItem(item) is not ListBoxItem container)
                continue;

            Point? point = container.TranslatePoint(new Point(0, 0), _host.FileList);
            if (point == null)
                continue;

            double top = point.Value.Y;
            if (top + container.Bounds.Height <= 0 || top >= _host.FileList.Bounds.Height)
                continue;

            firstVisibleItem = item;
            firstVisibleIndex = index;
            break;
        }

        if (firstVisibleItem == null)
        {
            _host.StickyHeader.IsVisible = false;
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
            if (_host.FileList.ContainerFromItem(item) is ListBoxItem container)
            {
                Point? point = container.TranslatePoint(new Point(0, 0), _host.FileList);
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
            _host.StickyHeader.IsVisible = true;
            _host.StickyHeaderText.Text = stickyItem.DisplayName;
            _host.StickyHeaderText.Margin = new Thickness(stickyItem.IndentPadding.Left, 0, 0, 0);
        }
        else
        {
            _host.StickyHeader.IsVisible = false;
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
}
