# 图像感知原子能力

当前实现：不可变 RGB 图像／ROI、区域 RGB 统计、显式双图差异测量、可替换的模板匹配与 OCR 接口、Tesseract OCR，以及使用原生 OpenCV 5 DNN 的 PP-OCR 单行识别。模板匹配与 PP-OCR 共用动态加载的 OpenCV 桥接，最低主版本为 5；PP-OCR 文字检测仍暂缓。

所有识别消费显式图像，不自行采集、重试、判断业务命中或产生节点 trace。与现有 .NET 项目无构建和运行依赖。

| 能力 | 接口／实现 | Cargo feature |
|---|---|---|
| 像素、ROI、来源与坐标 | `Image` | 默认可用 |
| 区域 RGB 统计 | `measure::region_statistics` | 默认可用，无原生依赖 |
| 显式双图差异 | `measure::image_difference` | 默认可用，无原生依赖 |
| 模板匹配 | `TemplateMatcher` → 原生 OpenCV >= 5 | 默认提供接口，运行时需原生依赖 |
| OCR | `OcrBackend` → Tesseract 5 C API，运行时加载动态库 | `ocr-tesseract`，构建无需原生 SDK |
| OCR | `OcrBackend` → PP-OCR 单行 ONNX → OpenCV >= 5 DNN | `ocr-ppocr`，运行时需含 dnn 的原生依赖 |

`create_template_matcher(TemplateBackendKind::OpenCv5)` 返回 `Result<Box<dyn TemplateMatcher>>`；`create_ocr(OcrConfig)` 返回 `Result<Box<dyn OcrBackend>>`。宿主可注入实现相同 trait 的 mock。未启用的 OCR 后端报 `BackendUnavailable`；已预留但未实现的能力报 `BackendNotImplemented`。不自动切换后端或返回假结果。

异步宿主可使用 [NativeMatcher／NativeOcr](../native-work/README.md)：在专用线程内加载、调用和销毁原生引擎，并借用到原语／IR 绑定；通过 `shutdown().await` 收尾。模型与字典继续由上层工作流逐套指定，核心不维护默认模型资产。直接同步接口保留，trait 默认异步入口仍内联执行。

## 图像、坐标与结果

`Image::new(width, height, rgb)` 校验非零尺寸、i32 维度上限和精确 RGB8 缓冲长度。像素不可变，克隆共享 `Arc`；`Image::from_frame` 共享原帧像素并保留身份与时间。

- `pixel(x,y)`、`crop(Rect)` 和识别 ROI 参数使用当前图像的局部坐标。
- `Rect` 采用左上原点，x 向右、y 向下，右／下边界不包含。空区域、越界或整数溢出报错，不自动裁切。
- 嵌套裁剪保留原图偏移，匹配框和 OCR 多边形返回原始图像／帧坐标。
- 文件／合成图像的 `source` 为 `None`，不伪造摄像头身份。

`TemplateMatch` 的接口保留搜索区域、候选框、原始评分、可用的 confidence、来源和实现标识；低评分应是正常测量结果。支持六种 OpenCV 度量：`SqDiff`、`SqDiffNormed`、`CCorr`、`CCorrNormed`、`CCoeff`、`CCoeffNormed`，默认 `SqDiffNormed`。未归一化模式不应伪造 confidence；confidence 不表示概率或跨模式可比评分。归一化度量的 confidence 分别为 `1-score`、`score`、`(score+1)/2`，限制到 0..1；非归一化度量返回 `None`。CCoeff 模式拒绝每个通道都没有空间变化的常量模板。

原生路径为 `vision::opencv::OpenCv5` → `easycon-opencv-runtime` → 项目自有 C ABI 桥接 → 原生 `opencv_core`／`opencv_imgproc` 动态库，或官方 `opencv_world`。不使用 OpenCvSharp／OpenCvSharpExtern，不加载 .NET。Rust 构建不需要 OpenCV SDK；运行时用 `EASYCON_OPENCV_DIR`／CLI `--opencv-dir` 选择依赖目录，默认使用项目内或可执行文件旁的依赖。首次初始化检查桥接 ABI 与实际 OpenCV 版本；错误不回退到其他库。

OpenCV 二进制来源限定为官方 Release：先提供 Windows SDK 准备与桥接打包流程，macOS／Linux 等待自备 SDK。没有对应库时明确报错。官方资产、配置、最低版本、平台验证状态和替换规则见 [原生库说明](../native/README.md)。

`OcrResult` 保留 kind、原图区域、来源、全文、boxes 及 profile。每个 OcrBox 包含 text、可用的 0..1 confidence 和四点 polygon；`confidence()` 返回所有 box 分数的算术均值，空结果或任一未知分数为 None。IR 输出同时暴露顶层 confidence；这个汇总不是概率，也不承诺跨 kind 可比。空文本是正常结果；模型缺失、形状不符、字典不匹配、非法输出是错误。Tesseract 返回单词框，PP-OCR 当前返回所选文字行 ROI，不伪造更精确的字框。

