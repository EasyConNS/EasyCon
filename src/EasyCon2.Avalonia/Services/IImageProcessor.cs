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
}