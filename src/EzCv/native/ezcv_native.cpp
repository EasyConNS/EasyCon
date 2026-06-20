// ezcv_native.cpp — C 胶水层实现，将 OpenCV 5 C++ API 包装为 c 兼容接口
//
// 异常穿越：cv::Exception / std::bad_alloc 不能穿 C ABI（UB）。所有可能抛异常的
// 函数体用 EZCV_TRY/EZCV_CATCH 宏包裹，catch 后把 e.what() 写入 thread_local
// g_last_error，并返回失败 sentinel（nullptr / 0 / false / 0.0）。调用方
// （C# 端）在每次 P/Invoke 后调 ezcv_last_error() 检查并抛 OpenCVException。

#include "ezcv_native.h"
#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/videoio.hpp>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/dnn.hpp>
#include <opencv2/geometry/2d.hpp>  // boundingRect in OpenCV 5
#include <vector>
#include <cstring>
#include <string>

// =========================================================================
// 内部辅助
// =========================================================================

// 线程局部存储：findContours 的结果缓存
static thread_local std::vector<std::vector<cv::Point>> g_contours;
static thread_local std::vector<cv::Vec4i> g_hierarchy;

// thread_local 错误缓冲（errno 风格）。每个 native 线程独立。
static thread_local std::string g_last_error;

// Scalar 辅助
static inline cv::Scalar make_scalar(double r, double g, double b) {
    return cv::Scalar(b, g, r); // OpenCV uses BGR order
}

// --------------------------------------------------------------------------
// 异常穿越 ABI 的宏。用法：
//   EZCV_API void f(void* a) {
//       EZCV_TRY
//       cv::something(*static_cast<cv::Mat*>(a));
//       EZCV_CATCH(return)              // void: 仅吞掉
//   }
//   EZCV_API void* g(void) {
//       EZCV_TRY
//       return new cv::Mat();
//       EZCV_CATCH(return nullptr)      // 有返回值: 返回失败 sentinel
//   }
// 每次 EZCV_TRY 进入时清空 g_last_error，避免读到上次调用的残留。
// --------------------------------------------------------------------------
#define EZCV_TRY \
    g_last_error.clear(); \
    try {

#define EZCV_CATCH(fail_stmt) \
    } \
    catch (const cv::Exception& e) { g_last_error = e.what(); fail_stmt; } \
    catch (const std::exception& e) { g_last_error = e.what(); fail_stmt; }

// =========================================================================
// 异常查询 (errno 风格)
// =========================================================================

EZCV_API const char* ezcv_last_error(void) {
    return g_last_error.c_str();
}

EZCV_API void ezcv_clear_error(void) {
    g_last_error.clear();
}

// =========================================================================
// Mat 生命周期
// =========================================================================

EZCV_API void* ezcv_mat_create(void) {
    EZCV_TRY
    return new cv::Mat();
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_mat_create_roi(void* src, int x, int y, int w, int h) {
    EZCV_TRY
    auto* mat = static_cast<cv::Mat*>(src);
    return new cv::Mat(*mat, cv::Rect(x, y, w, h));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_mat_create_sized(int rows, int cols, int type) {
    EZCV_TRY
    return new cv::Mat(rows, cols, type);
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_mat_clone(void* mat) {
    EZCV_TRY
    auto* m = static_cast<cv::Mat*>(mat);
    return new cv::Mat(m->clone());
    EZCV_CATCH(return nullptr)
}

EZCV_API void ezcv_mat_release(void* mat) {
    // delete 不抛 cv::Exception；但若传入野指针会 SEH，不包 try。
    delete static_cast<cv::Mat*>(mat);
}

EZCV_API int ezcv_mat_empty(void* mat) {
    return static_cast<cv::Mat*>(mat)->empty() ? 1 : 0;
}

EZCV_API int ezcv_mat_width(void* mat) {
    return static_cast<cv::Mat*>(mat)->cols;
}

EZCV_API int ezcv_mat_height(void* mat) {
    return static_cast<cv::Mat*>(mat)->rows;
}

EZCV_API int ezcv_mat_channels(void* mat) {
    return static_cast<cv::Mat*>(mat)->channels();
}

EZCV_API int ezcv_mat_type(void* mat) {
    return static_cast<cv::Mat*>(mat)->type();
}

EZCV_API int64_t ezcv_mat_step(void* mat) {
    return static_cast<int64_t>(static_cast<cv::Mat*>(mat)->step);
}

EZCV_API int ezcv_mat_dims(void* mat) {
    return static_cast<cv::Mat*>(mat)->dims;
}

EZCV_API int ezcv_mat_size_dim(void* mat, int dim) {
    auto* m = static_cast<cv::Mat*>(mat);
    if (dim < 0 || dim >= m->dims) {
        return -1;
    }
    return m->size[dim];
}

EZCV_API unsigned char* ezcv_mat_data(void* mat) {
    return static_cast<cv::Mat*>(mat)->data;
}

EZCV_API void ezcv_mat_convert_to(void* src, void* dst, int type) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    s->convertTo(*d, type);
    EZCV_CATCH(return)
}

// =========================================================================
// 图像处理 (imgproc + core)
// =========================================================================

EZCV_API void ezcv_cvt_color(void* src, void* dst, int code) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::cvtColor(*s, *d, code);
    EZCV_CATCH(return)
}

EZCV_API double ezcv_threshold(void* src, void* dst, double thresh, double maxval, int type) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    return cv::threshold(*s, *d, thresh, maxval, type);
    EZCV_CATCH(return 0.0)
}

