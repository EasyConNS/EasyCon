using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EasyCon2.Avalonia.Controls;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Scripting;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// IDialogService 实现 —— 封装文件对话框、颜色选择器和脚本参数对话框，
/// 并负责 Core 层 DTO（FileDialogFilter/DialogColor）与 Avalonia 类型之间的转换。
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>
    /// 对话框的 Owner，由 App 在初始化时注入。
    /// </summary>
    public Window? Owner { get; set; }


    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title,
        IReadOnlyList<FileDialogFilter>? filters = null,
        string? suggestedStartPath = null)
    {
        var mainWindow = Owner;
        if (mainWindow == null) return Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters?.Select(ToFileType).ToList()
        };

        if (suggestedStartPath != null)
        {
            var startFolder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(suggestedStartPath);
            options.SuggestedStartLocation = startFolder;
        }

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> SaveFileAsync(string title,
        string defaultExtension,
        IReadOnlyList<FileDialogFilter>? filters = null,
        string? suggestedFileName = null)
    {
        var mainWindow = Owner;
        if (mainWindow == null) return null;

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = filters?.Select(ToFileType).ToList(),
            SuggestedFileName = suggestedFileName
        });

        return file?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderAsync(string title,
        string? suggestedStartPath = null)
    {
        var mainWindow = Owner;
        if (mainWindow == null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (suggestedStartPath != null)
        {
            var startFolder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(suggestedStartPath);
            options.SuggestedStartLocation = startFolder;
        }

        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<DialogColor?> PickColorAsync(DialogColor current)
    {
        var owner = Owner;
        if (owner == null) return null;

        var avaloniaColor = new Color(current.A, current.R, current.G, current.B);
        var popup = new ColorPickerPopup(avaloniaColor);
        var picked = await popup.ShowDialog<Color?>(owner);
        return picked == null ? null : new DialogColor(picked.Value.A, picked.Value.R, picked.Value.G, picked.Value.B);
    }

    public async Task<string[]?> ShowScriptArgsDialogAsync()
    {
        var owner = Owner;
        if (owner == null) return null;

        var dialog = new ScriptArgsWindow();
        await dialog.ShowDialog<string[]?>(owner);
        return dialog.Args;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = Owner;
        if (owner == null) return false;

        var confirmed = false;
        var okButton = new Button { Content = "确定", MinWidth = 88, Margin = new Thickness(0, 0, 12, 0) };
        var cancelButton = new Button { Content = "取消", MinWidth = 88 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380
        });
        content.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = content
        };

        okButton.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);
        return confirmed;
    }

    private static FilePickerFileType ToFileType(FileDialogFilter filter)
        => new(filter.Name) { Patterns = [.. filter.Patterns] };
}