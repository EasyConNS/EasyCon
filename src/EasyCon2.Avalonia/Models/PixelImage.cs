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

    /// <summary>逐行紧密排列的 RGBA8888（非预乘）像素。</summary>
    public ReadOnlySpan<byte> Rgba => _rgba.AsSpan(0, Width * Height * 4);

    public Color GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        int i = (y * Width + x) * 4;
        return Color.FromArgb(_rgba[i + 3], _rgba[i], _rgba[i + 1], _rgba[i + 2]);
    }

    /// <summary>
    /// 先顺时针旋转 <paramref name="quarterTurns"/> 个 90°，再按旋转后的方向水平/垂直翻转。
    /// 只搬动像素、不重采样；无需变换时原样返回。
    /// </summary>
    public PixelImage Orient(int quarterTurns, bool flipHorizontal, bool flipVertical)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0 && !flipHorizontal && !flipVertical)
            return this;

        bool swap = turns % 2 == 1;
        int width = swap ? Height : Width;
        int height = swap ? Width : Height;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                (int tx, int ty) = turns switch
                {
                    1 => (Height - 1 - y, x),
                    2 => (Width - 1 - x, Height - 1 - y),
                    3 => (y, Width - 1 - x),
                    _ => (x, y),
                };
                if (flipHorizontal)
                    tx = width - 1 - tx;
                if (flipVertical)
                    ty = height - 1 - ty;
                Array.Copy(_rgba, (y * Width + x) * 4, rgba, (ty * width + tx) * 4, 4);
            }
        }
        return new PixelImage(width, height, rgba);
    }
}