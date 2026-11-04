namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 二值化前的明暗调整，作用在按白底合成后的亮度上（0～1）：先加亮度，再绕中灰拉伸对比度，最后做伽马。
/// <see cref="Identity"/> 不改变任何像素。
/// </summary>
/// <param name="Brightness">-100～100：整体变暗 / 变亮，100 时加满一档白。</param>
/// <param name="Contrast">-100～100：负值压平、正值拉开明暗差（接近 100 时近似直接按中灰分黑白）。</param>
/// <param name="Gamma">0.2～5：大于 1 提亮暗部、小于 1 压暗亮部，纯黑纯白不变。</param>
public sealed record ToneAdjust(int Brightness, int Contrast, double Gamma)
{
    public static readonly ToneAdjust Identity = new(0, 0, 1);

    public bool IsIdentity => this == Identity;

    /// <summary>把 0～1 的亮度按本调整映射，结果截到 0～1。</summary>
    public double Apply(double value)
    {
        double v = value + Brightness / 100.0;
        double factor = Contrast >= 0 ? 1 / Math.Max(0.01, 1 - Contrast / 100.0) : 1 + Contrast / 100.0;
        v = (v - 0.5) * factor + 0.5;
        v = Math.Clamp(v, 0, 1);
        return Math.Pow(v, 1 / Math.Max(0.01, Gamma));
    }
}