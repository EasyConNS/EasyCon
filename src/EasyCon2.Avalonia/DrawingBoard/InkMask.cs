using EasyCon2.Avalonia.Models;
using System.Drawing;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>抖动算法：用墨点疏密表现明暗。下拉框按此顺序列出。</summary>
public enum DitherMode
{
    /// <summary>不抖动，按阈值直接分黑白。</summary>
    None,
    /// <summary>经典误差扩散，四个邻居，逐行从左往右扫。</summary>
    FloydSteinberg,
    /// <summary>同 Floyd–Steinberg，但奇数行从右往左扫，减少误差总往一个方向推出的斜纹。</summary>
    FloydSteinbergSerpentine,
    /// <summary>只扩散 3/4 的误差：浅色区域点更少、对比更强，墨点总数通常最少，画得最快。</summary>
    Atkinson,
    /// <summary>扩散到 12 个邻居，过渡最平滑，计算量最大。</summary>
    JarvisJudiceNinke,
    /// <summary>与 Jarvis–Judice–Ninke 范围相同、权重不同，略锐利。</summary>
    Stucki,
    /// <summary>8×8 Bayer 矩阵有序抖动：按固定网格判断，图案规整、没有纹理。</summary>
    Bayer,
}

/// <summary>
/// 二值化参数。<paramref name="ThresholdPercent"/> 为明暗分界（0～100，占最大亮度的百分比）；
/// <paramref name="Dither"/> 为抖动算法，灰度图效果更好，但墨点更碎、绘制更久。
/// </summary>
public sealed record BinarizeOptions(int ThresholdPercent, bool Reverse, DitherMode Dither = DitherMode.None);

/// <summary>画布上每个点是否需要落墨。绘制只看掩码，预览也直接显示掩码，所见即所画。</summary>
public sealed class InkMask
{
    /// <summary>R+G+B 的最大值；量化后的点要么是 0（墨）要么是它（纸）。</summary>
    private const double White = 255 * 3;

    /// <summary>误差扩散的一个去向：相对当前点的偏移（向扫描方向为正）与所占比例。</summary>
    private readonly record struct Spread(int Dx, int Dy, double Weight);

    private static readonly Spread[] FloydSteinbergKernel =
    [
        new(1, 0, 7 / 16.0),
        new(-1, 1, 3 / 16.0), new(0, 1, 5 / 16.0), new(1, 1, 1 / 16.0),
    ];

    // 6 个去向各 1/8，合计只扩散 3/4 的误差
    private static readonly Spread[] AtkinsonKernel =
    [
        new(1, 0, 1 / 8.0), new(2, 0, 1 / 8.0),
        new(-1, 1, 1 / 8.0), new(0, 1, 1 / 8.0), new(1, 1, 1 / 8.0),
        new(0, 2, 1 / 8.0),
    ];

    private static readonly Spread[] JarvisJudiceNinkeKernel =
    [
        new(1, 0, 7 / 48.0), new(2, 0, 5 / 48.0),
        new(-2, 1, 3 / 48.0), new(-1, 1, 5 / 48.0), new(0, 1, 7 / 48.0), new(1, 1, 5 / 48.0), new(2, 1, 3 / 48.0),
        new(-2, 2, 1 / 48.0), new(-1, 2, 3 / 48.0), new(0, 2, 5 / 48.0), new(1, 2, 3 / 48.0), new(2, 2, 1 / 48.0),
    ];

    private static readonly Spread[] StuckiKernel =
    [
        new(1, 0, 8 / 42.0), new(2, 0, 4 / 42.0),
        new(-2, 1, 2 / 42.0), new(-1, 1, 4 / 42.0), new(0, 1, 8 / 42.0), new(1, 1, 4 / 42.0), new(2, 1, 2 / 42.0),
        new(-2, 2, 1 / 42.0), new(-1, 2, 2 / 42.0), new(0, 2, 4 / 42.0), new(1, 2, 2 / 42.0), new(2, 2, 1 / 42.0),
    ];

