#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <limits>
#include <stdexcept>

#if CV_VERSION_MAJOR < 5
#error EasyCon requires OpenCV 5 or newer
#endif

#ifdef _WIN32
#define EC_EXPORT extern "C" __declspec(dllexport)
#else
#define EC_EXPORT extern "C" __attribute__((visibility("default")))
#endif

struct EcImage {
    const std::uint8_t* pixels;
    std::size_t length;
    std::uint32_t width;
    std::uint32_t height;
};

struct EcMatch {
    std::int32_t x;
    std::int32_t y;
    double score;
};

struct EcVersion {
    std::uint32_t major;
    std::uint32_t minor;
    std::uint32_t revision;
    std::uint32_t compiled_major;
};

EC_EXPORT std::uint32_t easycon_opencv_abi() noexcept { return 1; }

EC_EXPORT EcVersion easycon_opencv_version() noexcept {
    return {static_cast<std::uint32_t>(cv::getVersionMajor()),
            static_cast<std::uint32_t>(cv::getVersionMinor()),
            static_cast<std::uint32_t>(cv::getVersionRevision()), CV_VERSION_MAJOR};
}

static cv::Mat image(const EcImage& value) {
    if (!value.pixels || value.width == 0 || value.height == 0 ||
        value.width > std::numeric_limits<int>::max() ||
        value.height > std::numeric_limits<int>::max() ||
        std::uint64_t(value.width) * value.height * 3 != value.length) {
        throw std::invalid_argument("invalid tightly packed RGB8 image");
    }
    // cv::matchTemplate only reads these buffers; no mutable Mat escapes this call.
    return cv::Mat(static_cast<int>(value.height), static_cast<int>(value.width),
                   CV_8UC3, const_cast<std::uint8_t*>(value.pixels),
                   std::size_t(value.width) * 3);
}

EC_EXPORT int easycon_opencv_match(const EcImage* source, const EcImage* templ,
                                  std::uint32_t method, EcMatch* result,
                                  char* error, std::size_t error_capacity) noexcept {
    try {
        if (!source || !templ || !result || method > 5) {
            throw std::invalid_argument("invalid matching arguments");
        }
        if (cv::getVersionMajor() != CV_VERSION_MAJOR) {
            throw std::runtime_error("OpenCV major ABI changed; rebuild the bridge with the new SDK");
        }
        const auto src = image(*source);
        const auto target = image(*templ);
        if (target.cols > src.cols || target.rows > src.rows) {
            throw std::invalid_argument("template exceeds search image");
        }
        cv::Mat scores;
        cv::matchTemplate(src, target, scores, static_cast<int>(method));
        double low, high;
        cv::Point low_point, high_point;
        cv::minMaxLoc(scores, &low, &high, &low_point, &high_point);
        const bool minimize = method == cv::TM_SQDIFF || method == cv::TM_SQDIFF_NORMED;
        const auto point = minimize ? low_point : high_point;
        *result = {point.x, point.y, minimize ? low : high};
        return 0;
    } catch (const std::exception& ex) {
        if (error && error_capacity) {
            const auto count = std::min(std::strlen(ex.what()), error_capacity - 1);
            std::memcpy(error, ex.what(), count);
            error[count] = '\0';
        }
        return 1;
    } catch (...) {
        if (error && error_capacity) {
            constexpr char text[] = "unknown native OpenCV exception";
            const auto count = std::min(sizeof(text) - 1, error_capacity - 1);
            std::memcpy(error, text, count);
            error[count] = '\0';
        }
        return 1;
    }
}
