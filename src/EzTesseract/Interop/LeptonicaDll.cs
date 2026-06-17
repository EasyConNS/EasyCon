using System.Runtime.InteropServices;

namespace EzTesseract.Interop;

/// <summary>
/// leptonica C API 绑定（capture 实际用到的子集）。全部 Cdecl。
/// 逻辑库名 <see cref="NativeLoader.LeptonicaLib"/> 由 resolver 重写为平台真实库文件。
/// 使用 [LibraryImport] 源生成器（编译期 marshalling，AOT 友好）。
/// 签名依据：tools/TesseractOCR/TesseractOCR/Interop/LeptonicaApi.cs。
/// </summary>
internal static partial class LeptonicaDll
{
    /// <summary>从内存字节数组解码图像（支持 PNG/JPEG/TIFF 等）。返回 NULL 表示失败。</summary>
    [LibraryImport(NativeLoader.LeptonicaLib, EntryPoint = "pixReadMem", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr PixReadMem(byte* data, int length);

    /// <summary>释放 Pix，将句柄置零。</summary>
    [LibraryImport(NativeLoader.LeptonicaLib, EntryPoint = "pixDestroy", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void PixDestroy(ref IntPtr pix);
}
