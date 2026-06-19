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

#ifdef __cplusplus
}
#endif

#endif // EZCV_NATIVE_H