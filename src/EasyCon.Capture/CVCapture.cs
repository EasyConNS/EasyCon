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