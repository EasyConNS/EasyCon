using OpenCvSharp;
using System.Diagnostics;

namespace EasyCapture;

public class OpenCVCapture : IDisposable
{
    private readonly VideoCapture videoCapture = new();

    public OpenCVCapture()
    {

    }

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

    public Mat GetFrame()
    {
        if (videoCapture.IsOpened())
        {
            return videoCapture.RetrieveMat();
            //_image?.Dispose();
            //_image = BitmapConverter.ToBitmap(_curMat);

            //if (!m.Empty()) // should check empty
            //{
            //    m.ToBytes();
            //    string strbaser64 = Convert.ToBase64String(m.ToBytes());
            //}
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
