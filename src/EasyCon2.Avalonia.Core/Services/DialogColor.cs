namespace EasyCon2.Avalonia.Core.Services;

/// <summary>
/// 与 UI 框架无关的颜色值（颜色选择对话框的出入参）。
/// </summary>
public sealed record DialogColor(byte A, byte R, byte G, byte B);