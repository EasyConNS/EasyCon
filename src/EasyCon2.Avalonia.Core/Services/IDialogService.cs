using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace EasyCon2.Avalonia.Core.Services;

public interface IDialogService
{
    // 文件/文件夹对话框
    Task<IReadOnlyList<string>> OpenFilesAsync(string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? suggestedStartPath = null);

    Task<string?> SaveFileAsync(string title,
        string defaultExtension,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? suggestedFileName = null);

    Task<string?> OpenFolderAsync(string title,
        string? suggestedStartPath = null);

    // 颜色选择器
    Task<Color?> PickColorAsync(Color current);
}