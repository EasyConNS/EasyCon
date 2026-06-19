using System.Runtime.InteropServices;

namespace EzCv.Interop;

/// <summary>
/// ezcv_native C API 绑定（OpenCV 5 capture 实际用到的子集）。全部 Cdecl。
/// 逻辑库名 <see cref="NativeLoader.EzCvLib"/> 由 resolver 重写为平台真实库文件名。
/// 使用 [LibraryImport] 源生成器（编译期 marshalling，AOT 友好）。
/// </summary>
internal static partial class EzCvDll
{
    // =========================================================================
    // Mat 生命周期
    // =========================================================================

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_create", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr MatCreate();

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_create_roi", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr MatCreateRoi(IntPtr src, int x, int y, int w, int h);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_create_sized", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr MatCreateSized(int rows, int cols, int type);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_clone", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr MatClone(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_release", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MatRelease(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_empty", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatEmpty(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_width", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatWidth(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_height", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatHeight(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_channels", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatChannels(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_type", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatType(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_step", StringMarshalling = StringMarshalling.Utf8)]
    public static partial long MatStep(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_data", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial byte* MatData(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_convert_to", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MatConvertTo(IntPtr src, IntPtr dst, int type);

    // =========================================================================
    // 图像处理
    // =========================================================================

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_cvt_color", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void CvtColor(IntPtr src, IntPtr dst, int code);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_threshold", StringMarshalling = StringMarshalling.Utf8)]
    public static partial double Threshold(IntPtr src, IntPtr dst, double thresh, double maxval, int type);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_sobel", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Sobel(IntPtr src, IntPtr dst, int ddepth, int dx, int dy, int ksize);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_add_weighted", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void AddWeighted(IntPtr src1, double alpha, IntPtr src2, double beta, double gamma, IntPtr dst);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_gaussian_blur", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void GaussianBlur(IntPtr src, IntPtr dst, int kw, int kh, double sigma);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_laplacian", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Laplacian(IntPtr src, IntPtr dst, int ddepth, int ksize);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_convert_scale_abs", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ConvertScaleAbs(IntPtr src, IntPtr dst);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_canny", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Canny(IntPtr src, IntPtr dst, double t1, double t2);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_resize", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Resize(IntPtr src, IntPtr dst, int dw, int dh, double fx, double fy, int interpolation);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_copy_make_border", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void CopyMakeBorder(IntPtr src, IntPtr dst, int top, int bottom, int left, int right, int borderType, double r, double g, double b);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_split", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Split(IntPtr src, IntPtr[] matArray);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_merge", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Merge(IntPtr[] matArray, int count, IntPtr dst);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_match_template", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MatchTemplate(IntPtr image, IntPtr templ, IntPtr result, int method);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_match_template_masked", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MatchTemplateMasked(IntPtr image, IntPtr templ, IntPtr result, int method, IntPtr mask);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_min_max_loc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void MinMaxLoc(IntPtr src, out double minVal, out double maxVal, out int minX, out int minY, out int maxX, out int maxY);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_find_contours", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FindContours(IntPtr image, int mode, int method);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_contour_point_count", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ContourPointCount(int index);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_contour_points", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ContourPoints(int index, int[] xArray, int[] yArray, int count);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_clear_contours", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ClearContours();

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_bounding_rect", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void BoundingRect(int contourIndex, out int x, out int y, out int w, out int h);

    // =========================================================================
    // 图像编解码
    // =========================================================================

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_imdecode_mem", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr ImDecodeMem(byte* data, int length, int flags);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_imencode_mem", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr ImEncodeMem(byte* ext, IntPtr src, out int outLen);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_free_buf", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void FreeBuf(IntPtr buf);

    // =========================================================================
    // 视频采集
    // =========================================================================

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_create_default", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr VcCreateDefault();

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_create_index", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr VcCreateIndex(int index, int api);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VcOpen(IntPtr vc, int index, int api);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_is_opened", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VcIsOpened(IntPtr vc);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_read", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VcRead(IntPtr vc, IntPtr dst);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_set", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VcSet(IntPtr vc, int propId, double value);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_get", StringMarshalling = StringMarshalling.Utf8)]
    public static partial double VcGet(IntPtr vc, int propId);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_vc_release", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void VcRelease(IntPtr vc);
}