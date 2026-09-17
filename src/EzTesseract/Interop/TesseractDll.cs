using System.Runtime.InteropServices;

namespace EzTesseract.Interop;

/// <summary>
/// libtesseract C API 绑定（capture 实际用到的子集）。全部 Cdecl。
/// 逻辑库名 <see cref="NativeLoader.TesseractLib"/> 由 resolver 重写为平台真实库文件。
/// 使用 [LibraryImport] 源生成器（编译期 marshalling，AOT 友好）。
/// 签名依据：tools/TesseractOCR/TesseractOCR/Interop/TessApi.cs（Sicos1977/TesseractOCR 5.5.2）。
/// </summary>
internal static partial class TesseractDll
{
    /// <summary>创建 TessBaseAPI 实例。返回 NULL 表示失败。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPICreate", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr BaseApiCreate();

    /// <summary>释放 TessBaseAPI 实例（等价于析构重建）。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPIDelete", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void BaseApiDelete(IntPtr handle);

    /// <summary>
    /// 初始化引擎（OEM 生效）。返回 0 成功，-1 失败。
    /// </summary>
    /// <param name="datapath">tessdata 的父目录；为空时用 TESSDATA_PREFIX。</param>
    /// <param name="language">语言，如 "eng" 或 "chi_sim+eng"。</param>
    /// <param name="mode"><see cref="EzTesseract.Enums.EngineMode"/>（OEM）。</param>
    /// <remarks>
    /// 原生签名：int TessBaseAPIInit4(handle, const char* datapath, const char* language, int mode,
    ///   const char** configs, int configs_size, const char** vars_vec, const char** vars_values,
    ///   size_t vars_vec_size, bool set_only_non_debug_params)
    /// capture 不使用 configs/vars，故 char** 参数以 IntPtr 表达并传 IntPtr.Zero。
    /// </remarks>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPIInit4", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Init4(
        IntPtr handle,
        string datapath,
        string language,
        int mode,
        IntPtr configs,
        int configs_size,
        IntPtr vars_vec,
        IntPtr vars_values,
        UIntPtr vars_vec_size,
        [MarshalAs(UnmanagedType.U1)] bool set_only_non_debug_params);

    /// <summary>设置页面分割模式。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPISetPageSegMode", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetPageSegMode(IntPtr handle, int mode);

    /// <summary>设置输入图像（Pix 句柄，不拷贝、不获取所有权）。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPISetImage2", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetImage2(IntPtr handle, IntPtr pix);

    /// <summary>执行识别。monitor 传 NULL。返回 0 成功。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPIRecognize", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Recognize(IntPtr handle, IntPtr monitor);

    /// <summary>获取 UTF-8 文本，须用 <see cref="DeleteText"/> 释放。返回 NULL 表示失败。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPIGetUTF8Text", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetUTF8Text(IntPtr handle);

    /// <summary>平均置信度（0~100）。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPIMeanTextConf", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MeanTextConf(IntPtr handle);

    /// <summary>清除识别结果与图像数据，保留可复用的识别数据。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessBaseAPIClear", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Clear(IntPtr handle);

    /// <summary>释放 <see cref="GetUTF8Text"/> 返回的字符串。</summary>
    [LibraryImport(NativeLoader.TesseractLib, EntryPoint = "TessDeleteText", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DeleteText(IntPtr textPtr);
}