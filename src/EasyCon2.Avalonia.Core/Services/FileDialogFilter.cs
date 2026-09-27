namespace EasyCon2.Avalonia.Core.Services;

/// <summary>
/// 文件对话框过滤器（与 UI 框架无关的描述）。
/// </summary>
/// <param name="Name">过滤器显示名，如"图片文件"。</param>
/// <param name="Patterns">扩展名模式，如 ["*.png", "*.jpg"]。</param>
public sealed record FileDialogFilter(string Name, IReadOnlyList<string> Patterns);