#include <opencv2/core.hpp>
#include <opencv2/dnn.hpp>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <exception>
#include <limits>
#include <memory>
#include <stdexcept>

#if CV_VERSION_MAJOR < 5
#error EasyCon requires OpenCV 5 or newer
#endif

#ifdef _WIN32
#define EC_EXPORT extern "C" __declspec(dllexport)
#else
#define EC_EXPORT extern "C" __attribute__((visibility("default")))
#endif

struct EcDnn {
    cv::dnn::Net net;
    cv::Mat output;
};

struct EcTensor {
    const float* values;
    std::size_t length;
    std::int64_t shape[3];
};

static void message(char* error, std::size_t capacity, const char* text) noexcept {
    if (error && capacity) {
        const auto count = std::min(std::strlen(text), capacity - 1);
        std::memcpy(error, text, count);
        error[count] = '\0';
    }
}

EC_EXPORT std::uint32_t easycon_opencv_dnn_abi() noexcept { return 1; }

EC_EXPORT int easycon_opencv_dnn_open(const std::uint8_t* model, std::size_t length,
    EcDnn** result, std::int64_t* input_shape, char* error, std::size_t capacity) noexcept {
    if (result) *result = nullptr;
    try {
        if (!model || !length || !result || !input_shape) {
            throw std::invalid_argument("missing ONNX model or output arguments");
        }
        if (cv::getVersionMajor() != CV_VERSION_MAJOR) {
            throw std::runtime_error("OpenCV major ABI changed; rebuild the bridge");
        }
        auto state = std::make_unique<EcDnn>();
        // Explicit native OpenCV 5 engine: no ORT engine or automatic fallback.
        state->net = cv::dnn::readNetFromONNX(reinterpret_cast<const char*>(model), length,
                                            cv::dnn::ENGINE_NEW);
        const auto graph = state->net.getMainGraph();
        if (!graph || graph->inputs().size() != 1 || graph->outputs().size() != 1) {
            throw std::invalid_argument("expected one model input and one output");
        }
        const auto& input = state->net.argData(graph->inputs()[0]);
        if (input.type != CV_32F || input.shape.size() != 4) {
            throw std::invalid_argument("expected float32 NCHW input");
        }
        for (std::size_t i = 0; i < 4; ++i) {
            if (input.shape[i] == 0) throw std::invalid_argument("zero model input dimension");
            // New-engine symbolic dimensions have distinct negative IDs.
            input_shape[i] = input.shape[i] < 0 ? -1 : input.shape[i];
        }
        *result = state.release();
        return 0;
    } catch (const std::exception& ex) {
        message(error, capacity, ex.what());
    } catch (...) {
        message(error, capacity, "unknown OpenCV DNN model-loading exception");
    }
    return 1;
}

EC_EXPORT int easycon_opencv_dnn_forward(EcDnn* state, const float* input,
    std::size_t length, std::uint32_t height, std::uint32_t width,
    EcTensor* result, char* error, std::size_t capacity) noexcept {
    if (result) *result = {};
    try {
        if (!state || !input || !result || !height || !width ||
            height > std::numeric_limits<int>::max() ||
            width > std::numeric_limits<int>::max() ||
            std::uint64_t(height) * width * 3 != length) {
            throw std::invalid_argument("expected float32 [1,3,H,W] input");
        }
        const int shape[] = {1, 3, static_cast<int>(height), static_cast<int>(width)};
        // Own input storage even if Net retains the blob after forward returns.
        const cv::Mat blob = cv::Mat(4, shape, CV_32F, const_cast<float*>(input)).clone();
        state->net.setInput(blob);
        state->output = state->net.forward();
        const auto& output = state->output;
        if (output.type() != CV_32F || output.dims != 3 || !output.isContinuous()) {
            throw std::runtime_error("expected contiguous float32 [1,T,C] output");
        }
        result->values = output.ptr<float>();
        result->length = output.total();
        for (int i = 0; i < 3; ++i) result->shape[i] = output.size[i];
        return 0;
    } catch (const std::exception& ex) {
        message(error, capacity, ex.what());
    } catch (...) {
        message(error, capacity, "unknown OpenCV DNN inference exception");
    }
    return 1;
}

EC_EXPORT void easycon_opencv_dnn_delete(EcDnn* state) noexcept {
    delete state;
}
