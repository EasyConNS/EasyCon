// ezcv_native.h — C 胶水层，导出 OpenCV 5 C++ API 的 C 兼容接口
// 所有函数以 ezcv_ 前缀命名，使用 extern "C" 导出
// Mat/VideoCapture 等 C++ 对象通过不透明句柄 (void*) 传递
//
// 异常穿越 ABI：cv::Exception / std::bad_alloc 等不能穿 C ABI（UB）。
// 所有可能抛异常的函数在内部 try/catch 吞掉，把 e.what() 写入 thread_local
// 缓冲；调用方在每次 P/Invoke 后用 ezcv_last_error() 检查（errno 风格）。
// 成功时 ezcv_last_error() 返回空串。
//
// 导出符号：由 EZCV_API 宏统一处理（Windows __declspec(dllexport)，
// GCC/Clang visibility），无需 .def 文件或构建期从 .obj 扫符号。

#ifndef EZCV_NATIVE_H
#define EZCV_NATIVE_H

#include <stdint.h>

// 跨平台导出符号
#if defined(_WIN32) || defined(__CYGWIN__)
  #define EZCV_API __declspec(dllexport)
#else
  #define EZCV_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// =========================================================================
// 异常查询 (errno 风格)
// =========================================================================

// 读取当前线程的最近一次错误信息。无错时返回空串 ("")。
// 返回的指针指向 thread_local 缓冲，仅当前线程读取有效，下次 native 调用可能被覆盖。
EZCV_API const char* ezcv_last_error(void);

// 清空当前线程的错误缓冲。成功调用后通常无需显式调用。
EZCV_API void ezcv_clear_error(void);

// =========================================================================
// Mat 生命周期
// =========================================================================

// 创建空 Mat
EZCV_API void* ezcv_mat_create(void);

// 从已有 Mat 创建 ROI 视图 (shallow copy, 共享数据)
EZCV_API void* ezcv_mat_create_roi(void* src, int x, int y, int w, int h);

// 创建指定大小和类型的 Mat
EZCV_API void* ezcv_mat_create_sized(int rows, int cols, int type);

// 深拷贝
EZCV_API void* ezcv_mat_clone(void* mat);

// 释放 Mat
EZCV_API void ezcv_mat_release(void* mat);

// 判空
EZCV_API int ezcv_mat_empty(void* mat);

// 属性访问
EZCV_API int ezcv_mat_width(void* mat);
EZCV_API int ezcv_mat_height(void* mat);
EZCV_API int ezcv_mat_channels(void* mat);
EZCV_API int ezcv_mat_type(void* mat);
EZCV_API int64_t ezcv_mat_step(void* mat);
// 多维维度访问
EZCV_API int ezcv_mat_dims(void* mat);
EZCV_API int ezcv_mat_size_dim(void* mat, int dim);

// 获取原始数据指针 (不可在托管端释放)
EZCV_API unsigned char* ezcv_mat_data(void* mat);

// 类型转换 (in-place 或创建新 Mat)
EZCV_API void ezcv_mat_convert_to(void* src, void* dst, int type);

// =========================================================================
// 图像处理 (imgproc + core)
// =========================================================================

// 颜色空间转换
EZCV_API void ezcv_cvt_color(void* src, void* dst, int code);

// 二值化
EZCV_API double ezcv_threshold(void* src, void* dst, double thresh, double maxval, int type);

// Sobel 边缘检测
EZCV_API void ezcv_sobel(void* src, void* dst, int ddepth, int dx, int dy, int ksize);

// 加权加法
EZCV_API void ezcv_add_weighted(void* src1, double alpha, void* src2, double beta, double gamma, void* dst);

// 高斯模糊
EZCV_API void ezcv_gaussian_blur(void* src, void* dst, int kw, int kh, double sigma);

// Laplacian 边缘检测
EZCV_API void ezcv_laplacian(void* src, void* dst, int ddepth, int ksize);

// 绝对值缩放转换
EZCV_API void ezcv_convert_scale_abs(void* src, void* dst);

// Canny 边缘检测
EZCV_API void ezcv_canny(void* src, void* dst, double t1, double t2);

// 缩放
EZCV_API void ezcv_resize(void* src, void* dst, int dw, int dh, double fx, double fy, int interpolation);

// 边界填充
EZCV_API void ezcv_copy_make_border(void* src, void* dst, int top, int bottom, int left, int right, int border_type, double r, double g, double b);

// 通道分离 — 返回通道数，mat_array 是预分配的大小为3的 void* 数组
EZCV_API int ezcv_split(void* src, void** mat_array);

// 通道合并
EZCV_API void ezcv_merge(void** mat_array, int count, void* dst);

// 模板匹配 (无 mask)
EZCV_API void ezcv_match_template(void* image, void* templ, void* result, int method);

