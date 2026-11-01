cmake_minimum_required(VERSION 3.21)
project(easycon_opencv LANGUAGES CXX)

# Discover the caller's native SDK. Never download or build OpenCV here.
find_package(OpenCV REQUIRED COMPONENTS core imgproc dnn)
if(OpenCV_VERSION VERSION_LESS "5.0")
    message(FATAL_ERROR "EasyCon requires OpenCV >= 5.0; found ${OpenCV_VERSION}")
endif()

add_library(easycon_opencv SHARED bridge.cpp dnn.cpp)
target_compile_features(easycon_opencv PRIVATE cxx_std_17)
target_include_directories(easycon_opencv PRIVATE ${OpenCV_INCLUDE_DIRS})
target_link_libraries(easycon_opencv PRIVATE ${OpenCV_LIBS})
set_target_properties(easycon_opencv PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
    BUILD_WITH_INSTALL_RPATH YES)
if(APPLE)
    set_target_properties(easycon_opencv PROPERTIES INSTALL_RPATH "@loader_path")
elseif(UNIX)
    set_target_properties(easycon_opencv PROPERTIES INSTALL_RPATH "$ORIGIN")
endif()
set(SYSTEM_RUNTIME_EXCLUDES "[Ww]indows[/\\\\][Ss]ystem32[/\\\\]")
if(APPLE)
    list(APPEND SYSTEM_RUNTIME_EXCLUDES "^/System/Library/" "^/usr/lib/")
elseif(UNIX)
    list(APPEND SYSTEM_RUNTIME_EXCLUDES "^/(usr/)?lib(64)?/.*(ld-linux|libc\\.so|libm\\.so|libdl\\.so|libpthread\\.so|librt\\.so|libstdc\\+\\+\\.so|libgcc_s\\.so)")
endif()
install(TARGETS easycon_opencv
    RUNTIME_DEPENDENCIES
        DIRECTORIES "${EASYCON_OPENCV_RUNTIME_DIR}"
        PRE_EXCLUDE_REGEXES "api-ms-.*" "ext-ms-.*"
        POST_EXCLUDE_REGEXES ${SYSTEM_RUNTIME_EXCLUDES}
    LIBRARY DESTINATION . RUNTIME DESTINATION . FRAMEWORK DESTINATION .)
