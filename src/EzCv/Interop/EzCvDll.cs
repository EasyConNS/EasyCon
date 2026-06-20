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

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_dims", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatDims(IntPtr mat);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_mat_size_dim", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MatSizeDim(IntPtr mat, int dim);

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

    // =========================================================================
    // DNN 深度神经网络 (dnn)
    // =========================================================================

    // --- DNN 模型加载 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnReadNet(string model, string config, string framework, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_onnx", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnReadNetFromOnnx(string onnxFile, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_onnx_mem", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr DnnReadNetFromOnnxMem(byte* data, int length, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_tensorflow", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnReadNetFromTensorflow(string model, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_tensorflow_with_config", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnReadNetFromTensorflowWithConfig(string model, string config, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_tensorflow_mem", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr DnnReadNetFromTensorflowMem(byte* modelData, int modelLen, byte* configData, int configLen, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_tflite", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnReadNetFromTflite(string model, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_net_from_tflite_mem", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr DnnReadNetFromTfliteMem(byte* data, int length, int engine);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_read_tensor_from_onnx", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnReadTensorFromOnnx(string path);

    // --- Net 生命周期 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_release", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnNetRelease(IntPtr net);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_empty", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DnnNetEmpty(IntPtr net);

    // --- Net 推理 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_set_input", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnNetSetInput(IntPtr net, IntPtr blob, string name);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_set_inputs_names", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnNetSetInputsNames(IntPtr net, string[] names, int count);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_forward", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnNetForward(IntPtr net, string? outputName);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_forward_multi", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnNetForwardMulti(IntPtr net, IntPtr[] outputMats, int count, string? outputName);

    // --- Net 配置 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_set_preferable_backend", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnNetSetPreferableBackend(IntPtr net, int backendId);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_set_preferable_target", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnNetSetPreferableTarget(IntPtr net, int targetId);

    // --- Net 信息查询 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_get_layer_id", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DnnNetGetLayerId(IntPtr net, string name);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_get_layer_names", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int DnnNetGetLayerNames(IntPtr net, char*** names);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_get_unconnected_out_layers", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int DnnNetGetUnconnectedOutLayers(IntPtr net, int** ids);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_get_unconnected_out_layers_names", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int DnnNetGetUnconnectedOutLayersNames(IntPtr net, char*** names);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_get_perf_profile", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial long DnnNetGetPerfProfile(IntPtr net, double** timings, int* timingCount);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_net_dump", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial IntPtr DnnNetDump(IntPtr net);

    // --- DNN 工具函数 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_blob_from_image", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnBlobFromImage(IntPtr image, double scaleFactor, int w, int h, double r, double g, double b, int swapRb, int crop);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_nms_boxes", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DnnNmsBoxes(int[] boxesXywh, float[] scores, int count, float scoreThreshold, float nmsThreshold, int[] indices, float eta = 1.0f, int topK = 0);

    // --- DetectionModel ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnDetectionModelNew(string model, string config);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_from_net", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr DnnDetectionModelFromNet(IntPtr net);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_release", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelRelease(IntPtr dm);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_input_size", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetInputSize(IntPtr dm, int w, int h);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_input_mean", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetInputMean(IntPtr dm, double r, double g, double b);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_input_scale", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetInputScale(IntPtr dm, double scale);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_input_crop", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetInputCrop(IntPtr dm, int crop);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_input_swap_rb", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetInputSwapRb(IntPtr dm, int swapRb);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_preferable_backend", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetPreferableBackend(IntPtr dm, int backendId);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_set_preferable_target", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void DnnDetectionModelSetPreferableTarget(IntPtr dm, int targetId);

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_dnn_detection_model_detect", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DnnDetectionModelDetect(IntPtr dm, IntPtr frame,
        int[] classIds, float[] confidences, int[] boxesXywh,
        int maxCount, float confThreshold, float nmsThreshold);

    // --- 通用释放 ---

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_free_string_array", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void FreeStringArray(IntPtr arr, int count);

    // =========================================================================
    // 异常查询 (errno 风格)
    // =========================================================================
    // 返回值指向 native thread_local 缓冲，StringMarshalling.Utf8 会拷贝成托管 string。
    // 无错时返回空串。详见 EzCvError.ThrowIfAny()。

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_last_error", StringMarshalling = StringMarshalling.Utf8)]
    public static partial string LastError();

    [LibraryImport(NativeLoader.EzCvLib, EntryPoint = "ezcv_clear_error", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ClearError();
}