// 模板匹配 (带 mask)
EZCV_API void ezcv_match_template_masked(void* image, void* templ, void* result, int method, void* mask);

// 查找最小最大值位置
EZCV_API void ezcv_min_max_loc(void* src, double* min_val, double* max_val, int* min_x, int* min_y, int* max_x, int* max_y);

// 查找轮廓 — 返回轮廓数量
EZCV_API int ezcv_find_contours(void* image, int mode, int method);

// 获取第 index 个轮廓的点数
EZCV_API int ezcv_contour_point_count(int index);

// 获取第 index 个轮廓的点坐标 (写入 x_array, y_array，调用者预分配)
EZCV_API void ezcv_contour_points(int index, int* x_array, int* y_array, int count);

// 清除缓存的轮廓数据
EZCV_API void ezcv_clear_contours(void);

// 获取轮廓的外接矩形
EZCV_API void ezcv_bounding_rect(int contour_index, int* x, int* y, int* w, int* h);

// =========================================================================
// 图像编解码 (imgcodecs)
// =========================================================================

// 从内存字节数组解码图像
EZCV_API void* ezcv_imdecode_mem(const unsigned char* data, int length, int flags);

// 将 Mat 编码为图像字节数组（PNG/JPEG 等），返回 malloc 分配的 buffer
// 调用者需通过 ezcv_free_buf 释放；失败返回 NULL，out_len 为 0
EZCV_API unsigned char* ezcv_imencode_mem(const char* ext, void* src, int* out_len);
EZCV_API void ezcv_free_buf(void* buf);

// =========================================================================
// 视频采集 (videoio)
// =========================================================================

// 创建 VideoCapture 实例
EZCV_API void* ezcv_vc_create_default(void);
EZCV_API void* ezcv_vc_create_index(int index, int api);

// 打开设备
EZCV_API int ezcv_vc_open(void* vc, int index, int api);

// 是否已打开
EZCV_API int ezcv_vc_is_opened(void* vc);

// 读取一帧 (写入 dst Mat，调用者负责创建/释放)
EZCV_API int ezcv_vc_read(void* vc, void* dst);

// 设置属性
EZCV_API int ezcv_vc_set(void* vc, int prop_id, double value);

// 获取属性
EZCV_API double ezcv_vc_get(void* vc, int prop_id);

// 释放 VideoCapture
EZCV_API void ezcv_vc_release(void* vc);

// =========================================================================
// DNN 深度神经网络 (dnn)
// =========================================================================

// --- 模型加载 ---

// readNet: 自动检测格式加载网络
// engine: 1=CLASSIC, 2=NEW, 3=AUTO, 4=ORT (OpenCV 5 EngineType)
EZCV_API void* ezcv_dnn_read_net(const char* model, const char* config, const char* framework, int engine);

// readNetFromONNX: 从 ONNX 文件加载
EZCV_API void* ezcv_dnn_read_net_from_onnx(const char* onnx_file, int engine);

// readNetFromONNX: 从内存字节加载
EZCV_API void* ezcv_dnn_read_net_from_onnx_mem(const unsigned char* data, int length, int engine);

// readNetFromTensorflow: 从 TensorFlow .pb 文件加载
EZCV_API void* ezcv_dnn_read_net_from_tensorflow(const char* model, int engine);

// readNetFromTensorflow: 从 .pb 文件和 .pbtxt 配置文件加载
EZCV_API void* ezcv_dnn_read_net_from_tensorflow_with_config(const char* model, const char* config, int engine);

// readNetFromTensorflow: 从内存字节加载
EZCV_API void* ezcv_dnn_read_net_from_tensorflow_mem(const unsigned char* model_data, int model_len,
                                                      const unsigned char* config_data, int config_len,
                                                      int engine);

// readNetFromTFLite: 从 TFLite .tflite 文件加载
EZCV_API void* ezcv_dnn_read_net_from_tflite(const char* model, int engine);

// readNetFromTFLite: 从内存字节加载
EZCV_API void* ezcv_dnn_read_net_from_tflite_mem(const unsigned char* data, int length, int engine);

// readTensorFromONNX: 从 ONNX .pb 文件读取张量
EZCV_API void* ezcv_dnn_read_tensor_from_onnx(const char* path);

// --- Net 生命周期 ---

// 释放 Net
EZCV_API void ezcv_dnn_net_release(void* net);

// 判空
EZCV_API int ezcv_dnn_net_empty(void* net);

// --- Net 推理 ---

// 设置输入 blob
EZCV_API void ezcv_dnn_net_set_input(void* net, void* blob, const char* name);

// 设置输入名称
EZCV_API void ezcv_dnn_net_set_inputs_names(void* net, const char** names, int count);

