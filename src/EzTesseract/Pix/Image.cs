using System.Runtime.InteropServices;
using EzTesseract.Interop;

namespace EzTesseract.Pix;

/// <summary>
/// leptonica Pix 图像的托管包装。
/// 通过 <see cref="LoadFromMemory(byte[])"/> 从字节数组（PNG/JPEG/TIFF）解码；
/// 句柄所有权归本对象，<see cref="Dispose"/> 时调用 pixDestroy 释放。
/// </summary>
public sealed class Image : IDisposable
{
    /// <summary>原生 Pix 句柄，供 <see cref="Engine.Process"/> 传给 TessBaseAPISetImage2。</summary>
    public IntPtr Handle { get; private set; }

    private Image(IntPtr handle) => Handle = handle;

    /// <summary>从内存字节数组解码图像。</summary>
    /// <exception cref="IOException">解码失败（pixReadMem 返回 NULL）。</exception>
    public static Image LoadFromMemory(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return LoadFromMemory(bytes, 0, bytes.Length);
    }

    /// <summary>从内存字节数组的指定区间解码图像。</summary>
    public static unsafe Image LoadFromMemory(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || length < 0 || offset + length > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        IntPtr handle;
        fixed (byte* ptr = bytes)
            handle = LeptonicaDll.PixReadMem(ptr + offset, length);

        if (handle == IntPtr.Zero)
            throw new IOException("Failed to load image from memory (pixReadMem returned NULL)");

        return new Image(handle);
    }

    public void Dispose()
    {
        var h = Handle;
        if (h == IntPtr.Zero) return;
        Handle = IntPtr.Zero;
        LeptonicaDll.PixDestroy(ref h);
        GC.SuppressFinalize(this);
    }

    ~Image() => Dispose();
}
