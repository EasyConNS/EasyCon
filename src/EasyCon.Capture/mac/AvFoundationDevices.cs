using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EasyCon.Capture.mac;

/// <summary>使用与锁定的 OpenCV AVFoundation 后端一致的设备集合及排序，不打开摄像头。</summary>
[SupportedOSPlatform("macos")]
internal static class AvFoundationDevices
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly nint VideoMediaType;
    private static readonly nint MuxedMediaType;

    static AvFoundationDevices()
    {
        // 保持框架加载到进程退出；枚举可先于 OpenCV 的首次使用发生。
        nint framework = NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
        VideoMediaType = Marshal.ReadIntPtr(NativeLibrary.GetExport(framework, "AVMediaTypeVideo"));
        MuxedMediaType = Marshal.ReadIntPtr(NativeLibrary.GetExport(framework, "AVMediaTypeMuxed"));
    }

    public static IReadOnlyList<(string Name, string? StableId, int Index)> Enumerate()
    {
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        try
        {
            nint deviceClass = GetClass("AVCaptureDevice");
            nint video = SendPointer(deviceClass, Selector("devicesWithMediaType:"), VideoMediaType);
            nint muxed = SendPointer(deviceClass, Selector("devicesWithMediaType:"), MuxedMediaType);
            nint devices = SendPointer(video, Selector("arrayByAddingObjectsFromArray:"), muxed);

            // OpenCvSharp5 5.0.0.20261011 的 OpenCV 提交 40738fb：video + muxed，
            // 随后使用 NSString compare: 按 uniqueID 升序排列。不能按名称或系统返回顺序分配索引。
            nint key = SendString(GetClass("NSString"), Selector("stringWithUTF8String:"), "uniqueID");
            nint sort = SendSort(GetClass("NSSortDescriptor"), Selector("sortDescriptorWithKey:ascending:"), key, true);
            nint descriptors = SendPointer(GetClass("NSArray"), Selector("arrayWithObject:"), sort);
            nint ordered = SendPointer(devices, Selector("sortedArrayUsingDescriptors:"), descriptors);
            int count = checked((int)SendCount(ordered, Selector("count")));
            List<(string, string?, int)> result = new(count);
            for (int index = 0; index < count; index++)
            {
                nint device = SendIndex(ordered, Selector("objectAtIndex:"), (nuint)index);
                string name = ReadString(Send(device, Selector("localizedName"))) ?? $"摄像头 {index}";
                string? id = ReadString(Send(device, Selector("uniqueID")));
                result.Add((name, id, index));
            }
            return result;
        }
        finally
        {
            SendVoid(pool, Selector("drain"));
        }
    }

    public static void ThrowIfAccessUnavailable()
    {
        nint status = SendPointer(GetClass("AVCaptureDevice"), Selector("authorizationStatusForMediaType:"), VideoMediaType);
        if (status == 0)
            throw new UnauthorizedAccessException("尚未获得摄像头权限，请在 macOS 授权弹窗中允许 EasyCon 访问摄像头后重新连接");
        if (status is 1 or 2)
            throw new UnauthorizedAccessException("摄像头访问被 macOS 拒绝或限制，请在系统设置 → 隐私与安全性 → 摄像头中允许 EasyCon（CLI 请允许对应终端）");
    }

    private static string? ReadString(nint value)
        => value == 0 ? null : Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String")));

    private static nint GetClass(string name)
    {
        nint value = GetNativeClass(name);
        return value != 0 ? value : throw new InvalidOperationException($"macOS 系统框架未提供 {name}，无法枚举视频来源");
    }

    // objc_msgSend 必须使用每个方法的确切签名，尤其不能把 arm64 的固定参数当作可变参数传递。
    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern nint GetNativeClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendPointer(nint receiver, nint selector, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendIndex(nint receiver, nint selector, nuint index);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendString(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendSort(nint receiver, nint selector, nint key, [MarshalAs(UnmanagedType.I1)] bool ascending);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendCount(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);
}