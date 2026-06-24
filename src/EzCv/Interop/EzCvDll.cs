using System.Runtime.InteropServices;

namespace EzCv.Interop;

/// <summary>
/// ezcv_native C API 绑定（OpenCV 5 capture 实际用到的子集）。全部 Cdecl。
/// DllImport 统一使用逻辑短名 "ezcv_native"，由 NativeLoader 在运行时按平台
/// 解析为实际文件名（ezcv_native.dll / libezcv_native.dylib / .so）。
/// </summary>
/// <remarks>
/// DllImport 属性参考 OpenCvSharp NativeMethods 模式：
///   - ExactSpelling = true：禁止 .NET 追加 A/W 后缀
///   - BestFitMapping = false, ThrowOnUnmappableChar = true：字符串参数防数据丢失
///   - [MarshalAs(UnmanagedType.LPUTF8Str)]：显式 UTF-8 编组（.NET 10 默认但显式更安全）
///   - CallingConvention.Cdecl：与 native extern "C" 一致
/// </remarks>
internal static partial class EzCvDll
{
    // 统一使用逻辑短名，由 NativeLoader 在运行时按平台解析为实际文件名。
    // Windows：resolver 在 runtimes/win-x64/native/ 中查找 ezcv_native.dll。
    // macOS/Linux：resolver 在 runtimes/<rid>/native/ 中查找 libezcv_native.{dylib,so}。
    private const string DllName = "ezcv_native";

    // 字符串 DllImport 通用属性（不含 CallingConvention / ExactSpelling，每个入口显式声明）。
    private const UnmanagedType StrMarshal = UnmanagedType.LPUTF8Str;

    // =========================================================================
    // Mat 生命周期
    // =========================================================================

