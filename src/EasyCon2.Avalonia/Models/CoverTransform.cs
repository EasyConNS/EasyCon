namespace EasyCon2.Avalonia.Models;

/// <summary>
/// 等比裁切（<see cref="ImageFit.Cover"/>）时的取景调整。恒等变换与普通居中等比裁切完全一致。
/// </summary>
/// <param name="AngleDegrees">顺时针旋转角度。基础缩放会随角度放大，保证居中时旋转后的图仍铺满画布。</param>
/// <param name="Zoom">在铺满画布的基础上再放大的倍数（≥ 1）。</param>
/// <param name="PanX">水平取景位置，-1 = 贴左边、0 = 居中、1 = 贴右边。</param>
/// <param name="PanY">垂直取景位置，-1 = 贴上边、0 = 居中、1 = 贴下边。</param>
public sealed record CoverTransform(double AngleDegrees, double Zoom, double PanX, double PanY)
{
    public static readonly CoverTransform Identity = new(0, 1, 0, 0);

    public bool IsIdentity => this == Identity;
}