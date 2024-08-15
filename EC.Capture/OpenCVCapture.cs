using OpenCvSharp;
using Tesseract;

namespace EC.Capture;

internal class OpenCVCapture
{
    private readonly VideoCapture videoCapture = new();

    public OpenCVCapture(int idx)
    {
        var ok = videoCapture.Open(idx);
        if (ok)
        {
            // TODO
        }
    }

    public Mat GetFrame()
    {
        if (videoCapture.IsOpened())
        {
            using var m = videoCapture.RetrieveMat();
            if (!m.Empty())
            {
                m.ToBytes();
                string strbaser64 = Convert.ToBase64String(m.ToBytes());
            }
        }
        
        return new Mat(); // should check m.Empty()
    }
}
