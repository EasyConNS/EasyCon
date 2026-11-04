using EzTesseract.Interop;
using System.Runtime.InteropServices;

namespace EzTesseract;

/// <summary>
/// 一次 OCR 处理的结果页。对应原 TesseractOCR.Page 的 capture 用到的子集。
/// 惰性识别：首次访问 <see cref="Text"/> 或 <see cref="MeanConfidence"/> 时才调用 Recognize。
/// </summary>
public sealed class Page : IDisposable
{
    private readonly Engine _engine;
    private readonly Pix.Image _image;
    private readonly int _imageGeneration;
    private bool _recognized;
    private bool _disposed;

    internal Page(Engine engine, Pix.Image image)
    {
        _engine = engine;
        _image = image;
        _imageGeneration = engine.ImageGeneration;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Page));
        if (_engine.Handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(Engine));
        // SetImage2 不拷贝、不持有 Pix：Pix 已释放后再识别即原生 use-after-free
        if (_image.Handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(Pix.Image), "Pix 已释放，本 Page 不可再识别");
        // 引擎已被用于新图像：SetImage2 使旧图数据失效，继续识别会读到新图的结果（静默串图）
        if (_engine.ImageGeneration != _imageGeneration)
            throw new InvalidOperationException("引擎已处理新图像，本 Page 已失效（请先取完结果再 Process 下一张）");
    }

    /// <summary>识别图像（幂等）。返回非 0 抛异常。</summary>
    private void Recognize()
    {
        ThrowIfDisposed();
        if (_recognized) return;

        if (TesseractDll.Recognize(_engine.Handle, IntPtr.Zero) != 0)
            throw new InvalidOperationException("Recognition of image failed");
        _recognized = true;
    }

    /// <summary>识别后的纯文本（UTF-8）。</summary>
    public string Text
    {
        get
        {
            Recognize();
            var ptr = TesseractDll.GetUTF8Text(_engine.Handle);
            if (ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return (Marshal.PtrToStringUTF8(ptr) ?? string.Empty).Trim('\n');
            }
            finally
            {
                TesseractDll.DeleteText(ptr);
            }
        }
    }

    /// <summary>平均置信度（0~1，对应原 TesseractOCR.Page.MeanConfidence）。</summary>
    public float MeanConfidence
    {
        get
        {
            Recognize();
            return TesseractDll.MeanTextConf(_engine.Handle) / 100f;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 仅清除识别结果与图像数据，引擎本身由持有者释放（与原 Page.Dispose 行为一致）
        if (_engine.Handle != IntPtr.Zero)
            TesseractDll.Clear(_engine.Handle);
        GC.SuppressFinalize(this);
    }

    ~Page() => Dispose();
}