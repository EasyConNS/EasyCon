using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EC.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EasyCon2.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly string VER = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
    public MainWindow()
    {
        InitializeComponent();
        SetTitleWithVersion();
        DataContextChanged += OnDataContextChanged;
    }

    private void SetTitleWithVersion()
    {
        string displayVersion = VER;
        var plusIndex = displayVersion.IndexOf('+');
        if (plusIndex > 0)
            displayVersion = displayVersion[..plusIndex];
        Title = $"伊机控 EasyCon v{displayVersion}  QQ群:946057081";
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.LogOutput))
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var textBox = this.FindControl<TextBox>("LogTextBox");
                if (textBox != null && textBox.IsAttachedToVisualTree())
                {
                    await Task.Delay(10);
                    var scrollViewer = GetScrollViewer(textBox);
                    if (scrollViewer != null)
                    {
                        scrollViewer.Offset = new global::Avalonia.Point(scrollViewer.Offset.X, Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height + 20));
                    }
                }
            });
        }
    }

    private ScrollViewer? GetScrollViewer(Control control)
    {
        if (control is ScrollViewer sv)
            return sv;

        var descendants = control.GetVisualDescendants().ToList();
        foreach (var descendant in descendants)
        {
            if (descendant is ScrollViewer scrollViewer)
                return scrollViewer;
        }
        return null;
    }

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

    private void SerialPortComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RefreshSerialPortsCommand.Execute(null);
    }

    private void CaptureSourceComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RefreshCaptureSourcesCommand.Execute(null);
    }

    private void ControlSourceComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RefreshControlSourcesCommand.Execute(null);
    }
}