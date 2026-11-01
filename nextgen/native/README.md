# 原生 OpenCV 运行库

模板匹配与 PP-OCR 推理直接使用原生 OpenCV C++ API。`bridge/bridge.cpp`／`bridge/dnn.cpp` 是本项目维护的最小 C ABI 桥接，Rust 使用 `libloading` 在运行时加载它。桥接封装版本查询、一次模板匹配，以及 DNN 模型会话的打开／推理／释放，C++ 对象与异常留在原生边界内。没有 OpenCvSharp、OpenCvSharpExtern、NuGet 或 .NET 运行时依赖。

## 官方 Windows SDK

项目提供的 OpenCV 二进制只采用 **OpenCV 官方 GitHub Release**。本轮先接入 Windows；macOS／Linux 等待调用方提供 SDK，不分发其他来源的预编译库。目前仓库没有预构建好的 Windows 桥接归档；以下命令与 Windows CI 会从官方 SDK 生成完整依赖包。

`setup_windows.py` 固定下载官方 **5.0.0** Windows SDK，并按发布资产的 SHA-256 校验。这是默认供应版本，运行时最低版本仍是主版本 5，并非锁死 5.0.0。官方资产与摘要：

| 架构 | 官方发布文件 | SHA-256 |
|---|---|---|
| x64 | `opencv-5.0.0-windows.exe` | `9c6c1fcea58acdf06edba13148b2246e00c2658143fa51e61ecd370db8c39f63` |
| ARM64 | `opencv-5.0.0-windows-arm64.exe` | `087bd5cc1f2d6d44f71ec941babbc49e0426fb81c4ecd59ab7721890c852b8a6` |

下载地址固定为 `https://github.com/opencv/opencv/releases/download/5.0.0/` 下的对应文件，不回退到包管理器或 NuGet。需要 Python >= 3.11、7-Zip（`7z`／`7zz`）、CMake >= 3.21 与 MSVC C++17 工具链。在匹配架构的 MSVC Developer PowerShell 中，从 `nextgen` 执行：

```powershell
python native/setup_windows.py --arch x64 --build
# ARM64 在对应 Windows ARM64 工具环境中改用 --arch arm64。
cargo test --locked --lib real_opencv -- --ignored
cargo run --locked --bin easycon-vision -- match `
  --image tests/fixtures/hello.png --template tests/fixtures/hello.png
```

脚本下载并解压到忽略跟踪的 `.deps/opencv/`，发现 SDK 的 CMake 配置与实际 DLL 目录，**只编译本项目桥接，不编译 OpenCV**。构建后复制原生 DLL、非系统依赖和 SDK 许可证到 `native/runtimes/win-x64/`，并生成 `native/archives/win-x64.zip` 及来源／SHA-256 清单。ARM64 使用对应目录。Windows 目标系统还需匹配的 VC Runtime。重复准备会重新校验下载包并保留已解压的 SDK；构建输出目录必须为空，避免覆盖用户替换的运行库。

省略 `--build` 可在其他系统上只下载、校验和解压 Windows SDK；该操作不验证 DLL 的执行兼容性。已有 SDK 可直接使用下文 `build.py`，升级 SDK 时也使用这一入口。Windows x64 CI 构建依赖包，执行六种模板匹配度量和 CLI 冒烟后上传归档；ARM64 尚待目标机器验收。

取得对应平台归档和同名清单后，放入 `native/archives/`：

```sh
python native/prepare.py --rid win-x64
```

`prepare.py` 校验压缩包和每个文件摘要，解压到 `native/runtimes/<rid>/`；不会覆盖用户替换过的文件。清单用于核验提供的依赖，**运行时不以摘要锁死用户自选库**。没有对应归档时明确报错，不自动下载其他来源。

## 选择依赖与部署

加载顺序：

1. CLI `match --opencv-dir <目录>`，或代码 `OpenCv5::open(directory)`。
2. 工厂默认读取 `EASYCON_OPENCV_DIR`；显式路径失败就返回错误。
3. 可执行文件旁 `native/<rid>/`。
4. Cargo 工作区 `native/runtimes/<rid>/`，用于开发和测试。

目录应包含本项目桥接以及它动态链接的原生 OpenCV 库与非系统依赖。

| 系统 | 桥接文件 | 原生库形式 |
|---|---|---|
| Windows | `easycon_opencv.dll` | `opencv_world*.dll` 或模块 DLL |
| macOS | `libeasycon_opencv.dylib` | `libopencv_*.dylib` |
| Linux | `libeasycon_opencv.so` | `libopencv_*.so*` |

Windows 使用 `LoadLibraryEx` 的 DLL 所在目录与系统搜索范围，不改进程 PATH；Unix 使用立即解析、局部加载。桥接运行路径为 macOS `@loader_path`／Linux `$ORIGIN`。部署时复制完整目录，不能只复制桥接；Rust 默认构建与 CLI 帮助不加载原生库。

`native/package.py --executable <宿主程序> --output <新输出目录>` 将程序与已准备的 `native/runtimes/<rid>/` 完整复制为上述可自动加载的布局，包括许可证；也可用 `--runtime` 指定已有完整运行包。脚本不覆盖已有输出、不下载依赖。Windows x64 CI 同时生成包含 `easycon-run.exe` 与原生库的宿主产物，使用方式见 [项目运行宿主](../project/README.md)。

## 最低版本与升级

- SDK 配置和桥接编译均拒绝 OpenCV < 5。
- 加载时验证本项目桥接 ABI 版本为 1，以及实际 OpenCV 主版本 >= 5。
- 不精确固定 5.0.0，也不禁止主版本 6 或更高。
- 同一主版本内可替换 ABI 兼容的更高版本库，保持依赖名称与引用关系匹配。
- **OpenCV 跨主版本不保证 C++ ABI 兼容。** 升级到更高主版本需用其 SDK 重建这段小桥接；Rust 调用接口不变。若桥接编译主版本与实际库不符，加载时明确拒绝。
- 更换动态库后重启使用它的进程。新版本仍需运行模板匹配验收，版本检查不能证明算法行为和任意第三方构建均兼容。

## 用自备原生 SDK 构建桥接

需要 CMake >= 3.21、C++17 编译器和含 `core`／`imgproc`／`dnn` 的平台原生 SDK；Linux 打包还需要 `patchelf`。**仅编译项目自有桥接，不下载或编译 OpenCV。** `--opencv-dir` 必须显式指向 `OpenCVConfig.cmake` 所在目录。

```sh
# macOS / Linux 示例；路径按实际 SDK 替换。
python3 native/build.py \
  --opencv-dir /path/to/sdk/lib/cmake/opencv5 \
  --library-dir /path/to/sdk/lib \
  --output /path/to/new-runtime

