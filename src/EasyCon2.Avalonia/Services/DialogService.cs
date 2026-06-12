using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EasyCon2.Avalonia.Controls;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// IDialogService 实现 —— 封装文件对话框和颜色选择器。
/// </summary>
public class DialogService : IDialogService
{
    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? suggestedStartPath = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
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
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? suggestedFileName = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = filters,
            SuggestedFileName = suggestedFileName
        });

        return file?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderAsync(string title,
        string? suggestedStartPath = null)
    {
        var mainWindow = GetMainWindow();
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

    public async Task<Color?> PickColorAsync(Color current)
    {
        var owner = GetMainWindow();
        if (owner == null) return null;

        var popup = new ColorPickerPopup(current);
        return await popup.ShowDialog<Color?>(owner);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
