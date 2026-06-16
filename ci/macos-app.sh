#!/usr/bin/env bash
# EasyCon2 macOS .app 打包脚本
# 编译 Avalonia + CLI，组装成 .app；CLI 重命名为 ezcon 作为命令行工具

set -e
set -u

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/publish"
DIST_DIR="$ROOT_DIR/dist"
TFM="net10.0"
RID="osx-arm64"
PROJ_NAME="EasyCon2.Avalonia"   # .csproj 项目名（编译输出的可执行文件名）
APP_DISPLAY="EasyCon"           # .app 包名 / 显示名
CLI_NAME="EasyCon2.CLI"
VERSION="$(git -C "$ROOT_DIR" rev-parse --short HEAD)"

echo "========================================="
echo "  EasyCon2 macOS .app 打包"
echo "  架构: $RID   版本: $VERSION"
echo "========================================="

mkdir -p "$DIST_DIR"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR"

# ========== 编译 ==========
publish_proj() {
    local proj="$1"
    local dir="$ROOT_DIR/src/$proj"
    echo "正在编译 $proj ..."
    rm -rf "$dir/bin" "$dir/obj"
    dotnet publish "$dir/$proj.csproj" --nologo \
        -c Release -r "$RID" -f "$TFM" \
        -p:PublishSingleFile=true --self-contained true \
        -o "$STAGE_DIR"
    # 必须自包含（self-contained）：Finder 双击启动的 GUI 应用不会继承 shell 的
    # DOTNET_ROOT，且本机 dotnet 经 Homebrew 安装、不在 /usr/local/share/dotnet，
    # 框架依赖构建会因找不到运行时而静默退出（"闪退"）。
}

publish_proj "$CLI_NAME"
publish_proj "$PROJ_NAME"

# CLI 重命名为 ezcon
if [ -f "$STAGE_DIR/$CLI_NAME" ]; then
    mv "$STAGE_DIR/$CLI_NAME" "$STAGE_DIR/ezcon"
    echo "  重命名: $CLI_NAME -> ezcon"
fi

# ========== 组装 .app bundle ==========
echo "正在组装 .app ..."
APP_BUNDLE="$STAGE_DIR/$APP_DISPLAY.app"
mkdir -p "$APP_BUNDLE/Contents/MacOS"

# 将 stage 内除 .app 外的产物移入 MacOS/
for f in "$STAGE_DIR"/*; do
    [ -e "$f" ] || continue
    case "$f" in *.app) continue ;; esac
    mv "$f" "$APP_BUNDLE/Contents/MacOS/"
done

# 生成 Info.plist（替换版本号）
sed "s/SOURCE_GIT_VERSION/$VERSION/g" \
    "$ROOT_DIR/ci/app/Info.plist" > "$APP_BUNDLE/Contents/Info.plist"

# 写入应用图标（由 favicon 预生成的 .icns，免依赖 sips/iconutil）
mkdir -p "$APP_BUNDLE/Contents/Resources"
cp "$ROOT_DIR/ci/app/AppIcon.icns" "$APP_BUNDLE/Contents/Resources/AppIcon.icns"

# 清理调试文件
rm -f "$APP_BUNDLE/Contents/MacOS/"*.pdb 2>/dev/null || true

# ========== 输出到 dist ==========
rm -rf "$DIST_DIR/publish"
mkdir -p "$DIST_DIR/publish"
mv "$APP_BUNDLE" "$DIST_DIR/publish/"

echo ""
echo "========================================="
echo "  打包完成!"
echo "  应用: $DIST_DIR/publish/$APP_DISPLAY.app"
echo "  CLI:  $DIST_DIR/publish/$APP_DISPLAY.app/Contents/MacOS/ezcon"
echo "  绑定系统命令: sudo ln -sf /Applications/$APP_DISPLAY.app/Contents/MacOS/ezcon /usr/local/bin/ezcon"
echo "========================================="