    [DllImport(DllName, EntryPoint = "ezcv_mat_create", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr MatCreate();

    [DllImport(DllName, EntryPoint = "ezcv_mat_create_roi", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr MatCreateRoi(IntPtr src, int x, int y, int w, int h);

    [DllImport(DllName, EntryPoint = "ezcv_mat_create_sized", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr MatCreateSized(int rows, int cols, int type);

    [DllImport(DllName, EntryPoint = "ezcv_mat_clone", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr MatClone(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_release", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MatRelease(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_empty", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatEmpty(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_width", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatWidth(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_height", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatHeight(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_channels", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatChannels(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_type", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatType(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_step", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern long MatStep(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_dims", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatDims(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_size_dim", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MatSizeDim(IntPtr mat, int dim);

    [DllImport(DllName, EntryPoint = "ezcv_mat_data", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern byte* MatData(IntPtr mat);

    [DllImport(DllName, EntryPoint = "ezcv_mat_convert_to", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MatConvertTo(IntPtr src, IntPtr dst, int type);

    // =========================================================================
    // 图像处理
    // =========================================================================

    [DllImport(DllName, EntryPoint = "ezcv_cvt_color", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void CvtColor(IntPtr src, IntPtr dst, int code);

    [DllImport(DllName, EntryPoint = "ezcv_threshold", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern double Threshold(IntPtr src, IntPtr dst, double thresh, double maxval, int type);

    [DllImport(DllName, EntryPoint = "ezcv_sobel", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void Sobel(IntPtr src, IntPtr dst, int ddepth, int dx, int dy, int ksize);

    [DllImport(DllName, EntryPoint = "ezcv_add_weighted", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void AddWeighted(IntPtr src1, double alpha, IntPtr src2, double beta, double gamma, IntPtr dst);

    [DllImport(DllName, EntryPoint = "ezcv_gaussian_blur", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void GaussianBlur(IntPtr src, IntPtr dst, int kw, int kh, double sigma);

    [DllImport(DllName, EntryPoint = "ezcv_laplacian", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void Laplacian(IntPtr src, IntPtr dst, int ddepth, int ksize);

    [DllImport(DllName, EntryPoint = "ezcv_convert_scale_abs", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void ConvertScaleAbs(IntPtr src, IntPtr dst);

    [DllImport(DllName, EntryPoint = "ezcv_canny", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void Canny(IntPtr src, IntPtr dst, double t1, double t2);

    [DllImport(DllName, EntryPoint = "ezcv_resize", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void Resize(IntPtr src, IntPtr dst, int dw, int dh, double fx, double fy, int interpolation);

    [DllImport(DllName, EntryPoint = "ezcv_copy_make_border", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void CopyMakeBorder(IntPtr src, IntPtr dst, int top, int bottom, int left, int right, int borderType, double r, double g, double b);

    [DllImport(DllName, EntryPoint = "ezcv_split", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int Split(IntPtr src, IntPtr[] matArray);

    [DllImport(DllName, EntryPoint = "ezcv_merge", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void Merge(IntPtr[] matArray, int count, IntPtr dst);

    [DllImport(DllName, EntryPoint = "ezcv_match_template", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MatchTemplate(IntPtr image, IntPtr templ, IntPtr result, int method);

    [DllImport(DllName, EntryPoint = "ezcv_match_template_masked", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MatchTemplateMasked(IntPtr image, IntPtr templ, IntPtr result, int method, IntPtr mask);

    [DllImport(DllName, EntryPoint = "ezcv_min_max_loc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MinMaxLoc(IntPtr src, out double minVal, out double maxVal, out int minX, out int minY, out int maxX, out int maxY);

    [DllImport(DllName, EntryPoint = "ezcv_find_contours", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int FindContours(IntPtr image, int mode, int method);

    [DllImport(DllName, EntryPoint = "ezcv_contour_point_count", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int ContourPointCount(int index);

    [DllImport(DllName, EntryPoint = "ezcv_contour_points", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void ContourPoints(int index, int[] xArray, int[] yArray, int count);

    [DllImport(DllName, EntryPoint = "ezcv_clear_contours", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void ClearContours();

    [DllImport(DllName, EntryPoint = "ezcv_bounding_rect", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void BoundingRect(int contourIndex, out int x, out int y, out int w, out int h);

    // =========================================================================
    // 图像编解码
    // =========================================================================

    [DllImport(DllName, EntryPoint = "ezcv_imdecode_mem", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern IntPtr ImDecodeMem(byte* data, int length, int flags);

    [DllImport(DllName, EntryPoint = "ezcv_imencode_mem", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern IntPtr ImEncodeMem(byte* ext, IntPtr src, out int outLen);

    [DllImport(DllName, EntryPoint = "ezcv_free_buf", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FreeBuf(IntPtr buf);

    [DllImport(DllName, EntryPoint = "ezcv_imread", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr ImRead(
        [MarshalAs(StrMarshal)] string path, int flags);

    [DllImport(DllName, EntryPoint = "ezcv_imwrite", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int ImWrite(
        [MarshalAs(StrMarshal)] string path, IntPtr src);

    // =========================================================================
    // 绘图
    // =========================================================================

    [DllImport(DllName, EntryPoint = "ezcv_rectangle", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void Rectangle(IntPtr img, int x1, int y1, int x2, int y2, double r, double g, double b, int thickness);

    [DllImport(DllName, EntryPoint = "ezcv_put_text", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern void PutText(IntPtr img,
        [MarshalAs(StrMarshal)] string text, int x, int y, int fontFace, double fontScale,
        double r, double g, double b, int thickness);

    [DllImport(DllName, EntryPoint = "ezcv_get_text_size", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern void GetTextSize(
        [MarshalAs(StrMarshal)] string text, int fontFace, double fontScale, int thickness,
        out int outW, out int outH);

    // =========================================================================
    // 视频采集
    // =========================================================================

    [DllImport(DllName, EntryPoint = "ezcv_vc_create_default", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr VcCreateDefault();

    [DllImport(DllName, EntryPoint = "ezcv_vc_create_index", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr VcCreateIndex(int index, int api);

    [DllImport(DllName, EntryPoint = "ezcv_vc_open", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int VcOpen(IntPtr vc, int index, int api);

    [DllImport(DllName, EntryPoint = "ezcv_vc_is_opened", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int VcIsOpened(IntPtr vc);

    [DllImport(DllName, EntryPoint = "ezcv_vc_read", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int VcRead(IntPtr vc, IntPtr dst);

    [DllImport(DllName, EntryPoint = "ezcv_vc_set", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int VcSet(IntPtr vc, int propId, double value);

    [DllImport(DllName, EntryPoint = "ezcv_vc_get", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern double VcGet(IntPtr vc, int propId);

    [DllImport(DllName, EntryPoint = "ezcv_vc_release", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void VcRelease(IntPtr vc);

    // =========================================================================
    // DNN 深度神经网络 (dnn)
    // =========================================================================

    // --- DNN 模型加载 ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnReadNet(
        [MarshalAs(StrMarshal)] string model,
        [MarshalAs(StrMarshal)] string config,
        [MarshalAs(StrMarshal)] string framework, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_onnx", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnReadNetFromOnnx(
        [MarshalAs(StrMarshal)] string onnxFile, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_onnx_mem", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern IntPtr DnnReadNetFromOnnxMem(byte* data, int length, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_tensorflow", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnReadNetFromTensorflow(
        [MarshalAs(StrMarshal)] string model, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_tensorflow_with_config", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnReadNetFromTensorflowWithConfig(
        [MarshalAs(StrMarshal)] string model,
        [MarshalAs(StrMarshal)] string config, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_tensorflow_mem", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern IntPtr DnnReadNetFromTensorflowMem(byte* modelData, int modelLen, byte* configData, int configLen, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_tflite", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnReadNetFromTflite(
        [MarshalAs(StrMarshal)] string model, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_net_from_tflite_mem", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern IntPtr DnnReadNetFromTfliteMem(byte* data, int length, int engine);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_read_tensor_from_onnx", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnReadTensorFromOnnx(
        [MarshalAs(StrMarshal)] string path);

    // --- Net 生命周期 ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_release", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnNetRelease(IntPtr net);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_empty", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int DnnNetEmpty(IntPtr net);

    // --- Net 推理 ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_set_input", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern void DnnNetSetInput(IntPtr net, IntPtr blob,
        [MarshalAs(StrMarshal)] string name);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_set_inputs_names", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnNetSetInputsNames(IntPtr net, string[] names, int count);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_forward", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnNetForward(IntPtr net,
        [MarshalAs(StrMarshal)] string? outputName);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_forward_multi", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern void DnnNetForwardMulti(IntPtr net, IntPtr[] outputMats, int count,
        [MarshalAs(StrMarshal)] string? outputName);

    // --- Net 配置 ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_set_preferable_backend", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnNetSetPreferableBackend(IntPtr net, int backendId);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_set_preferable_target", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnNetSetPreferableTarget(IntPtr net, int targetId);

    // --- Net 信息查询 ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_get_layer_id", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int DnnNetGetLayerId(IntPtr net,
        [MarshalAs(StrMarshal)] string name);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_get_layer_names", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern int DnnNetGetLayerNames(IntPtr net, char*** names);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_get_unconnected_out_layers", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern int DnnNetGetUnconnectedOutLayers(IntPtr net, int** ids);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_get_unconnected_out_layers_names", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern int DnnNetGetUnconnectedOutLayersNames(IntPtr net, char*** names);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_get_perf_profile", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern long DnnNetGetPerfProfile(IntPtr net, double** timings, int* timingCount);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_net_dump", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static unsafe extern IntPtr DnnNetDump(IntPtr net);

    // --- DNN 工具函数 ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_blob_from_image", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr DnnBlobFromImage(IntPtr image, double scaleFactor, int w, int h, double r, double g, double b, int swapRb, int crop);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_nms_boxes", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int DnnNmsBoxes(int[] boxesXywh, float[] scores, int count, float scoreThreshold, float nmsThreshold, int[] indices, float eta = 1.0f, int topK = 0);

    // --- DetectionModel ---

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_new", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr DnnDetectionModelNew(
        [MarshalAs(StrMarshal)] string model,
        [MarshalAs(StrMarshal)] string config);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_from_net", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr DnnDetectionModelFromNet(IntPtr net);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_release", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelRelease(IntPtr dm);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_input_size", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetInputSize(IntPtr dm, int w, int h);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_input_mean", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetInputMean(IntPtr dm, double r, double g, double b);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_input_scale", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetInputScale(IntPtr dm, double scale);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_input_crop", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetInputCrop(IntPtr dm, int crop);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_input_swap_rb", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetInputSwapRb(IntPtr dm, int swapRb);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_preferable_backend", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetPreferableBackend(IntPtr dm, int backendId);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_set_preferable_target", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void DnnDetectionModelSetPreferableTarget(IntPtr dm, int targetId);

    [DllImport(DllName, EntryPoint = "ezcv_dnn_detection_model_detect", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int DnnDetectionModelDetect(IntPtr dm, IntPtr frame,
        int[] classIds, float[] confidences, int[] boxesXywh,
        int maxCount, float confThreshold, float nmsThreshold);

    // --- 通用释放 ---

    [DllImport(DllName, EntryPoint = "ezcv_free_string_array", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void FreeStringArray(IntPtr arr, int count);

    // =========================================================================
    // 异常查询 (errno 风格)
    // =========================================================================
    // 返回值指向 native thread_local 缓冲，StringMarshalling.Utf8 会拷贝成托管 string。
    // 无错时返回空串。详见 EzCvError.ThrowIfAny()。

    [DllImport(DllName, EntryPoint = "ezcv_last_error", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr LastError();

    [DllImport(DllName, EntryPoint = "ezcv_clear_error", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void ClearError();
}