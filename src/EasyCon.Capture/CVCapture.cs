using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon.Capture;

public class OpenCVCapture(int idx = 0, VideoCaptureAPIs apiRefs = VideoCaptureAPIs.ANY) : IDisposable
{
    private readonly VideoCapture videoCapture = new();

    private int deviceId = idx;
    private VideoCaptureAPIs refs = apiRefs;

    public bool IsOpened => videoCapture.IsOpened();

    public bool Open(int idx, int apiRefs = 0)
    {
        deviceId = idx;
        refs = (VideoCaptureAPIs)apiRefs;
        return videoCapture.Open(deviceId, refs);
    }

    public void SetProperties(int x, int y)
    {
        SetResolution(x, y);
        SetProperties();
    }
    public void SetResolution(int x, int y)
    {
        videoCapture.Set(VideoCaptureProperties.FrameWidth, x);
        videoCapture.Set(VideoCaptureProperties.FrameHeight, y);
    }

    public void SetProperties()
    {
        videoCapture.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
        videoCapture.Set(VideoCaptureProperties.Fps, 30);
    }

    public void GetProperties()
    {
        Debug.WriteLine($"Actual Width: {videoCapture.Get(VideoCaptureProperties.FrameWidth)}");
        Debug.WriteLine($"Actual Height: {videoCapture.Get(VideoCaptureProperties.FrameHeight)}");
        Debug.WriteLine($"FourCC: {videoCapture.Get(VideoCaptureProperties.FourCC)}");
        Debug.WriteLine($"FPS: {videoCapture.Get(VideoCaptureProperties.Fps)}");
        Debug.WriteLine($"Backend: {videoCapture.Get(VideoCaptureProperties.Backend)}");
    }

    public Mat GetMatFrame()
    {
        if (videoCapture.IsOpened())
        {
            var mat = new Mat();
            // Read() 失败时 mat 本身就是空的，直接返回即可
            videoCapture.Read(mat);
            return mat;
        }

        return new Mat();
    }

    /// <summary>
    /// 保证获取一帧新数据：双次 Grab 消费缓冲区旧帧，再 Retrieve 解码新帧。
    /// 调用开销约为 GetMatFrame 的两倍，仅在需要帧新鲜度保证时使用（如脚本执行）。
    /// </summary>
    public Mat GetFreshFrame()
    {
        if (videoCapture.IsOpened())
        {
            videoCapture.Grab();  // 丢弃缓冲区当前帧（可能是旧帧）
            videoCapture.Grab();  // 等待硬件产出新帧
            var mat = new Mat();
            videoCapture.Retrieve(mat);
            return mat;
        }

        return new Mat();
    }

    public void Release()
    {
        videoCapture.Release();
    }

    public void Dispose()
    {
        if (!videoCapture.IsDisposed)
        {
            videoCapture.Dispose();
        }
    }
}