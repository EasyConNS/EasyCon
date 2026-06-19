#!/bin/bash
# src/EzCv/native/build.sh — 用 Zig 跨平台编译 ezcv_native (osx-arm64 / linux-x64)
#
# 关键设计：ezcv 用 Zig 一次跨编两平台。OpenCV 不做跨编（各平台另行准备）。
# 对 opencv 的依赖处理：
#   - osx-arm64 (本机)：链接本机已构建的 opencv_world（需先 Depend/build_opencv.sh）
#   - linux-x64：构建期不链接 opencv（.so 允许 undefined 符号 lazy binding），运行期
#       由 opencv_world.so 提供；runtimes 目录只放 ezcv 库，opencv_world 另行放入。
#
# 目录约定 (本脚本位于 src/EzCv/native/)：
#   SCRIPT_DIR   = src/EzCv/native
#   RUNTIMES     = src/EzCv/native/../runtimes        (= src/EzCv/runtimes)
#   OPENCV_INSTALL osx 平台 OpenCV install 前缀
#
# 用法:
#   cd src/EzCv/native && ./build.sh                 # 编全部两平台
#   BUILD_OSX=0 ./build.sh                            # 跳过 osx
#   BUILD_LINUX64=0 ./build.sh                        # 只编 osx
#
# 环境变量:
#   OPENCV_INSTALL  osx 平台 OpenCV install 前缀 (覆盖默认 Depend/opencv/build/install)
#   ZIG             zig 可执行文件 (默认 PATH 查找)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
EZCV_SRC="${SCRIPT_DIR}"                              # build.zig 与 cpp 同在本目录
RUNTIMES="${SCRIPT_DIR}/../runtimes"                  # src/EzCv/runtimes
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
OPENCV_INSTALL="${OPENCV_INSTALL:-${REPO_ROOT}/Depend/opencv/build/install}"
ZIG="${ZIG:-zig}"
OPTIMIZE="ReleaseFast"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

BUILD_OSX="${BUILD_OSX:-1}"
BUILD_LINUX64="${BUILD_LINUX64:-1}"

if ! command -v "${ZIG}" >/dev/null 2>&1; then
    echo "错误: 未找到 zig"; exit 1
fi

CVINC="${OPENCV_INSTALL}/include/opencv5"            # osx install 布局
EZCV_CXX_FLAGS=(-std=c++17 -O2 -fPIC)

# ---------------------------------------------------------------------------
# osx-arm64：链接本机 opencv_world (走 build.zig)
# ---------------------------------------------------------------------------
build_osx() {
    if [ ! -f "${OPENCV_INSTALL}/lib/libopencv_world.dylib" ]; then
        echo "[osx] 跳过：未找到 ${OPENCV_INSTALL}/lib/libopencv_world.dylib"
        echo "       请先运行 ${REPO_ROOT}/Depend/build_opencv.sh"
        return
    fi
    echo "========================================= [osx-arm64] (link opencv)"
    ( cd "${EZCV_SRC}" && rm -rf zig-out && \
      "${ZIG}" build -Dtarget=aarch64-macos -Doptimize="${OPTIMIZE}" -Dopencv_dir="${OPENCV_INSTALL}" )

    local out="${RUNTIMES}/osx-arm64/native"
    mkdir -p "${out}"
    cp "${EZCV_SRC}/zig-out/lib/libezcv_native.dylib" "${out}/"
    cp -L "${OPENCV_INSTALL}/lib/libopencv_world.dylib" "${out}/libopencv_world.dylib"
    rm -f "${out}/libopencv_world".*.dylib
    # 归一 OpenCV 版本化 soname
    install_name_tool -id "@rpath/libopencv_world.dylib" "${out}/libopencv_world.dylib"
    install_name_tool -change \
        "@rpath/libopencv_world.500.dylib" "@rpath/libopencv_world.dylib" "${out}/libezcv_native.dylib"
    echo "[osx] $(otool -L "${out}/libezcv_native.dylib" | grep -c opencv) opencv deps OK"
}

# ---------------------------------------------------------------------------
# linux-x64：lazy binding，不链接 opencv
# ---------------------------------------------------------------------------
build_linux() {
    echo "========================================= [linux-x64] (lazy opencv)"
    local obj="${WORK}/ezcv_linux.o"
    "${ZIG}" c++ -target x86_64-linux-gnu "${EZCV_CXX_FLAGS[@]}" -c \
        "${EZCV_SRC}/ezcv_native.cpp" -I "${CVINC}" -o "${obj}"

    local out="${RUNTIMES}/linux-x64/native"
    mkdir -p "${out}"
    "${ZIG}" c++ -target x86_64-linux-gnu -shared "${EZCV_CXX_FLAGS[@]}" \
        "${obj}" \
        -Wl,-soname,libezcv_native.so -Wl,-rpath,\$ORIGIN \
        -o "${out}/libezcv_native.so"
    echo "[linux] exports=$(nm -D "${out}/libezcv_native.so" | grep -cE ' T .*ezcv_'), undef opencv=$(nm -D "${out}/libezcv_native.so" | grep -cE ' U .*_ZN2cv')"
}

echo "EasyCon ezcv_native 跨平台构建 (Zig)"
echo "  native src : ${EZCV_SRC}"
echo "  opencv     : ${OPENCV_INSTALL}"
echo "  runtimes    : ${RUNTIMES}"
echo "  work dir    : ${WORK}"
[ "${BUILD_OSX}"     = "1" ] && build_osx
[ "${BUILD_LINUX64}" = "1" ] && build_linux

echo ""
echo "========================================= 产物总览"
find "${RUNTIMES}" -type f \( -name '*.dylib' -o -name '*.dll' -o -name '*.so' \) -exec ls -la {} \;
echo ""
echo "注意: linux-x64 目录暂未含 opencv_world，需另行准备后放入。"
