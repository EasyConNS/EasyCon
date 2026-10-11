using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.Models;

namespace EasyCon2.Avalonia.FileTree;

public partial class FileTreeView : UserControl, IScrollAnchorHost
{
    private readonly FileTreeScrollAnchorBehavior _scrollAnchorBehavior;
    private ScrollViewer? _scrollViewer;
    private readonly List<Visual> _visibilityWatchers = [];

    public FileTreeView()
    {
        SortMenuGroupName = Guid.NewGuid().ToString("N");
        _scrollAnchorBehavior = new FileTreeScrollAnchorBehavior(this);
        InitializeComponent();
        DataContextChanged += (_, _) => _scrollAnchorBehavior.AttachViewModel();
        AttachedToVisualTree += (_, _) =>
        {
            _scrollAnchorBehavior.Attach();
            _scrollAnchorBehavior.AttachViewModel();
            AttachScrollViewer();
            AttachVisibilityWatchers();
            _scrollAnchorBehavior.ScheduleScrollRestoreIfPending();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _scrollAnchorBehavior.Detach();
            _scrollViewer = null;
            DetachVisibilityWatchers();
        };
        FileList.Loaded += (_, _) =>
        {
            AttachScrollViewer();
            _scrollAnchorBehavior.ScheduleScrollRestoreIfPending();
        };
        FileList.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
                _scrollAnchorBehavior.HandleListVisibilityChanged();
        };
    }

    public string SortMenuGroupName { get; }

    ListBox IScrollAnchorHost.FileList => FileList;
    ScrollViewer? IScrollAnchorHost.ScrollViewer => _scrollViewer;
    Border IScrollAnchorHost.StickyHeader => StickyHeader;
    TextBlock IScrollAnchorHost.StickyHeaderText => StickyHeaderText;
    FileTreeViewModel? IScrollAnchorHost.ViewModel => DataContext as FileTreeViewModel;

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
            _scrollAnchorBehavior.HandleListVisibilityChanged();
    }

    private void AttachScrollViewer()
    {
        // ScrollViewer 的视觉树查询必须留在控件内部；找到后交给 Behavior 统一订阅/退订。
        _scrollAnchorBehavior.DetachScrollViewer();
        _scrollViewer = FileList.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer != null)
            _scrollAnchorBehavior.AttachScrollViewer(_scrollViewer);
    }

    /// <summary>单击目录项切换展开/折叠。</summary>
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

    /// <summary>双击文件项打开。</summary>
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
