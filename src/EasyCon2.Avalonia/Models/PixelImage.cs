using System.Drawing;

namespace EasyCon2.Avalonia.Models;

/// <summary>
/// 已解码的位图像素（RGBA8888、非预乘），与 UI 框架无关。
/// <see cref="GetPixel"/> 的语义对齐 System.Drawing.Bitmap.GetPixel：越界抛 <see cref="ArgumentOutOfRangeException"/>。
/// </summary>
public sealed class PixelImage
{
    private readonly byte[] _rgba;

    public PixelImage(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            throw new ArgumentException("像素数据与尺寸不符");
        Width = width;
        Height = height;
        _rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public Color GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        int i = (y * Width + x) * 4;
        return Color.FromArgb(_rgba[i + 3], _rgba[i], _rgba[i + 1], _rgba[i + 2]);
    }
}