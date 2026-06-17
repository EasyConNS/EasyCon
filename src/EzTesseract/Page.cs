using System.Runtime.InteropServices;
using EzTesseract.Interop;

namespace EzTesseract;

/// <summary>
/// 一次 OCR 处理的结果页。对应原 TesseractOCR.Page 的 capture 用到的子集。
/// 惰性识别：首次访问 <see cref="Text"/> 或 <see cref="MeanConfidence"/> 时才调用 Recognize。
/// </summary>
public sealed class Page : IDisposable
{
    private readonly Engine _engine;
    private bool _recognized;
    private bool _disposed;

    internal Page(Engine engine) => _engine = engine;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Page));
        if (_engine.Handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(Engine));
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