## RGB 测量原子能力

`vision::measure` 的两项能力均为 Pure：只消费显式不可变图像，没有采集、隐式上一帧或业务阈值。像素本身由已有的 `Image::pixel(x, y)` 取得。测量对 RGB8 编码值直接运算，不做线性光／亮度转换。

- `region_statistics(image, region, token)` 返回来源、原图区域、像素数及 RGB 各通道的最小值、最大值和算术平均值。通道顺序为 RGB，范围 0..255；实现标识为 `rgb8-statistics/v1`。
- `image_difference(left, right, left_region, right_region, token)` 对齐比较两个显式区域的相同局部位置，返回左右来源与原图区域、像素数、发生任一通道变化的像素数、全部 RGB 通道对的 MAE／RMSE 和最大单通道差值。MAE／RMSE 范围 0..255，0 为完全相同，不是 confidence；实现标识为 `rgb8-difference/v1`。

ROI 均相对于各自传入的图像，`None` 代表该图像全域；嵌套裁剪仍返回原图坐标。差异要求两侧 ROI 尺寸完全一致，不自动缩放或配准；不同大小的原图可以选择同样大小的 ROI。来源可以不同，不据此推断时钟同步或画面已对齐。

空／越界／溢出 ROI、尺寸不一致返回 `InvalidArgument`。平均值前使用整数累计，支持 RGB8 全值域，不因 `u8` 相减而回绕。测量不复制完整 ROI，按行读取，每最多 4096 个像素检查一次取消，并在入口／返回前检查。异步宿主应放入自己管理的阻塞任务。

是否变化、是否为黑屏以及比例阈值由调用方判断；多次测量之间的状态属于后续组合子。

## Tesseract

显式提供 `runtime_library`、tessdata 目录、语言码（如 `eng` 或 `eng+chi_sim`）和文本 PSM 3..13。CLI 使用 `--tesseract-library` 指定动态库。所需 traineddata 必须已存在，不自动下载；PSM 6 用于文本块，7 用于单行。

独立 `tesseract-runtime/` 将 unsafe 限制在最小 C API 边界，通过 `libloading` 加载指定库，验证主版本 5 与所需导出符号；不使用 leptess、构建期链接或头文件生成绑定。直接传入 RGB8，原生接口复制像素，不经过 TIFF 编解码。每次识别先清空上一幅图像，关闭自适应和文档词典学习，输出 UTF-8 全文和 TSV 单词框。原生字符串由同一库的 `TessDeleteText` 释放，引擎先销毁再卸载库，实例保持线程局部。

运行时仍需完整的 Tesseract 及其 Leptonica 等传递依赖；本层不直接调用 Leptonica。Windows 指向 `libtesseract-5.dll` 等实际 DLL，依赖放在同目录或系统搜索目录；macOS 为 `libtesseract.5.dylib`，Linux 为 `libtesseract.so.5`，名称以发行包为准。显式路径失败会返回错误，不搜索其他库。所有 OCR feature 的构建都不需要安装 Tesseract SDK。

## PP-OCR / OpenCV 5 DNN

提供识别 ONNX 和 UTF-8 字典；`PpOcrConfig::new(recognition_model, dictionary)` 创建配置，`opencv_dir`／CLI `--opencv-dir` 可指定包含桥接与 OpenCV 动态库的目录。省略时采用与模板匹配相同的环境／部署／工作区路径规则。旧 `runtime_library`／`--ort-library` 已移除。当前仅支持调用方已定位的文字行／ROI。设置 `detection_model`／`--det-model` 会在加载资产和运行库前返回 `BackendNotImplemented("ppocr-detection-opencv5")`，不会降级到单行模式。DB 参数暂时保留在配置中，不参与单行识别。

输入是单个 float32 NCHW 张量，batch=1、三通道；识别输出必须为 `[1,T,C]` 的 0..1 概率，不接受 logits 或其他布局。使用 Rust `image` 的 Triangle 滤波缩放，RGB 转 BGR，归一化到 [-1,1]，右侧归一化填充值为 0。动态高度默认 48、最大宽度默认 2048；固定尺寸以模型为准。动态宽度超出上限报错；固定宽度模型按其宽度适配。

字典每行一个 token，CTC blank 为 0，默认末尾追加空格，`append_space=false` 可关闭。输出 C 必须等于 token 数 + blank。解码移除相邻重复 token，保留 blank 分隔的重复字符；空结果不伪造置信度。

运行时使用 `easycon-opencv-runtime::dnn::Dnn` → 自有 C ABI 桥接 → `cv::dnn::readNetFromONNX(..., ENGINE_NEW)` → `setInput`／`forward`，明确使用 OpenCV 5 原生 CPU 新引擎。已移除 `ort`／`ort-sys` 和进程级 ORT 初始化，不自动回退到 ORT、旧引擎或其他后端。桥接需要 OpenCV `core`／`imgproc`／`dnn`，旧的仅匹配桥接需重建。Rust 构建不需原生 SDK。

