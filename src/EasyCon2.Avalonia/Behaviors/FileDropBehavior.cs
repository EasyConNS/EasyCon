using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Behaviors;

/// <summary>
/// 文件拖放行为。将拖入的第一个文件路径作为 string 参数执行绑定的 Command。
/// 用法：behaviors:FileDropBehavior.Command="{Binding DropFileCommand}"
/// </summary>
public class FileDropBehavior
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<FileDropBehavior, Control, ICommand>("Command");

    private static readonly AttachedProperty<bool> _registeredProperty =
        AvaloniaProperty.RegisterAttached<FileDropBehavior, Control, bool>("_registered");

    static FileDropBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is not ICommand || e.NewValue == null) return;
            if (control.GetValue(_registeredProperty)) return;

            control.SetValue(_registeredProperty, true);
            DragDrop.SetAllowDrop(control, true);
            DragDrop.AddDragEnterHandler(control, OnDragEnter);
            DragDrop.AddDragOverHandler(control, OnDragOver);
            DragDrop.AddDropHandler(control, OnDrop);
        });
    }

    public static ICommand? GetCommand(Control element) => element.GetValue(CommandProperty);
    public static void SetCommand(Control element, ICommand value) => element.SetValue(CommandProperty, value);

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        var files = e.DataTransfer.TryGetFiles();
        var path = files?.FirstOrDefault()?.Path.LocalPath;
        if (path == null) return;

        var cmd = sender is Control c ? c.GetValue(CommandProperty) : null;
        if (cmd?.CanExecute(path) == true)
            cmd.Execute(path);
    }
}