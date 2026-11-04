namespace EasyCon2.Avalonia.Terminal;

/// <summary>
/// 纯 RGB 颜色值，终端数据模型与解析层的颜色载体（无 Avalonia 依赖）；
/// View 层渲染时再转换为 Avalonia.Media.Color。
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B);