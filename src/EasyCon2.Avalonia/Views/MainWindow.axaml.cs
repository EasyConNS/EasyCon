using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EC.Avalonia.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace EasyCon2.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly string VER = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
    private ScrollViewer? _logScrollViewer;

    public MainWindow()
    {
        InitializeComponent();
        SetTitleWithVersion();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var textBox = this.FindControl<TextBox>("LogTextBox");
        if (textBox != null)
            _logScrollViewer = GetScrollViewer(textBox);
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
            if (_logScrollViewer != null)
            {
                _logScrollViewer.Offset = new global::Avalonia.Point(
                    _logScrollViewer.Offset.X,
                    Math.Max(0, _logScrollViewer.Extent.Height - _logScrollViewer.Viewport.Height + 20));
            }
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
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        // 仅接受文件拖放
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // 持续判断拖拽状态
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files is null) return;

            if (DataContext is MainWindowViewModel vm)
            {
                var file = files.FirstOrDefault();
                if (file != null)
                {
                    var path = file.Path.LocalPath;
                    vm.OpenScriptFromPath(path);
                }
            }
        }
    }
}