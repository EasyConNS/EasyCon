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
        videoCapture.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
        videoCapture.Set(VideoCaptureProperties.Fps, 30);
        videoCapture.Set(VideoCaptureProperties.FrameWidth, x);
        videoCapture.Set(VideoCaptureProperties.FrameHeight, y);

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
            // 使用 Read() 而非 RetrieveMat()，因为 Read() 会自动执行 Grab() + Retrieve()
            // RetrieveMat() 需要先调用 Grab() 才能正确工作
            var mat = new Mat();
            if (videoCapture.Read(mat))
            {
                return mat;
            }
            mat.Dispose();
            return new Mat();
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
