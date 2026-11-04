namespace EasyCon2.Avalonia.Models;

/// <summary>图片放进固定尺寸画布的方式。</summary>
public enum ImageFit
{
    /// <summary>等比缩放到完整放进画布，空出的部分留空（透明）。</summary>
    Contain,
    /// <summary>等比缩放到铺满画布，超出的部分居中裁掉。</summary>
    Cover,
    /// <summary>直接拉伸到画布尺寸，可能变形。</summary>
    Stretch,
}