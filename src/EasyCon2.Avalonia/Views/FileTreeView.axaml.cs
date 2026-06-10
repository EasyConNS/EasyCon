using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.ViewModels;

namespace EasyCon2.Avalonia.Views;

public partial class FileTreeView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public FileTreeView()
    {
        InitializeComponent();
        FileList.Loaded += OnFileListLoaded;
    }

    private void OnFileListLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _scrollViewer = FileList.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
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