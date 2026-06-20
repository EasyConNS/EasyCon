using EzCv.Interop;

namespace EzCv;

/// <summary>
/// 托管 Mat 包装，API 兼容 OpenCvSharp.Mat。
/// 内部持有 ezcv_native 的 Mat 句柄，实现 IDisposable。
/// </summary>
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
    }

    internal Mat(IntPtr handle, bool ownsHandle = true)
    {
        Handle = handle;
        _ownsHandle = ownsHandle;
    }

    private bool _ownsHandle = true;
    private bool _disposed;

    public int Width => EzCvDll.MatWidth(Handle);
    public int Height => EzCvDll.MatHeight(Handle);
    public int Channels() => EzCvDll.MatChannels(Handle);
    public int Type() => EzCvDll.MatType(Handle);

    /// <summary>Mat 的维度数（2D 图像为 2，DNN blob 通常为 4）。</summary>
    public int Dims => EzCvDll.MatDims(Handle);

    /// <summary>获取指定维度的尺寸（dim 0 = rows/batch, dim 1 = cols/channels, dim 2+ = 更高维度）。</summary>
    public int Size(int dim) => EzCvDll.MatSizeDim(Handle, dim);

    public unsafe IntPtr Data => (IntPtr)EzCvDll.MatData(Handle);

    public long Step() => EzCvDll.MatStep(Handle);

    public bool Empty() => EzCvDll.MatEmpty(Handle) != 0;

    public bool IsDisposed => _disposed;

    /// <summary>深拷贝。</summary>
    public Mat Clone()
    {
        var h = EzCvDll.MatClone(Handle);
        EzCvError.ThrowIfAny();
        return new Mat(h, ownsHandle: true);
    }

    /// <summary>类型转换。</summary>
    public void ConvertTo(Mat dst, int type)
    {
        EzCvDll.MatConvertTo(Handle, dst.Handle, type);
        EzCvError.ThrowIfAny();
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