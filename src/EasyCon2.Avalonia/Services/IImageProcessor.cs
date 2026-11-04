using EasyCon2.Avalonia.Models;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 与 UI 框架无关的图像处理接口，PNG 字节进出。
/// ViewModel 通过本接口完成裁剪/加载等像素操作，避免持有
/// RenderTargetBitmap 之类的渲染对象；GUI 宿主提供实现。
/// </summary>
public interface IImageProcessor
{
    /// <summary>解码图像字节并裁剪指定区域，重新编码为 PNG 返回；失败返回 null。</summary>
    byte[]? Crop(byte[] sourceBytes, int x, int y, int width, int height);

    /// <summary>从文件加载图像并编码为 PNG 字节；失败返回 null。</summary>
    byte[]? LoadAsPng(string filePath);

    /// <summary>从文件解码为 RGBA 非预乘像素（供逐像素读取）；失败返回 null。</summary>
    PixelImage? LoadPixels(string filePath);

    /// <summary>
    /// 按 <paramref name="fit"/> 把图片缩放进 <paramref name="width"/>×<paramref name="height"/> 的画布，未覆盖处透明。
    /// <paramref name="cover"/> 只在 <see cref="ImageFit.Cover"/> 时生效（任意角度旋转、缩放、取景位置）。
    /// <paramref name="nearest"/> 为最近邻采样（像素画放大不出现灰色过渡），否则为平滑的 Mitchell 三次插值。
    /// 尺寸已一致且无取景调整时原样返回，不做重采样。
    /// </summary>
    PixelImage FitTo(PixelImage source, int width, int height, ImageFit fit, CoverTransform? cover = null, bool nearest = false);

    /// <summary>把像素按原尺寸编码为 PNG 写入文件；失败返回 false。</summary>
    bool SavePng(PixelImage image, string filePath);
}