EASYCON_OPENCV_DIR=/path/to/new-runtime \
cargo test --locked --lib real_opencv -- --ignored
```

Windows 已有官方原生 SDK 时，在 MSVC Developer PowerShell 中显式指定路径：

```powershell
python native/build.py `
  --opencv-dir C:/sdk/opencv/build `
  --library-dir C:/sdk/opencv/build/x64/vc16/bin `
  --output C:/easycon/native-runtime
$env:EASYCON_OPENCV_DIR = 'C:/easycon/native-runtime'
cargo test --locked --lib real_opencv -- --ignored
```

VC 版本目录以实际 SDK 为准，系统须具备对应 VC Runtime。CMake 收集非系统动态依赖；macOS 额外将所有非系统依赖改为同目录引用并重签名。Linux 为每个复制的非系统 `.so` 设置 `$ORIGIN` RUNPATH，避免只设置桥接路径却漏掉传递依赖。Linux 需自备原生 SDK，系统 glibc／C++ runtime 仍须满足该 SDK 的最低要求；不能用 OpenCV 4 的系统开发包替代。

构建输出要求为空目录，避免覆盖现有依赖。可加 `--archive --source '<官方资产来源与版本>' --notices-dir <SDK完整许可证目录>` 生成依赖 ZIP 和校验清单；SDK 目录应包含 OpenCV 及其第三方依赖的许可证；额外单独的 OpenCV 许可证可用 `--license-file` 指定。许可证复制到输出的 `licenses/` 并随归档保存。此命令针对当前宿主平台，不执行交叉编译。

## 桥接契约

桥接 ABI 1 导出 `easycon_opencv_abi`、`easycon_opencv_version`、`easycon_opencv_match`。输入是带长度的只读 RGB8 缓冲与尺寸，输出是候选坐标和评分；没有 native Mat 指针跨越 Rust 边界。OpenCV unsafe 限制在 `opencv-runtime/`，Tesseract 的 C API 同样隔离在 `tesseract-runtime/`；主库仍禁止 unsafe。

DNN 扩展独立校验 `easycon_opencv_dnn_abi()==1`，并导出 `easycon_opencv_dnn_open`／`easycon_opencv_dnn_forward`／`easycon_opencv_dnn_delete`。原有匹配 ABI 保持 1；旧桥接缺少 DNN 导出时，PP-OCR 明确报错，需用对应 SDK 重建。DNN 使用 `ENGINE_NEW`，输入为单个 float32 NCHW 张量，输出为 float32 `[1,T,C]`，由 Rust 继续解码。模型使用内存字节加载；handle、Net 和输出存储在 C++ 内部，退出前释放。

原生匹配与 DNN `forward` 不可被强制抢占，取消在计算前后检查。异步宿主可通过 [NativeMatcher／NativeOcr](../native-work/README.md) 在专用线程托管初始化、调用与销毁；直接同步调用仍需宿主管理阻塞工作。不承诺在超时到达时立刻停止正在执行的 OpenCV 调用。本轮没有更改桥接 ABI，不需要因此重建现有运行包。

`package.py` 可通过 `--project <工作流目录>` 同时复制完整项目到发布包 `project/`，包括 `models/` 中实际存在的 PP-OCR ONNX／字典和 Tesseract traineddata。模型路径保持相对 workflow.json，不下载或转换模型。OCR 通用宿主构建使用 `--all-features`；Tesseract 动态库及其依赖需另放到准备的 native runtime 目录，工具会随该目录一并复制。