EZCV_API void ezcv_sobel(void* src, void* dst, int ddepth, int dx, int dy, int ksize) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::Sobel(*s, *d, ddepth, dx, dy, ksize);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_add_weighted(void* src1, double alpha, void* src2, double beta, double gamma, void* dst) {
    EZCV_TRY
    auto* s1 = static_cast<cv::Mat*>(src1);
    auto* s2 = static_cast<cv::Mat*>(src2);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::addWeighted(*s1, alpha, *s2, beta, gamma, *d);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_gaussian_blur(void* src, void* dst, int kw, int kh, double sigma) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::GaussianBlur(*s, *d, cv::Size(kw, kh), sigma);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_laplacian(void* src, void* dst, int ddepth, int ksize) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::Laplacian(*s, *d, ddepth, ksize);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_convert_scale_abs(void* src, void* dst) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::convertScaleAbs(*s, *d);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_canny(void* src, void* dst, double t1, double t2) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::Canny(*s, *d, t1, t2);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_resize(void* src, void* dst, int dw, int dh, double fx, double fy, int interpolation) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::resize(*s, *d, cv::Size(dw, dh), fx, fy, interpolation);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_copy_make_border(void* src, void* dst, int top, int bottom, int left, int right, int border_type, double r, double g, double b) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    auto* d = static_cast<cv::Mat*>(dst);
    cv::copyMakeBorder(*s, *d, top, bottom, left, right, border_type, make_scalar(r, g, b));
    EZCV_CATCH(return)
}

EZCV_API int ezcv_split(void* src, void** mat_array) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    std::vector<cv::Mat> channels;
    cv::split(*s, channels);
    for (size_t i = 0; i < channels.size() && i < 4; i++) {
        mat_array[i] = new cv::Mat(channels[i]);
    }
    return static_cast<int>(channels.size());
    EZCV_CATCH(return 0)
}

