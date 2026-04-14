#!/bin/bash

# EasyCon2.CLI 发布脚本
# 支持 Linux、macOS、Windows (交叉编译)

set -e  # 遇到错误立即退出

# 项目名称
PROJ_NAME="EasyCon2.CLI"

# 检测操作系统
detect_os() {
    case "$(uname -s)" in
        Linux*)     echo "linux" ;;
        Darwin*)    echo "osx" ;;
        *)          echo "linux" ;;
    esac
}

# 检测架构
detect_arch() {
    case "$(uname -m)" in
        arm64|aarch64)  echo "arm64" ;;
        x86_64|amd64)   echo "x64" ;;
        i386|i686)      echo "x86" ;;
        *)              echo "x64" ;;
    esac
}

# 检测 .NET SDK 版本
detect_sdk() {
    if dotnet --list-sdks 2>/dev/null | grep -q "^10\\.0"; then
        echo "net10.0"
    elif dotnet --list-sdks 2>/dev/null | grep -q "^8\\.0"; then
        echo "net8.0"
    else
        echo "net8.0"  # 默认使用 net8.0
    fi
}

# 检测目标框架
detect_framework() {
    echo "net10.0"
}

# ========== 主流程 ==========

echo "========================================="
echo "  EasyCon2.CLI 发布脚本"
echo "========================================="

# 检测环境
OS=$(detect_os)
ARCH=$(detect_arch)
RUNTIME="${OS}-${ARCH}"
NET_SDK=$(detect_sdk)
FRAMEWORK=$(detect_framework "$OS")

echo "检测到环境:"
echo "  操作系统: $OS"
echo "  架构: $ARCH"
echo "  运行时: $RUNTIME"
echo "  SDK版本: $NET_SDK"
echo "  目标框架: $FRAMEWORK"
echo ""

# 设置目录路径
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$SCRIPT_DIR/../src"
DIST_DIR="$SCRIPT_DIR/../dist"
PUBLISH_DIR="$SRC_DIR/publish"
PROJ_DIR="$SRC_DIR/$PROJ_NAME"

# 清理旧文件
echo "正在清理旧文件..."
cd "$SRC_DIR"
rm -rf "$PUBLISH_DIR"
rm -rf "$PROJ_DIR/bin"
rm -rf "$PROJ_DIR/obj"

# 编译项目
echo "正在编译 $PROJ_NAME..."
dotnet publish "$PROJ_DIR/$PROJ_NAME.csproj" \
    -c Release \
    -r "$RUNTIME" \
    -f "$FRAMEWORK" \
    -p:PublishSingleFile=true \
    --self-contained false \
    -o "$PUBLISH_DIR"

if [ $? -ne 0 ]; then
    echo "错误: 编译失败!"
    exit 1
fi

echo "编译成功!"

# 重命名可执行文件
cd "$PUBLISH_DIR"

# dotnet publish 生成的可执行文件名称就是项目名称
if [ -f "$PROJ_NAME" ]; then
    mv "$PROJ_NAME" "ezcon"
    echo "  重命名: $PROJ_NAME -> ezcon"
else
    echo "  警告: 未找到可执行文件 $PROJ_NAME"
    ls -la
fi

# 复制到 dist 目录
echo "正在复制到 dist 文件夹..."
mkdir -p "$DIST_DIR/publish"
rsync -a --remove-source-files "$PUBLISH_DIR/" "$DIST_DIR/publish/"

# 清理临时文件
rm -rf "$PUBLISH_DIR"

echo ""
echo "========================================="
echo "  发布完成!"
echo "  输出目录: $DIST_DIR/publish"
echo "========================================="