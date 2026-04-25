using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EasyCon.Capture.macOS;

/// <summary>
/// 通过 ObjCRuntime P/Invoke 直接调用 AVFoundation API，
/// 在 macOS 上可靠地枚举摄像头和采集卡等视频输入设备。
/// </summary>
[SupportedOSPlatform("macos")]
static class AVFoundationDeviceEnumerator
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    #region ObjCRuntime P/Invoke

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlopen(string? path, int mode);

    #endregion

    private static readonly IntPtr _avfHandle;

    static AVFoundationDeviceEnumerator()
    {
        // RTLD_LAZY = 1, 加载 AVFoundation 框架
        _avfHandle = dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", 1);
    }

    private static IntPtr CreateNSString(string str)
    {
        var cls = objc_getClass("NSString");
        var obj = objc_msgSend(cls, sel_registerName("alloc"));
        var bytes = System.Text.Encoding.UTF8.GetBytes(str + "\0");
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return objc_msgSend(obj, sel_registerName("initWithUTF8String:"), handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static string? GetNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        var ptr = objc_msgSend(nsString, sel_registerName("UTF8String"));
        return Marshal.PtrToStringUTF8(ptr);
    }

    /// <summary>
    /// 枚举所有可用的视频输入设备（摄像头、USB 采集卡等）。
/// 设备索引与 OpenCvSharp 的 VideoCapture 索引一致，
/// 因为 OpenCV macOS 后端同样基于 AVFoundation。
    /// </summary>
    public static List<(string name, int index)> EnumerateVideoDevices()
    {
        var result = new List<(string name, int index)>();

        if (_avfHandle == IntPtr.Zero) return result;

        // AVMediaTypeVideo = "vide"
        var mediaType = CreateNSString("vide");
        var captureDeviceClass = objc_getClass("AVCaptureDevice");
        var deviceArray = objc_msgSend(captureDeviceClass, sel_registerName("devicesWithMediaType:"), mediaType);

        if (deviceArray == IntPtr.Zero) return result;

        var count = (int)objc_msgSend_ulong(deviceArray, sel_registerName("count"));

        for (int i = 0; i < count; i++)
        {
            var device = objc_msgSend(deviceArray, sel_registerName("objectAtIndex:"), (IntPtr)i);
            if (device == IntPtr.Zero) continue;

            var name = GetNSString(objc_msgSend(device, sel_registerName("localizedName")));
            if (name == null) continue;

            result.Add((name, i));
        }

        return result;
    }
}
