using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace EzCv.Interop;

/// <summary>
/// errno 风格错误门：native 侧 <c>thread_local g_last_error</c> 在 <c>cv::Exception</c>
/// 或 <c>std::bad_alloc</c> 被吞掉时写入。每次可能抛异常的 P/Invoke 后调
/// <see cref="ThrowIfAny"/>，有错误就抛 <see cref="OpenCVException"/> 并清空缓冲。
/// </summary>
internal static class EzCvError
{
    /// <summary>
    /// 检查 native last_error；非空则清空并抛 <see cref="OpenCVException"/>。
    /// </summary>
    [DoesNotReturn]
    private static void Throw(string message) => throw new OpenCVException(message);

    public static void ThrowIfAny()
    {
        var ptr = EzCvDll.LastError();
        var msg = Marshal.PtrToStringUTF8(ptr);
        if (!string.IsNullOrEmpty(msg))
        {
            EzCvDll.ClearError();
            Throw(msg);
        }
    }
}
