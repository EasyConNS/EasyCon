using EasyCon.Capture.win;
using OpenCvSharp;

namespace EasyCon.Capture;

public sealed class ECCapture
{
    public static IEnumerable<(string name, int index)> GetCaptureCamera()
    {
        if (OperatingSystem.IsWindows())
        {
            var filters = DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory);
            return filters.Select((name, index) => (name, index));
        }

        // 非Windows平台：通过循环尝试连接摄像头来检测可用设备
        var availableCameras = new List<(string name, int index)>();

        // 尝试检测前 10 个摄像头设备
        for (int i = 0; i < 10; i++)
        {
            using var capture = new VideoCapture(i, VideoCaptureAPIs.ANY);
            if (capture.IsOpened())
            {
                // 获取摄像头名称（如果能获取到的话）
                string cameraName = $"摄像头 {i}";

                // 尝试获取一些摄像头信息
                try
                {
                    double backendName = capture.Get(VideoCaptureProperties.Backend);
                    double width = capture.Get(VideoCaptureProperties.FrameWidth);
                    double height = capture.Get(VideoCaptureProperties.FrameHeight);

                    if (width > 0 && height > 0)
                    {
                        cameraName += $" ({width}x{height})";
                    }
                }
                catch
                {
                    // 忽略获取信息时的错误
                }

                availableCameras.Add((cameraName, i));
            }
        }

        return availableCameras;
    }

    public static IEnumerable<(string, int)> GetCaptureTypes()
    {
        var values = Enum.GetValues<VideoCaptureAPIs>();

        foreach (var value in values)
        {
            yield return(value.ToString(), (int)value);
        }
    }
}
