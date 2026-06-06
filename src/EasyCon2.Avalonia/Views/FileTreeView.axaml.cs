using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EasyCon2.Avalonia.ViewModels;

namespace EasyCon2.Avalonia.Views;

public partial class FileTreeView : UserControl
{
    public FileTreeView()
    {
        InitializeComponent();
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