Rust 读取 ONNX 字节后交给桥接，避免将平台文件路径交给 C++ 文件接口；模型与字典每个实例仅在打开时加载，多次识别复用。C++ `Net`、输入和输出存储由 native handle 持有，Rust 拷贝校验后的结果，销毁 handle 后才卸载库。实例不跨线程共享；每个实例从自身配置解析运行目录，不再依赖进程级推理运行库初始化。profile 标记 `ppocr-ctc-opencv-dnn-new-image-triangle/v3`、实际 OpenCV 版本、库路径和配置；路径不是内容摘要，不能充当 IR 缓存键。

## 使用与验证

在 `nextgen` 目录：

```sh
cargo run --locked --bin easycon-vision -- --help

# 两项测量无需 OpenCV、OCR 模型或硬件。
cargo run --locked --bin easycon-vision -- stats --image frame.png --roi 10,20,100,50
cargo run --locked --bin easycon-vision -- diff --left before.png --right after.png
# --left-roi 与 --right-roi 可分别指定尺寸一致的比较区域。

# Windows 首次运行先用官方 SDK 构建桥接；需 MSVC，详见 native/README.md。
python native/setup_windows.py --arch x64 --build
cargo run --locked --bin easycon-vision -- match \
  --image frame.png --template target.png --mode sq-diff-normed
# --opencv-dir /path/to/runtime 可覆盖默认来源，或设置 EASYCON_OPENCV_DIR。

cargo run --locked --features ocr-tesseract --bin easycon-vision -- ocr \
  --backend tesseract --image frame.png --tesseract-library /path/to/libtesseract \
  --tessdata /path/to/tessdata --language eng --psm 6

cargo run --locked --features ocr-ppocr --bin easycon-vision -- ocr \
  --backend ppocr --image frame.png --opencv-dir /path/to/opencv-runtime \
  --rec-model /path/to/rec.onnx --dictionary /path/to/dict.txt --roi 100,200,300,48
```

路径应指向真实文件，运行库扩展名取决于平台。CLI 支持 PNG、JPEG、PNM、TIFF，只读取本地文件，不打开硬件。

宿主通过接口调用，测试可替换 `matcher`：

```rust
use easycon_sense::vision::{Image, MatchConfig, Result, TemplateMatcher};
use tokio_util::sync::CancellationToken;

fn observe(matcher: &mut dyn TemplateMatcher, image: &Image, template: &Image,
           token: &CancellationToken) -> Result<()> {
    let result = matcher.match_template(image, template, MatchConfig::default(), token)?;
    println!("{:?} {:?}", result.bounds, result.confidence);
    Ok(())
}
```

```sh
cargo test --locked --workspace --all-targets
cargo test --locked --workspace --all-targets --all-features
cargo clippy --locked --workspace --all-targets --all-features -- -D warnings
cargo fmt --all --check
cargo test --locked --lib real_opencv -- --ignored

# 真实 OCR 测试默认 ignored，显式提供资产后运行；游戏截图另需对应目录。
EASYCON_TESSERACT_LIBRARY=/path/to/libtesseract \
EASYCON_TESSDATA=/path/to/tessdata \
EASYCON_OPENCV_DIR=/path/to/opencv-runtime \
EASYCON_PPOCR_REC=/path/to/rec.onnx \
EASYCON_PPOCR_DICT=/path/to/dict.txt \
EASYCON_OCR_GAME_ASSETS=/path/to/ppocr \
cargo test --locked --all-features --lib vision::tests::native::real_ -- --ignored --nocapture
```

固定 PNG 位于 `tests/fixtures/`，测试和构建不依赖 OpenCV 生成图像。2026-10-24 已在 macOS arm64 使用本地现成 `PP-OCRv6_medium_rec.onnx`／配套字典及动态 Tesseract 5.5.3，执行 5 项真实 OCR 测试；模型身份、结果、复现命令和边界见 [验收记录](ACCEPTANCE.md)。这些回归不构成广泛精度基准。PP-OCR 不在本机转换模型，直接加载用户提供的 ONNX。

取消在调用前后及 PP-OCR 预处理边界检查。OpenCV DNN 的同步 `forward` 与模板匹配、Tesseract 原生调用均不可由本层强制打断；取消可能等待本次原生计算结束。取消后的结果不会作为成功输出，同一个 DNN 实例仍可继续识别。异步宿主优先通过 NativeMatcher／NativeOcr 托管后端；直接调用同步实现时须自行管理阻塞工作和收尾。CLI 在 Ctrl+C 后等待收尾。

工作流项目宿主使用一个 `ocr` 原语，显式 kind 为 Tesseract／PpOcr，同一流程可以同时使用两种实例。image 接受 Frame 或 Image，直接传入采集帧时保留帧身份、时间和尺寸，不重采集、不复制 RGB。模型及字典随工作流项目放在 `models/`，由 workflow.json 分别记录相对路径，详见 [项目宿主约定](../project/README.md)。底层 OcrBackend trait 复用结果及线程适配，kind 与已绑定实例不符时在调用前报错，不隐式切换实现。
