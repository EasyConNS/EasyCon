using OpenCvSharp;
using System.Diagnostics;

namespace EasyCapture;

public class OpenCVCapture : IDisposable
{
    private readonly VideoCapture videoCapture = new();

    public bool IsOpened => videoCapture.IsOpened();

    public bool Open(int idx, int apiRefs = 0)
    {
        return videoCapture.Open(idx, (VideoCaptureAPIs)apiRefs);
    }

    public void SetProperties(int x, int y)
    {
        videoCapture.Set(VideoCaptureProperties.FrameWidth, x);
        videoCapture.Set(VideoCaptureProperties.FrameHeight, y);
        videoCapture.Set(VideoCaptureProperties.Fps, 60);

        Debug.WriteLine(videoCapture.Get(VideoCaptureProperties.Mode));
        Debug.WriteLine(videoCapture.Get(VideoCaptureProperties.FourCC));
        Debug.WriteLine(videoCapture.Get(VideoCaptureProperties.Backend));
        Debug.WriteLine(videoCapture.Get(VideoCaptureProperties.Fps));
    }

    public Mat GetMatFrame()
    {
        if (videoCapture.IsOpened())
        {
            return videoCapture.RetrieveMat();
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
