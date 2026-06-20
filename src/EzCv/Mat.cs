using EzCv.Interop;

namespace EzCv;

/// <summary>
/// 托管 Mat 包装，API 兼容 OpenCvSharp.Mat。
/// 内部持有 ezcv_native 的 Mat 句柄，实现 IDisposable。
/// </summary>
/// <remarks>
/// 每个 native 调用后调用 GC.KeepAlive(this) 防止托管包装器在 P/Invoke 期间被 GC 回收
/// （参考 OpenCvSharp NativeMethods 模式）。
/// </remarks>
public class Mat : IDisposable
{
    internal IntPtr Handle { get; private set; }

    /// <summary>创建空 Mat。</summary>
    public Mat()
    {
        Handle = EzCvDll.MatCreate();
        EzCvError.ThrowIfAny();
    }

    /// <summary>创建指定大小和类型的 Mat。</summary>
    public Mat(int rows, int cols, int type)
    {
        Handle = EzCvDll.MatCreateSized(rows, cols, type);
        EzCvError.ThrowIfAny();
    }

    /// <summary>从已有 Mat 创建 ROI 视图（共享数据）。</summary>
    public Mat(Mat src, Rect roi)
    {
        Handle = EzCvDll.MatCreateRoi(src.Handle, roi.X, roi.Y, roi.Width, roi.Height);
        EzCvError.ThrowIfAny();
        GC.KeepAlive(src);
    }

    internal Mat(IntPtr handle, bool ownsHandle = true)
    {
        Handle = handle;
        _ownsHandle = ownsHandle;
    }

    private bool _ownsHandle = true;
    private bool _disposed;

    public int Width
    {
        get
        {
            var v = EzCvDll.MatWidth(Handle);
            GC.KeepAlive(this);
            return v;
        }
    }

    public int Height
    {
        get
        {
            var v = EzCvDll.MatHeight(Handle);
            GC.KeepAlive(this);
            return v;
        }
    }

    public int Channels()
    {
        var v = EzCvDll.MatChannels(Handle);
        GC.KeepAlive(this);
        return v;
    }

    public int Type()
    {
        var v = EzCvDll.MatType(Handle);
        GC.KeepAlive(this);
        return v;
    }

    /// <summary>Mat 的维度数（2D 图像为 2，DNN blob 通常为 4）。</summary>
    public int Dims
    {
        get
        {
            var v = EzCvDll.MatDims(Handle);
            GC.KeepAlive(this);
            return v;
        }
    }

    /// <summary>获取指定维度的尺寸（dim 0 = rows/batch, dim 1 = cols/channels, dim 2+ = 更高维度）。</summary>
    public int Size(int dim)
    {
        var v = EzCvDll.MatSizeDim(Handle, dim);
        GC.KeepAlive(this);
        return v;
    }

    public unsafe IntPtr Data
    {
        get
        {
            var v = (IntPtr)EzCvDll.MatData(Handle);
            GC.KeepAlive(this);
            return v;
        }
    }

    public long Step()
    {
        var v = EzCvDll.MatStep(Handle);
        GC.KeepAlive(this);
        return v;
    }

    public bool Empty()
    {
        var v = EzCvDll.MatEmpty(Handle) != 0;
        GC.KeepAlive(this);
        return v;
    }

    public bool IsDisposed => _disposed;

    /// <summary>深拷贝。</summary>
    public Mat Clone()
    {
        var h = EzCvDll.MatClone(Handle);
        EzCvError.ThrowIfAny();
        GC.KeepAlive(this);
        return new Mat(h, ownsHandle: true);
    }

    /// <summary>类型转换。</summary>
    public void ConvertTo(Mat dst, int type)
    {
        EzCvDll.MatConvertTo(Handle, dst.Handle, type);
        EzCvError.ThrowIfAny();
        GC.KeepAlive(this);
        GC.KeepAlive(dst);
    }

    /// <summary>将 Mat 编码为指定格式的字节数组（默认 PNG）。</summary>
    public byte[] ToBytes(string ext = ".png") => Cv2.ImEncode(ext, this);

    /// <summary>从图像字节数组解码为 Mat（默认彩色）。</summary>
    public static Mat FromImageData(byte[] bytes, ImreadModes mode = ImreadModes.Color) => Cv2.ImDecode(bytes, mode);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHandle && Handle != IntPtr.Zero)
        {
            EzCvDll.MatRelease(Handle);
            Handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}