    private static readonly int[,] BayerMatrix =
    {
        { 0, 32, 8, 40, 2, 34, 10, 42 },
        { 48, 16, 56, 24, 50, 18, 58, 26 },
        { 12, 44, 4, 36, 14, 46, 6, 38 },
        { 60, 28, 52, 20, 62, 30, 54, 22 },
        { 3, 35, 11, 43, 1, 33, 9, 41 },
        { 51, 19, 59, 27, 49, 17, 57, 25 },
        { 15, 47, 7, 39, 13, 45, 5, 37 },
        { 63, 31, 55, 23, 61, 29, 53, 21 },
    };

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

    /// <summary>按阈值（及可选的抖动）把图片转成掩码；透明部分按白底合成后再判断。反转在量化之后取反，不改变点的分布。</summary>
    public static InkMask Binarize(PixelImage image, BinarizeOptions options)
    {
        int width = image.Width;
        int height = image.Height;
        // 统一在 R+G+B（0～765）上比较，各算法用同一个分界
        double threshold = White * options.ThresholdPercent / 100.0;
        double[] brightness = new double[width * height];
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                brightness[row * width + col] = Brightness(image.GetPixel(col, row));

        bool[] dark = options.Dither switch
        {
            DitherMode.Bayer => OrderedDither(brightness, width, height, threshold),
            DitherMode.None => Diffuse(brightness, width, height, threshold, [], serpentine: false),
            DitherMode.FloydSteinberg => Diffuse(brightness, width, height, threshold, FloydSteinbergKernel, serpentine: false),
            DitherMode.FloydSteinbergSerpentine => Diffuse(brightness, width, height, threshold, FloydSteinbergKernel, serpentine: true),
            DitherMode.Atkinson => Diffuse(brightness, width, height, threshold, AtkinsonKernel, serpentine: false),
            DitherMode.JarvisJudiceNinke => Diffuse(brightness, width, height, threshold, JarvisJudiceNinkeKernel, serpentine: false),
            DitherMode.Stucki => Diffuse(brightness, width, height, threshold, StuckiKernel, serpentine: false),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Dither, null),
        };

        bool[] ink = new bool[dark.Length];
        for (int i = 0; i < dark.Length; i++)
            ink[i] = dark[i] != options.Reverse;
        return new InkMask(width, height, ink);
    }

    /// <summary>
    /// 误差扩散：逐点按阈值量化，把"原亮度 − 量化值"按 <paramref name="kernel"/> 分给尚未处理的邻居，超出画布的部分丢弃。
    /// 空 kernel 即不抖动。蛇形扫描时奇数行从右往左，kernel 的水平方向随之镜像。
    /// </summary>
    private static bool[] Diffuse(double[] brightness, int width, int height, double threshold, Spread[] kernel, bool serpentine)
    {
        bool[] dark = new bool[brightness.Length];
        for (int row = 0; row < height; row++)
        {
            bool rightToLeft = serpentine && row % 2 == 1;
            for (int step = 0; step < width; step++)
            {
                int col = rightToLeft ? width - 1 - step : step;
                int i = row * width + col;
                dark[i] = brightness[i] < threshold;
                double error = brightness[i] - (dark[i] ? 0 : White);
                foreach (Spread spread in kernel)
                {
                    int c = col + (rightToLeft ? -spread.Dx : spread.Dx);
                    int r = row + spread.Dy;
                    if (c >= 0 && c < width && r < height)
                        brightness[r * width + c] += error * spread.Weight;
                }
            }
        }
        return dark;
    }

    /// <summary>
    /// Bayer 有序抖动：每个点的分界按其在 8×8 矩阵中的位置上下浮动，均值仍是 <paramref name="threshold"/>。
    /// 50% 时纯黑、纯白不受影响。
    /// </summary>
    private static bool[] OrderedDither(double[] brightness, int width, int height, double threshold)
    {
        bool[] dark = new bool[brightness.Length];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                double offset = ((BayerMatrix[row % 8, col % 8] + 0.5) / 64 - 0.5) * White;
                int i = row * width + col;
                dark[i] = brightness[i] < threshold + offset;
            }
        }
        return dark;
    }

    /// <summary>黑白互换：橡皮擦模式下要擦的正是原本留空的点。</summary>
    public InkMask Inverted() => new(Width, Height, Array.ConvertAll(_ink, ink => !ink));

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