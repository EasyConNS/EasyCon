using EzCv.Interop;

namespace EzCv;

/// <summary>
/// 托管 VideoCapture 包装，API 兼容 OpenCvSharp.VideoCapture。
/// </summary>
public class VideoCapture : IDisposable
{
    internal IntPtr Handle { get; private set; }
    private bool _disposed;

    /// <summary>创建空 VideoCapture。</summary>
    public VideoCapture()
    {
        Handle = EzCvDll.VcCreateDefault();
        EzCvError.ThrowIfAny();
    }

    /// <summary>创建并打开指定设备。</summary>
    public VideoCapture(int index, VideoCaptureAPIs api = VideoCaptureAPIs.ANY)
    {
        Handle = EzCvDll.VcCreateIndex(index, (int)api);
        EzCvError.ThrowIfAny();
    }

    public bool IsOpened()
    {
        return EzCvDll.VcIsOpened(Handle) != 0;
    }

    public bool Open(int index, VideoCaptureAPIs api = VideoCaptureAPIs.ANY)
    {
        bool ok = EzCvDll.VcOpen(Handle, index, (int)api) != 0;
        // cv::Exception（如后端初始化失败）抛 OpenCVException；常规 open 失败（设备不存在）
        // 返回 false 不算异常，不进 last_error。
        EzCvError.ThrowIfAny();
        return ok;
    }

    public bool Read(Mat mat)
    {
        bool ok = EzCvDll.VcRead(Handle, mat.Handle) != 0;
        // 流末尾无新帧返回 false，不算错误；底层 cv::Exception 才抛。
        EzCvError.ThrowIfAny();
        return ok;
    }

    public bool Set(VideoCaptureProperties prop, double value)
    {
        bool ok = EzCvDll.VcSet(Handle, (int)prop, value) != 0;
        EzCvError.ThrowIfAny();
        return ok;
    }

    public bool Set(VideoCaptureProperties prop, int value)
    {
        bool ok = EzCvDll.VcSet(Handle, (int)prop, value) != 0;
        EzCvError.ThrowIfAny();
        return ok;
    }

    public double Get(VideoCaptureProperties prop)
    {
        var v = EzCvDll.VcGet(Handle, (int)prop);
        EzCvError.ThrowIfAny();
        return v;
    }

    public void Release()
    {
        EzCvDll.VcRelease(Handle);
        Handle = IntPtr.Zero;
    }

    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            EzCvDll.VcRelease(Handle);
            Handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}