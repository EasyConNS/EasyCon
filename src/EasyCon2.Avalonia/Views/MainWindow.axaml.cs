using Avalonia.Controls;
using Avalonia.Input;
using EasyCon2.Avalonia.ViewModels;
using System.ComponentModel;

namespace EasyCon2.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OnMainWindowClosing();
    }

    // 日志区工具条指针进出 —— 纯 UI 逻辑，保留在 code-behind
    private void LogBox_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsLogToolbarVisible = true;
    }

    private void LogBox_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsLogToolbarVisible = false;
    }
}