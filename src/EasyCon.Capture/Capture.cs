using EasyCon.Capture.win;
using FlashCap;
using OpenCvSharp;
using System.Diagnostics;

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
        var devices = new List<(string name, int index)>();

        try
        {
            // 使用 FlashCap 扫描所有视频捕获设备
            var captureDevices = new CaptureDevices();
            var descriptors = captureDevices.EnumerateDescriptors().ToList();

            for (int i = 0; i < descriptors.Count; i++)
            {
                var descriptor = descriptors[i];
                string deviceName = descriptor.ToString(); // 设备名称，如 "Logicool Webcam C930e: DirectShow device"

                // 尝试获取设备更多信息（第一个支持的分辨率）
                try
                {
                    var characteristics = descriptor.Characteristics;
                    if (characteristics.Length > 0)
                    {
                        var firstChar = characteristics[0];
                        deviceName += $" ({firstChar.Width}x{firstChar.Height})";
                    }
                }
                catch
                {
                    // 忽略获取详细信息时的错误
                }

                devices.Add((deviceName, i));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"扫描摄像头设备失败: {ex.Message}");
            // 降级到基础检测
            return GetFallbackCaptureCamera();
        }

        return devices;
    }

    private static IEnumerable<(string name, int index)> GetFallbackCaptureCamera()
    {
        var availableCameras = new List<(string name, int index)>();

        // 尝试检测前 10 个摄像头设备
        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var capture = new VideoCapture(i, VideoCaptureAPIs.ANY);
                if (capture.IsOpened())
                {
                    string cameraName = $"摄像头 {i}";

                    try
                    {
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
            catch
            {
                // 继续尝试下一个设备
            }
        }

        return availableCameras;
    }

    public static IEnumerable<(string, int)> GetCaptureTypes()
    {
        var values = Enum.GetValues<VideoCaptureAPIs>();

        foreach (var value in values)
        {
            yield return (value.ToString(), (int)value);
        }
    }
}