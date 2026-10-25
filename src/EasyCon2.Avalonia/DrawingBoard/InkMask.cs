using EasyCon2.Avalonia.Models;
using System.Drawing;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>二值化参数。<paramref name="ThresholdPercent"/> 为明暗分界（0～100，占最大亮度的百分比）。</summary>
public sealed record BinarizeOptions(int ThresholdPercent, bool Reverse);

/// <summary>画布上每个点是否需要落墨。绘制只看掩码，预览也直接显示掩码，所见即所画。</summary>
public sealed class InkMask
{
    private readonly bool[] _ink;

    public InkMask(int width, int height, bool[] ink)
    {
        if (width <= 0 || height <= 0 || ink.Length != width * height)
            throw new ArgumentException("掩码数据与尺寸不符");
        Width = width;
        Height = height;
        _ink = ink;
    }

    public int Width { get; }

    public int Height { get; }

    public bool this[int col, int row] => _ink[row * Width + col];

    /// <summary>按阈值把图片转成掩码；透明部分按白底合成后再判断。</summary>
    public static InkMask Binarize(PixelImage image, BinarizeOptions options)
    {
        double threshold = 255 * 3 * options.ThresholdPercent / 100.0;
        bool[] ink = new bool[image.Width * image.Height];
        for (int row = 0; row < image.Height; row++)
        {
            for (int col = 0; col < image.Width; col++)
            {
                bool dark = Brightness(image.GetPixel(col, row)) < threshold;
                // 反转：画亮的部分
                ink[row * image.Width + col] = dark != options.Reverse;
            }
        }
        return new InkMask(image.Width, image.Height, ink);
    }

    /// <summary>黑 = 要画的点，白 = 留空。</summary>
    public PixelImage ToPreview()
    {
        byte[] rgba = new byte[Width * Height * 4];
        for (int i = 0; i < _ink.Length; i++)
        {
            byte value = _ink[i] ? (byte)0 : (byte)255;
            rgba[i * 4] = value;
            rgba[i * 4 + 1] = value;
            rgba[i * 4 + 2] = value;
            rgba[i * 4 + 3] = 255;
        }
        return new PixelImage(Width, Height, rgba);
    }

    /// <summary>
    /// 像素按白底合成后的 R+G+B（0～765）。旧版直接读 RGB，透明 PNG 的透明区域 RGB 通常为 0，整片背景被当成墨点；
    /// 不透明像素（含所有 JPG）结果与旧版相同。
    /// </summary>
    internal static int Brightness(Color color)
    {
        int alpha = color.A;
        int OverWhite(int channel) => (channel * alpha + 255 * (255 - alpha)) / 255;
        return OverWhite(color.R) + OverWhite(color.G) + OverWhite(color.B);
    }
}