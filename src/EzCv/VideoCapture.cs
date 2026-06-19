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
    }

    /// <summary>创建并打开指定设备。</summary>
    public VideoCapture(int index, VideoCaptureAPIs api = VideoCaptureAPIs.ANY)
    {
        Handle = EzCvDll.VcCreateIndex(index, (int)api);
    }

    public bool IsOpened()
    {
        return EzCvDll.VcIsOpened(Handle) != 0;
    }

    public bool Open(int index, VideoCaptureAPIs api = VideoCaptureAPIs.ANY)
    {
        return EzCvDll.VcOpen(Handle, index, (int)api) != 0;
    }

    public bool Read(Mat mat)
    {
        return EzCvDll.VcRead(Handle, mat.Handle) != 0;
    }

    public bool Set(VideoCaptureProperties prop, double value)
    {
        return EzCvDll.VcSet(Handle, (int)prop, value) != 0;
    }

    public bool Set(VideoCaptureProperties prop, int value)
    {
        return EzCvDll.VcSet(Handle, (int)prop, value) != 0;
    }

    public double Get(VideoCaptureProperties prop)
    {
        return EzCvDll.VcGet(Handle, (int)prop);
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