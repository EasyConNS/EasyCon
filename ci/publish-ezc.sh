#!/bin/bash

# EasyCon2 发布脚本
# 支持 Linux、macOS、Windows (交叉编译)
# 发布 CLI 和 Avalonia UI 项目

set -e  # 遇到错误立即退出

# 项目名称
CLI_PROJ_NAME="EasyCon2.CLI"
AVALONIA_PROJ_NAME="EasyCon2.Avalonia"

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
echo "  EasyCon2 发布脚本"
echo "  发布 CLI 和 Avalonia UI 项目"
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
CLI_PROJ_DIR="$SRC_DIR/$CLI_PROJ_NAME"
AVALONIA_PROJ_DIR="$SRC_DIR/$AVALONIA_PROJ_NAME"

# ========== 发布 CLI 项目 ==========

echo "========================================="
echo "  [1/2] 正在发布 CLI 项目"
echo "========================================="

# 清理旧文件
echo "正在清理旧文件..."
cd "$SRC_DIR"
rm -rf "$PUBLISH_DIR"
rm -rf "$CLI_PROJ_DIR/bin"
rm -rf "$CLI_PROJ_DIR/obj"

# 编译 CLI 项目
echo "正在编译 $CLI_PROJ_NAME..."
dotnet publish "$CLI_PROJ_DIR/$CLI_PROJ_NAME.csproj" \
    -c Release \
    -r "$RUNTIME" \
    -f "$FRAMEWORK" \
    -p:PublishSingleFile=true \
    --self-contained false \
    -o "$PUBLISH_DIR"

if [ $? -ne 0 ]; then
    echo "错误: CLI 编译失败!"
    exit 1
fi

echo "CLI 编译成功!"

# 重命名 CLI 可执行文件
cd "$PUBLISH_DIR"

# dotnet publish 生成的可执行文件名称就是项目名称
if [ -f "$CLI_PROJ_NAME" ]; then
    mv "$CLI_PROJ_NAME" "ezcon"
    echo "  重命名: $CLI_PROJ_NAME -> ezcon"
else
    echo "  警告: 未找到可执行文件 $CLI_PROJ_NAME"
    ls -la
fi

echo "CLI 发布完成!"

# ========== 发布 Avalonia 项目 ==========

echo ""
echo "========================================="
echo "  [2/2] 正在发布 Avalonia UI 项目"
echo "========================================="

# 只在 Linux 和 macOS 上发布 Avalonia 项目
if [ "$OS" == "linux" ] || [ "$OS" == "osx" ]; then
    # 清理 Avalonia 编译产物（不影响已发布的 CLI 文件）
    echo "正在清理 Avalonia 编译产物..."
    rm -rf "$AVALONIA_PROJ_DIR/bin"
    rm -rf "$AVALONIA_PROJ_DIR/obj"

    # 编译 Avalonia 项目到同一个发布目录
    echo "正在编译 $AVALONIA_PROJ_NAME..."
    dotnet publish "$AVALONIA_PROJ_DIR/$AVALONIA_PROJ_NAME.csproj" \
        -c Release \
        -r "$RUNTIME" \
        -f "$FRAMEWORK" \
        -p:PublishSingleFile=true \
        --self-contained false \
        -o "$PUBLISH_DIR"

    if [ $? -ne 0 ]; then
        echo "错误: Avalonia 编译失败!"
        exit 1
    fi

    echo "Avalonia UI 编译成功!"
else
    echo "跳过 Avalonia 发布（仅支持 Linux 和 macOS 平台）"
fi
# 删除调试文件
if [ -d "$PUBLISH_DIR" ]; then
    rm -f "$PUBLISH_DIR"/*.pdb 2>/dev/null || true
    echo "已删除 pdb 调试文件"
fi

# ========== 复制到 dist 目录 ==========

echo ""
echo "========================================="
echo "  正在复制到 dist 目录"
echo "========================================="

# 复制到 dist 目录
mkdir -p "$DIST_DIR/publish"
rsync -a --remove-source-files "$PUBLISH_DIR/" "$DIST_DIR/publish/"

# 清理临时文件
rm -rf "$PUBLISH_DIR"

# ========== 完成 ==========

echo ""
echo "========================================="
echo "  全部发布完成!"
echo "  输出目录: $DIST_DIR/publish"
echo "========================================="