EZCV_API void ezcv_merge(void** mat_array, int count, void* dst) {
    EZCV_TRY
    std::vector<cv::Mat> channels;
    channels.reserve(count);
    for (int i = 0; i < count; i++) {
        channels.push_back(*static_cast<cv::Mat*>(mat_array[i]));
    }
    auto* d = static_cast<cv::Mat*>(dst);
    cv::merge(channels, *d);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_match_template(void* image, void* templ, void* result, int method) {
    EZCV_TRY
    auto* img = static_cast<cv::Mat*>(image);
    auto* t = static_cast<cv::Mat*>(templ);
    auto* r = static_cast<cv::Mat*>(result);
    cv::matchTemplate(*img, *t, *r, method);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_match_template_masked(void* image, void* templ, void* result, int method, void* mask) {
    EZCV_TRY
    auto* img = static_cast<cv::Mat*>(image);
    auto* t = static_cast<cv::Mat*>(templ);
    auto* r = static_cast<cv::Mat*>(result);
    auto* m = static_cast<cv::Mat*>(mask);
    cv::matchTemplate(*img, *t, *r, method, *m);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_min_max_loc(void* src, double* min_val, double* max_val, int* min_x, int* min_y, int* max_x, int* max_y) {
    EZCV_TRY
    auto* s = static_cast<cv::Mat*>(src);
    cv::Point min_loc, max_loc;
    cv::minMaxLoc(*s, min_val, max_val, &min_loc, &max_loc);
    *min_x = min_loc.x;
    *min_y = min_loc.y;
    *max_x = max_loc.x;
    *max_y = max_loc.y;
    EZCV_CATCH(return)
}

EZCV_API int ezcv_find_contours(void* image, int mode, int method) {
    EZCV_TRY
    auto* img = static_cast<cv::Mat*>(image);
    g_contours.clear();
    g_hierarchy.clear();
    cv::findContours(*img, g_contours, g_hierarchy, mode, method);
    return static_cast<int>(g_contours.size());
    EZCV_CATCH(return 0)
}

EZCV_API int ezcv_contour_point_count(int index) {
    if (index < 0 || index >= static_cast<int>(g_contours.size())) return 0;
    return static_cast<int>(g_contours[index].size());
}

EZCV_API void ezcv_contour_points(int index, int* x_array, int* y_array, int count) {
    if (index < 0 || index >= static_cast<int>(g_contours.size())) return;
    auto& pts = g_contours[index];
    int n = std::min(count, static_cast<int>(pts.size()));
    for (int i = 0; i < n; i++) {
        x_array[i] = pts[i].x;
        y_array[i] = pts[i].y;
    }
}

EZCV_API void ezcv_clear_contours(void) {
    g_contours.clear();
    g_hierarchy.clear();
}

EZCV_API void ezcv_bounding_rect(int contour_index, int* x, int* y, int* w, int* h) {
    EZCV_TRY
    if (contour_index < 0 || contour_index >= static_cast<int>(g_contours.size())) {
        *x = *y = *w = *h = 0;
        return;
    }
    cv::Rect r = cv::boundingRect(g_contours[contour_index]);
    *x = r.x;
    *y = r.y;
    *w = r.width;
    *h = r.height;
    EZCV_CATCH(*x = *y = *w = *h = 0)
}

// =========================================================================
// 图像编解码 (imgcodecs)
// =========================================================================

EZCV_API void* ezcv_imdecode_mem(const unsigned char* data, int length, int flags) {
    EZCV_TRY
    // 将原始字节包装为 cv::Mat(1, length, CV_8UC1, data)
    cv::Mat buf(1, length, CV_8UC1, const_cast<unsigned char*>(data));
    cv::Mat decoded = cv::imdecode(buf, flags);
    if (decoded.empty()) {
        // 不算异常：解码失败（坏数据）也写一条提示，让 C# 端能区分
        g_last_error = "imdecode: empty result (invalid or unsupported image data)";
        return nullptr;
    }
    return new cv::Mat(decoded);
    EZCV_CATCH(return nullptr)
}

EZCV_API unsigned char* ezcv_imencode_mem(const char* ext, void* src, int* out_len) {
    EZCV_TRY
    auto* mat = static_cast<cv::Mat*>(src);
    std::vector<unsigned char> buf;
    if (!cv::imencode(ext, *mat, buf)) {
        g_last_error = "imencode: encoding failed (unsupported extension or invalid image)";
        *out_len = 0;
        return nullptr;
    }
    *out_len = static_cast<int>(buf.size());
    unsigned char* data = (unsigned char*)malloc(buf.size());
    if (!data) {
        g_last_error = "imencode: out of memory";
        *out_len = 0;
        return nullptr;
    }
    std::memcpy(data, buf.data(), buf.size());
    return data;
    EZCV_CATCH(*out_len = 0; return nullptr)
}

EZCV_API void ezcv_free_buf(void* buf) {
    free(buf);
}

// =========================================================================
// 视频采集 (videoio)
// =========================================================================

EZCV_API void* ezcv_vc_create_default(void) {
    EZCV_TRY
    return new cv::VideoCapture();
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_vc_create_index(int index, int api) {
    EZCV_TRY
    return new cv::VideoCapture(index, api);
    EZCV_CATCH(return nullptr)
}

EZCV_API int ezcv_vc_open(void* vc, int index, int api) {
    EZCV_TRY
    auto* cap = static_cast<cv::VideoCapture*>(vc);
    return cap->open(index, api) ? 1 : 0;
    EZCV_CATCH(return 0)
}

EZCV_API int ezcv_vc_is_opened(void* vc) {
    return static_cast<cv::VideoCapture*>(vc)->isOpened() ? 1 : 0;
}

EZCV_API int ezcv_vc_read(void* vc, void* dst) {
    EZCV_TRY
    auto* cap = static_cast<cv::VideoCapture*>(vc);
    auto* mat = static_cast<cv::Mat*>(dst);
    // Read 返回 bool: true 表示成功读取
    // 即使返回 false，mat 也可能被修改（变为空 Mat）
    cap->read(*mat);
    return mat->empty() ? 0 : 1;
    EZCV_CATCH(return 0)
}

EZCV_API int ezcv_vc_set(void* vc, int prop_id, double value) {
    EZCV_TRY
    auto* cap = static_cast<cv::VideoCapture*>(vc);
    return cap->set(prop_id, value) ? 1 : 0;
    EZCV_CATCH(return 0)
}

EZCV_API double ezcv_vc_get(void* vc, int prop_id) {
    EZCV_TRY
    auto* cap = static_cast<cv::VideoCapture*>(vc);
    return cap->get(prop_id);
    EZCV_CATCH(return 0.0)
}

EZCV_API void ezcv_vc_release(void* vc) {
    auto* cap = static_cast<cv::VideoCapture*>(vc);
    cap->release();
    delete cap;
}

// =========================================================================
// DNN 深度神经网络 (dnn)
// =========================================================================

// --- 模型加载 ---

EZCV_API void* ezcv_dnn_read_net(const char* model, const char* config, const char* framework, int engine) {
    EZCV_TRY
    cv::String cfg = config ? config : "";
    cv::String fw  = framework ? framework : "";
    cv::dnn::Net net = cv::dnn::readNet(model, cfg, fw, engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_onnx(const char* onnx_file, int engine) {
    EZCV_TRY
    // 显式构造 cv::String 避免与 readNetFromONNX(const char*, size_t, int) 重载歧义
    cv::dnn::Net net = cv::dnn::readNetFromONNX(cv::String(onnx_file), engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_onnx_mem(const unsigned char* data, int length, int engine) {
    EZCV_TRY
    std::vector<uchar> buf(data, data + length);
    cv::dnn::Net net = cv::dnn::readNetFromONNX(buf, engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_tensorflow(const char* model, int engine) {
    EZCV_TRY
    cv::dnn::Net net = cv::dnn::readNetFromTensorflow(cv::String(model), cv::String(), engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_tensorflow_with_config(const char* model, const char* config, int engine) {
    EZCV_TRY
    cv::dnn::Net net = cv::dnn::readNetFromTensorflow(cv::String(model), cv::String(config), engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_tensorflow_mem(const unsigned char* model_data, int model_len,
                                                      const unsigned char* config_data, int config_len,
                                                      int engine) {
    EZCV_TRY
    std::vector<uchar> model_buf(model_data, model_data + model_len);
    std::vector<uchar> config_buf;
    if (config_data != nullptr && config_len > 0) {
        config_buf.assign(config_data, config_data + config_len);
    }
    cv::dnn::Net net = cv::dnn::readNetFromTensorflow(model_buf, config_buf, engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_tflite(const char* model, int engine) {
    EZCV_TRY
    cv::dnn::Net net = cv::dnn::readNetFromTFLite(cv::String(model), engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_net_from_tflite_mem(const unsigned char* data, int length, int engine) {
    EZCV_TRY
    std::vector<uchar> buf(data, data + length);
    cv::dnn::Net net = cv::dnn::readNetFromTFLite(buf, engine);
    return new cv::dnn::Net(std::move(net));
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_read_tensor_from_onnx(const char* path) {
    EZCV_TRY
    cv::Mat tensor = cv::dnn::readTensorFromONNX(cv::String(path));
    return new cv::Mat(std::move(tensor));
    EZCV_CATCH(return nullptr)
}

// --- Net 生命周期 ---

EZCV_API void ezcv_dnn_net_release(void* net) {
    delete static_cast<cv::dnn::Net*>(net);
}

EZCV_API int ezcv_dnn_net_empty(void* net) {
    return static_cast<cv::dnn::Net*>(net)->empty() ? 1 : 0;
}

// --- Net 推理 ---

EZCV_API void ezcv_dnn_net_set_input(void* net, void* blob, const char* name) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    auto* b = static_cast<cv::Mat*>(blob);
    n->setInput(*b, name ? name : "");
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_net_set_inputs_names(void* net, const char** names, int count) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    std::vector<cv::String> nameVec;
    nameVec.reserve(count);
    for (int i = 0; i < count; i++)
        nameVec.emplace_back(names[i]);
    n->setInputsNames(nameVec);
    EZCV_CATCH(return)
}

EZCV_API void* ezcv_dnn_net_forward(void* net, const char* output_name) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    cv::Mat result = n->forward(output_name ? output_name : "");
    return new cv::Mat(std::move(result));
    EZCV_CATCH(return nullptr)
}

EZCV_API void ezcv_dnn_net_forward_multi(void* net, void** output_mats, int count, const char* output_name) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    std::vector<cv::Mat> blobs;
    blobs.resize(count);
    n->forward(blobs, output_name ? output_name : "");
    for (int i = 0; i < count; i++) {
        auto* dst = static_cast<cv::Mat*>(output_mats[i]);
        *dst = std::move(blobs[i]);
    }
    EZCV_CATCH(return)
}

// --- Net 配置 ---

EZCV_API void ezcv_dnn_net_set_preferable_backend(void* net, int backend_id) {
    EZCV_TRY
    static_cast<cv::dnn::Net*>(net)->setPreferableBackend(backend_id);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_net_set_preferable_target(void* net, int target_id) {
    EZCV_TRY
    static_cast<cv::dnn::Net*>(net)->setPreferableTarget(target_id);
    EZCV_CATCH(return)
}

// --- Net 信息查询 ---

EZCV_API int ezcv_dnn_net_get_layer_id(void* net, const char* name) {
    EZCV_TRY
    return static_cast<cv::dnn::Net*>(net)->getLayerId(name);
    EZCV_CATCH(return -1)
}

EZCV_API int ezcv_dnn_net_get_layer_names(void* net, char*** names) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    auto layerNames = n->getLayerNames();
    int count = static_cast<int>(layerNames.size());
    char** result = (char**)malloc(count * sizeof(char*));
    for (int i = 0; i < count; i++) {
        const auto& s = layerNames[i];
        result[i] = (char*)malloc(s.size() + 1);
        std::memcpy(result[i], s.c_str(), s.size() + 1);
    }
    *names = result;
    return count;
    EZCV_CATCH(*names = nullptr; return 0)
}

EZCV_API int ezcv_dnn_net_get_unconnected_out_layers(void* net, int** ids) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    auto layers = n->getUnconnectedOutLayers();
    int count = static_cast<int>(layers.size());
    int* result = (int*)malloc(count * sizeof(int));
    for (int i = 0; i < count; i++)
        result[i] = layers[i];
    *ids = result;
    return count;
    EZCV_CATCH(*ids = nullptr; return 0)
}

EZCV_API int ezcv_dnn_net_get_unconnected_out_layers_names(void* net, char*** names) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    auto layerNames = n->getUnconnectedOutLayersNames();
    int count = static_cast<int>(layerNames.size());
    char** result = (char**)malloc(count * sizeof(char*));
    for (int i = 0; i < count; i++) {
        const auto& s = layerNames[i];
        result[i] = (char*)malloc(s.size() + 1);
        std::memcpy(result[i], s.c_str(), s.size() + 1);
    }
    *names = result;
    return count;
    EZCV_CATCH(*names = nullptr; return 0)
}

EZCV_API int64_t ezcv_dnn_net_get_perf_profile(void* net, double** timings, int* timing_count) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    std::vector<double> tvec;
    int64_t total = n->getPerfProfile(tvec);
    int count = static_cast<int>(tvec.size());
    double* result = (double*)malloc(count * sizeof(double));
    for (int i = 0; i < count; i++)
        result[i] = tvec[i];
    *timings = result;
    *timing_count = count;
    return total;
    EZCV_CATCH(*timings = nullptr; *timing_count = 0; return 0)
}

EZCV_API char* ezcv_dnn_net_dump(void* net) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    auto s = n->dump();
    char* result = (char*)malloc(s.size() + 1);
    std::memcpy(result, s.c_str(), s.size() + 1);
    return result;
    EZCV_CATCH(return nullptr)
}

// --- DNN 工具函数 ---

EZCV_API void* ezcv_dnn_blob_from_image(void* image, double scale_factor,
    int w, int h, double r, double g, double b, int swap_rb, int crop) {
    EZCV_TRY
    auto* img = static_cast<cv::Mat*>(image);
    cv::Mat blob = cv::dnn::blobFromImage(*img, scale_factor,
        cv::Size(w, h), make_scalar(r, g, b), swap_rb != 0, crop != 0);
    return new cv::Mat(std::move(blob));
    EZCV_CATCH(return nullptr)
}

EZCV_API int ezcv_dnn_nms_boxes(const int* boxes_xywh, const float* scores,
    int count, float score_threshold, float nms_threshold, int* indices,
    float eta, int top_k) {
    EZCV_TRY
    std::vector<cv::Rect> boxes;
    std::vector<float> scoresVec;
    boxes.reserve(count);
    scoresVec.reserve(count);
    for (int i = 0; i < count; i++) {
        boxes.emplace_back(boxes_xywh[i * 4], boxes_xywh[i * 4 + 1],
                           boxes_xywh[i * 4 + 2], boxes_xywh[i * 4 + 3]);
        scoresVec.push_back(scores[i]);
    }
    std::vector<int> result;
    cv::dnn::NMSBoxes(boxes, scoresVec, score_threshold, nms_threshold, result, eta, top_k);
    int n = static_cast<int>(result.size());
    for (int i = 0; i < n; i++)
        indices[i] = result[i];
    return n;
    EZCV_CATCH(return 0)
}

// --- DetectionModel 高层 API ---

EZCV_API void* ezcv_dnn_detection_model_new(const char* model, const char* config) {
    EZCV_TRY
    cv::String cfg = config ? config : "";
    return new cv::dnn::DetectionModel(model, cfg);
    EZCV_CATCH(return nullptr)
}

EZCV_API void* ezcv_dnn_detection_model_from_net(void* net) {
    EZCV_TRY
    auto* n = static_cast<cv::dnn::Net*>(net);
    return new cv::dnn::DetectionModel(*n);
    EZCV_CATCH(return nullptr)
}

EZCV_API void ezcv_dnn_detection_model_release(void* dm) {
    delete static_cast<cv::dnn::DetectionModel*>(dm);
}

EZCV_API void ezcv_dnn_detection_model_set_input_size(void* dm, int w, int h) {
    EZCV_TRY
    static_cast<cv::dnn::DetectionModel*>(dm)->setInputSize(cv::Size(w, h));
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_detection_model_set_input_mean(void* dm, double r, double g, double b) {
    EZCV_TRY
    static_cast<cv::dnn::DetectionModel*>(dm)->setInputMean(make_scalar(r, g, b));
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_detection_model_set_input_scale(void* dm, double scale) {
    EZCV_TRY
    static_cast<cv::dnn::DetectionModel*>(dm)->setInputScale(scale);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_detection_model_set_input_crop(void* dm, int crop) {
    EZCV_TRY
    static_cast<cv::dnn::DetectionModel*>(dm)->setInputCrop(crop != 0);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_detection_model_set_input_swap_rb(void* dm, int swap_rb) {
    EZCV_TRY
    static_cast<cv::dnn::DetectionModel*>(dm)->setInputSwapRB(swap_rb != 0);
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_detection_model_set_preferable_backend(void* dm, int backend_id) {
    EZCV_TRY
    // Model::setPreferableBackend 接受 dnn::Backend 枚举（Net 版本才接受 int）
    static_cast<cv::dnn::DetectionModel*>(dm)->setPreferableBackend(
        static_cast<cv::dnn::Backend>(backend_id));
    EZCV_CATCH(return)
}

EZCV_API void ezcv_dnn_detection_model_set_preferable_target(void* dm, int target_id) {
    EZCV_TRY
    static_cast<cv::dnn::DetectionModel*>(dm)->setPreferableTarget(
        static_cast<cv::dnn::Target>(target_id));
    EZCV_CATCH(return)
}

EZCV_API int ezcv_dnn_detection_model_detect(void* dm, void* frame,
    int* class_ids, float* confidences, int* boxes_xywh,
    int max_count, float conf_threshold, float nms_threshold) {
    EZCV_TRY
    auto* model = static_cast<cv::dnn::DetectionModel*>(dm);
    auto* img = static_cast<cv::Mat*>(frame);

    std::vector<int> ids;
    std::vector<float> confs;
    std::vector<cv::Rect> rects;

    model->detect(*img, ids, confs, rects, conf_threshold, nms_threshold);

    int n = std::min(static_cast<int>(ids.size()), max_count);
    for (int i = 0; i < n; i++) {
        class_ids[i] = ids[i];
        confidences[i] = confs[i];
        boxes_xywh[i * 4]     = rects[i].x;
        boxes_xywh[i * 4 + 1] = rects[i].y;
        boxes_xywh[i * 4 + 2] = rects[i].width;
        boxes_xywh[i * 4 + 3] = rects[i].height;
    }
    return n;
    EZCV_CATCH(return 0)
}

// --- 通用释放 ---

EZCV_API void ezcv_free_string_array(char** arr, int count) {
    if (!arr) return;
    for (int i = 0; i < count; i++)
        free(arr[i]);
    free(arr);
}
