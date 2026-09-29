using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.ViewModels;

namespace EasyCon2.Avalonia.Views;

public partial class FileTreeView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private FileTreeViewModel? _viewModel;
    private ScrollAnchor? _scrollAnchor;
    private int _scrollRestoreGeneration;
    private bool _isAttached;
    private bool _isViewModelSubscribed;

    private sealed record ScrollAnchor(string? FullPath, double RelativeTop, double ScrollOffset, bool WasSelectedVisible);

    public FileTreeView()
    {
        InitializeComponent();
        SortMenuGroupName = Guid.NewGuid().ToString("N");
        DataContextChanged += (_, _) => AttachViewModel();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            AttachViewModel();
            AttachScrollViewer();
            if (_scrollAnchor != null && FileList.IsVisible)
                ScheduleScrollRestore();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            _scrollRestoreGeneration++;
            DetachViewModel();
            DetachScrollViewer();
        };
        FileList.Loaded += OnFileListLoaded;
        FileList.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && FileList.IsVisible && _scrollAnchor != null)
                ScheduleScrollRestore();
        };
    }

    public string SortMenuGroupName { get; }

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
        if (_scrollAnchor != null && FileList.IsVisible)
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

            firstVisibleItem ??= item;
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
        if (!_isAttached || !FileList.IsVisible)
            return;

        Dispatcher.UIThread.Post(() => RestoreScrollAnchor(generation), DispatcherPriority.Loaded);
    }

    private void RestoreScrollAnchor(int generation)
    {
        if (generation != _scrollRestoreGeneration || !_isAttached || !FileList.IsVisible || _scrollViewer == null || _viewModel == null)
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
        if (_scrollViewer == null || DataContext is not FileTreeViewModel vm || vm.FlatItems.Count == 0)
        {
            StickyHeader.IsVisible = false;
            return;
        }

        var offset = _scrollViewer.Offset.Y;
        FileTreeDisplayItem? stickyItem = null;

        // 找到最后一个已滚出顶部的展开目录
        foreach (var item in vm.FlatItems)
        {
            var container = FileList.ContainerFromItem(item) as ListBoxItem;
            if (container == null) continue;

            var containerTop = container.TranslatePoint(new Point(0, 0), FileList)?.Y ?? 0;
            if (containerTop + container.Bounds.Height <= 0) continue;

            if (item.IsDirectory && item.IsExpanded)
            {
                if (containerTop < 0)
                    stickyItem = item;
                else
                    break;
            }
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