// 前向传播 (单输出) — 返回新 Mat
EZCV_API void* ezcv_dnn_net_forward(void* net, const char* output_name);

// 前向传播 (多输出) — 写入预分配的 Mat 数组
EZCV_API void ezcv_dnn_net_forward_multi(void* net, void** output_mats, int count, const char* output_name);

// --- Net 配置 ---

// 设置后端 (backend_id: DNN_BACKEND_*)
EZCV_API void ezcv_dnn_net_set_preferable_backend(void* net, int backend_id);

// 设置目标设备 (target_id: DNN_TARGET_*)
EZCV_API void ezcv_dnn_net_set_preferable_target(void* net, int target_id);

// --- Net 信息查询 ---

// 获取层 ID，未找到返回 -1
EZCV_API int ezcv_dnn_net_get_layer_id(void* net, const char* name);

// 获取所有层名称 — 返回数量，names 写入 malloc 的 char* 数组
// 调用者需通过 ezcv_free_string_array 释放
EZCV_API int ezcv_dnn_net_get_layer_names(void* net, char*** names);

// 获取未连接输出层 ID — 返回数量，ids 写入 malloc 的 int 数组
// 调用者需通过 ezcv_free_buf 释放
EZCV_API int ezcv_dnn_net_get_unconnected_out_layers(void* net, int** ids);

// 获取未连接输出层名称 — 返回数量，names 写入 malloc 的 char* 数组
// 调用者需通过 ezcv_free_string_array 释放
EZCV_API int ezcv_dnn_net_get_unconnected_out_layers_names(void* net, char*** names);

// 性能分析 — 返回总 ticks，timings 写入 malloc 的 double 数组
// 调用者需通过 ezcv_free_buf 释放 timings
EZCV_API int64_t ezcv_dnn_net_get_perf_profile(void* net, double** timings, int* timing_count);

// 网络 dump — 返回 malloc 的字符串
// 调用者需通过 ezcv_free_buf 释放
EZCV_API char* ezcv_dnn_net_dump(void* net);

// --- DNN 工具函数 ---

// blobFromImage: 从图像创建 4D blob
// mean: 以 r,g,b 顺序传入（内部转换为 Scalar(b,g,r)）
EZCV_API void* ezcv_dnn_blob_from_image(void* image, double scale_factor,
    int w, int h, double r, double g, double b, int swap_rb, int crop);

// NMSBoxes: 非极大值抑制
// boxes_xywh: 连续存储的 [x,y,w,h] 数组，长度 count*4
// scores: 长度为 count 的分数数组
// indices: 调用者预分配的 int 数组 (长度 >= count)
// eta: 自适应阈值系数 (默认 1.0)
// top_k: 如果 >0，限制输出框数量 (默认 0 表示不限制)
// 返回选中的索引数量
EZCV_API int ezcv_dnn_nms_boxes(const int* boxes_xywh, const float* scores,
    int count, float score_threshold, float nms_threshold, int* indices,
    float eta, int top_k);

// --- DetectionModel 高层 API ---

// 从文件创建 DetectionModel
EZCV_API void* ezcv_dnn_detection_model_new(const char* model, const char* config);

// 从已有 Net 创建 DetectionModel
EZCV_API void* ezcv_dnn_detection_model_from_net(void* net);

// 释放 DetectionModel
EZCV_API void ezcv_dnn_detection_model_release(void* dm);

// 配置输入参数
EZCV_API void ezcv_dnn_detection_model_set_input_size(void* dm, int w, int h);
EZCV_API void ezcv_dnn_detection_model_set_input_mean(void* dm, double r, double g, double b);
EZCV_API void ezcv_dnn_detection_model_set_input_scale(void* dm, double scale);
EZCV_API void ezcv_dnn_detection_model_set_input_crop(void* dm, int crop);
EZCV_API void ezcv_dnn_detection_model_set_input_swap_rb(void* dm, int swap_rb);

// 配置后端/目标
EZCV_API void ezcv_dnn_detection_model_set_preferable_backend(void* dm, int backend_id);
EZCV_API void ezcv_dnn_detection_model_set_preferable_target(void* dm, int target_id);

// 检测 — 写入预分配的数组 (class_ids, confidences, boxes_xywh 各 max_count 容量)
// 返回实际检测数量
EZCV_API int ezcv_dnn_detection_model_detect(void* dm, void* frame,
    int* class_ids, float* confidences, int* boxes_xywh,
    int max_count, float conf_threshold, float nms_threshold);

// --- 通用释放 ---

// 释放字符串数组 (由 get_layer_names 等函数返回)
EZCV_API void ezcv_free_string_array(char** arr, int count);

#ifdef __cplusplus
}
#endif

#endif // EZCV_NATIVE_H