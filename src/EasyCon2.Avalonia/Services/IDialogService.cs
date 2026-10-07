using EasyCon2.Avalonia.Models;
namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 对话框服务抽象。签名只依赖本程序集的 DTO，不暴露具体 UI 框架类型；
/// 由 GUI 宿主实现并负责与框架对话框之间的转换。
/// </summary>
public interface IDialogService
{
    // 文件/文件夹对话框
    Task<IReadOnlyList<string>> OpenFilesAsync(string title,
        IReadOnlyList<FileDialogFilter>? filters = null,
        string? suggestedStartPath = null);

    Task<string?> SaveFileAsync(string title,
        string defaultExtension,
        IReadOnlyList<FileDialogFilter>? filters = null,
        string? suggestedFileName = null);

    Task<string?> OpenFolderAsync(string title,
        string? suggestedStartPath = null);

    // 颜色选择器
    Task<DialogColor?> PickColorAsync(DialogColor current);

    // 脚本参数对话框：取消/关闭返回 null
    Task<string[]?> ShowScriptArgsDialogAsync();

    /// <summary>通用确认对话框（确定/取消）。返回 true 表示用户点了确定。</summary>
    Task<bool> ConfirmAsync(string title, string message);
}