# 真实 OCR 回归验收

2026-10-24，macOS arm64，本地 CPU。范围为 `nextgen/` 已实现的单次 OCR 原语，使用真实 Tesseract 动态库和本地现成的 PP-OCR v6 ONNX。PP-OCR 现由 OpenCV 5 原生 DNN 新引擎执行，已移除 Rust `ort`／`ort-sys` 依赖。未使用 mock 替代识别结果，未转换或重新下载 PP-OCR 模型。

## 使用的资产

PP-OCR 资产直接来自用户指定的 `test/assets/ppocr/`；该目录不由 `nextgen` 构建或转换。按配套 YAML 的 `Global.model_name` 确认为 `PP-OCRv6_medium_rec`，字典 18,708 项与 YAML 的 `PostProcess.character_dict` 逐项一致，末尾追加空格并预留 CTC blank，输出类别数为 18,710。ONNX 输入为 float32 `[N,3,48,W]`，输出 `[N,T,18710]`；ONNX checker 通过。原始下载来源未独立核验，以本地文件摘要标识本次资产。

| 资产 | SHA-256 |
|---|---|
| `PP-OCRv6_medium_rec.onnx` | `9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba` |
| `PP-OCRv6_medium_rec.yml` | `991b700facf5b50a7de193468207d5f4255b538dde0d312ae3b7c7a9b6873129` |
| `ppocrv6_dict.txt` | `b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d` |
| Tesseract `eng.traineddata` | `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2` |
| Tesseract `chi_sim.traineddata` | `a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730` |

Tesseract **5.5.3**／Leptonica **1.87.0** 来自本机 Homebrew；英文数据复制自 `/opt/homebrew/share/tessdata/eng.traineddata`，中文数据来自 tessdata_fast 4.1.0（通过 `https://cdn.jsdelivr.net/gh/tesseract-ocr/tessdata_fast@4.1.0/chi_sim.traineddata` 获取）。数据准备在验收外显式完成，后端不会联网。数据位于忽略跟踪的 `nextgen/.deps/tessdata/`。

OpenCV **5.0.0** 使用本机自备 Homebrew SDK：`/opt/homebrew/opt/opencv/lib/cmake/opencv5`，只编译项目自有桥接，不编译 OpenCV。桥接链接 `core`／`imgproc`／`dnn`，由 `native/build.py` 连同传递依赖复制到 `.deps/opencv-dnn-runtime/`，修正相对加载路径并签名；此本地验收包不作为项目官方 Release 分发。Tesseract 动态库 `/opt/homebrew/lib/libtesseract.5.dylib` 的摘要为 `27d331cd2d03bc403e76bded0fa62ebf472b72a373a2150e31fa1e1841e8ca1e`。

加载链为 `PpOcr` → Rust `Dnn` → 自有 C ABI → `cv::dnn::readNetFromONNX(bytes, length, ENGINE_NEW)` → `setInput`／`forward`，显式选用原生新引擎，无 ORT 回退。ONNX 字节加载避免原生库直接解释模型路径。模型对象由桥接持有并释放，输出复制为 Rust 所有的 float32 张量；归一化、BGR CHW 排列和 CTC 解码继续由现有 Rust 代码执行。

## 验收结果

5 项真实测试通过：Tesseract 基础识别、Tesseract 回归集、PP-OCR 基础识别、PP-OCR 回归集和 PP-OCR 游戏截图 ROI。固定回归集只忽略文本中的空白，其余字符、数字与大小写必须一致。

| 输入 | PP-OCR v6 | Tesseract eng+chi_sim |
|---|---|---|
| `HELLO 123` | 通过 | 通过（eng，PSM 7） |
| `EasyCon 2026` | 通过 | 通过 |
| `你好世界123` | 通过 | 通过 |
| `自动化测试 EasyCon 123` | 通过 | 通过 |
| 空白图像 | 空文本、无分段 | PSM 6 下空文本、无分段 |

Tesseract 会在中文词之间插入空格，验收保留原输出，仅比较时去掉空白。PSM 7 的基础测试不强制空白图像结果为空，而是对照新建引擎，检查上一张图像的结果是否残留。

PP-OCR 使用真实游戏截图中的单行 ROI，不运行文字检测，也不添加阈值、二值化或超分辨率预处理：

| 文件 | ROI：x,y,width,height | 期望／实际文本 |
|---|---|---|
| `ppocr3.png` | `1260,234,330,81` | `TAUROS` |
| `ppocr2.png` | `640,847,490,83` | `CHLOROPHYLL` |
| `ppocr.png` | `1130,625,610,75` | `がくしゅうそうち` |

这些截图的 SHA-256 分别为 `c3887a71147b2366ae8acb5c6c5fa6891bd5df647c98f3a1875a92c3b504cfbb`、`a4eedc5c4719468f6603276d61b0a8c6813b988a8d9d8b25f951ed429e7a7414`、`e5f04350a6a844f6c171b2e681df3b43c95d2a1796f14d9932c624500986bb73`。原图保留在用户指定目录，测试不复制整张图片进入 Rust 工作区。

额外断言覆盖：0..1 置信度、区域与分段坐标、嵌套 crop 的原图偏移、帧来源保留、非法 ROI、入口取消、取消后重用、空白后再次识别。PP-OCR 还验证请求开始后延迟取消及同一 session 重用、空／非法 ONNX 经 C ABI 返回错误，以及字典／空格配置不匹配时返回 `InvalidOutput`。OpenCV `forward` 为同步调用，执行期间收到取消会在返回后丢弃结果并返回 `Cancelled`；延迟取消检查不构成中断原生计算或硬实时保证。

PP-OCR CLI 使用新 `--opencv-dir` 配置识别游戏 ROI，Tesseract CLI 继续通过 `--tesseract-library` 识别混合文本。profile 升为 `ppocr-ctc-opencv-dnn-new-image-triangle/v3`，包含 OpenCV 版本与桥接路径，区分此前推理后端。Rust 产物通过 `libloading` 在运行时加载桥接和 Tesseract；`otool -L` 检查启动依赖不含 OpenCV／ORT／Tesseract／Leptonica。

使用同一 OpenCV DNN 运行包执行原有模板匹配真实回归，2 项通过：六种匹配模式、原图坐标与来源保留、低相似度成功返回测量值。新增 DNN 接口未改变匹配 ABI 1；旧的仅匹配桥接需要重建后才能执行 PP-OCR。

## 原生工作托管验收

同日追加 `tests/native_work.rs` 的真实集成测试，1 项通过。由宿主配置创建独立 NativeMatcher、Tesseract NativeOcr 和 PP-OCR NativeOcr，初始化、重复调用和销毁均经过专用工作线程。使用同一混合文本图像，每种 OCR 连续识别两次，去掉空白后均为 `自动化测试EasyCon123`；同时直接并发调用匹配线程，原图自身匹配 confidence > 0.999。最后显式等待每个提供者 shutdown。

模型／字典路径由本次测试的环境配置传入，核心没有默认模型、下载或转换步骤。该测试验证真实引擎可托管、复用和关闭，不作为吞吐量或取消时延基准。IR 的 Timeout／Race 取消收拢、线程 panic 致命失败和有界队列由无模型测试覆盖；单次 IR 运行中的提供者调用仍串行。

使用下节相同环境变量执行：

```sh
cargo test --locked --offline --all-features --test native_work real_native_workers -- --ignored --nocapture
```

验收日志为 `.deps/native-work-real.log`；托管接口及所有权见 [使用说明](../native-work/README.md)。

## 复现

在 `nextgen/` 执行；先提供上述 SDK 与 traineddata，路径按环境调整。仅编译桥接并准备运行目录，直接使用本地 ONNX，无模型转换步骤：

```sh
python3 native/build.py \
  --opencv-dir /opt/homebrew/opt/opencv/lib/cmake/opencv5 \
  --library-dir /opt/homebrew/opt/opencv/lib \
  --output .deps/opencv-dnn-runtime

export EASYCON_OPENCV_DIR="$PWD/.deps/opencv-dnn-runtime"
export EASYCON_TESSERACT_LIBRARY=/opt/homebrew/lib/libtesseract.5.dylib
export EASYCON_TESSDATA="$PWD/.deps/tessdata"
export EASYCON_PPOCR_REC="$PWD/../test/assets/ppocr/PP-OCRv6_medium_rec.onnx"
export EASYCON_PPOCR_DICT="$PWD/../test/assets/ppocr/ppocrv6_dict.txt"
export EASYCON_OCR_GAME_ASSETS="$PWD/../test/assets/ppocr"

cargo test --locked --all-features --lib vision::tests::native::real_ -- --ignored --nocapture
cargo test --locked --lib real_opencv -- --ignored --nocapture

cargo run --locked --all-features --bin easycon-vision -- ocr \
  --backend ppocr --image "$EASYCON_OCR_GAME_ASSETS/ppocr3.png" \
  --opencv-dir "$EASYCON_OPENCV_DIR" --rec-model "$EASYCON_PPOCR_REC" \
  --dictionary "$EASYCON_PPOCR_DICT" --roi 1260,234,330,81

cargo run --locked --all-features --bin easycon-vision -- ocr \
  --backend tesseract --image tests/fixtures/ocr-mixed.png \
  --tesseract-library "$EASYCON_TESSERACT_LIBRARY" \
  --tessdata "$EASYCON_TESSDATA" --language eng+chi_sim --psm 6
```

本轮验收日志位于本机忽略跟踪的 `.deps/ocr-dnn-regression.log`、`.deps/opencv-dnn-matching.log` 与 `.deps/ocr-dnn-build.log`，测试定义位于 `src/vision/tests.rs`。配置迁移为 `PpOcrConfig::new(recognition_model, dictionary)` 加可选 `opencv_dir`，不再接受旧 `runtime_library`／`--ort-library`／`EASYCON_ORT_LIBRARY`。CI 继续执行无模型测试，Linux 另装 Tesseract 运行库与中英文数据执行真实回归；Windows 桥接构建现要求 SDK 含 `dnn`，本地 PP-OCR 模型没有被加入 CI 下载或转换流程。

## 边界

PP-OCR `medium_det` 文件存在，但检测／整屏多行管线仍按此前范围暂缓，本次未调用该模型。当前 PP-OCR 返回所选文字行的 ROI，而非字级定位。测试不代表完整 PaddleOCR 管线、广泛精度基准或性能基准；没有为整张截图推断业务阈值。Windows／Linux 的真实 OCR 与远端 CI 尚未在本机验收。PP-OCR DNN 和 Tesseract 的同步原生计算均只能在调用前后检查取消；组合子的超时清理可能需要等待原生调用返回。

## 工作流项目模型与两个 OCR 节点（2026-10-24）

新增 `project::tests::real_project_workflow_runs_tesseract_and_ppocr_together`，显式运行通过。`examples/ocr/workflow.json` 分别声明 Tesseract／PP-OCR 实例，并经统一的 `ocr` 原语执行，两个节点的 kind 分别为 Tesseract／PpOcr；同时读取项目 models/ 的 eng／chi_sim traineddata 及 PP-OCRv6_medium_rec ONNX／对应字典，去除空白后两者都为 `自动化测试EasyCon123`。模型路径相对 workflow.json，不依赖进程工作目录。

本机 `native/package.py --project examples/ocr` 已复制完整工作流和约 83 MB 模型／字典，四个文件复制前后的 SHA-256 一致。发布包从 `/tmp` 运行成功，OpenCV 从应用旁自动加载；Tesseract 本次通过 `EASYCON_TESSERACT_LIBRARY` 指定本机库，其跨平台原生依赖打包仍单独验收。日志为 `.deps/project-models-real.log` 和 `.deps/project-models-package.log`。模型载荷只在工作流项目和发布包中提供，不作为源码库固定资产。


## 通用 OCR kind 与帧输入回归（2026-10-24）

工作流只保留 `ocr` 原语，kind 为类型化 Tesseract／PpOcr，两个实例仍独立持有工作流模型与原生工作线程。输入接受 Frame 或 Image；帧适配共享 RGB 并保留来源、时间、尺寸。输出改为 kind／source／region／text／confidence／boxes／profile；每个 box 保留文字、可选分数和原图坐标 polygon。顶层 confidence 为所有 box 分数均值，空或任一未知时为 None。Tesseract 提供单词框，PP-OCR 的 box 仍是单行 ROI，尚未新增文字检测。

默认工作区 208 项、全部 feature 209 项测试通过，另有 4 项 API 文档示例编译。新增 4 项契约回归覆盖直接采集帧 Pipe、类型约束、像素共享／错误形状和汇总置信度。严格 Clippy 与格式检查通过。

显式运行真实工作线程与项目双 OCR 两项验收均通过。工作线程测试现经 `PrimitiveCall::Ocr` 接受混合文字 PNG 构造的帧，两种 kind 各调用两次，检查归一化文本 `自动化测试EasyCon123`、帧身份、非空 boxes、有限置信度及 shutdown。项目验收使用 examples/ocr/models 中现有 PP-OCRv6_medium_rec ONNX／字典和 eng／chi_sim traineddata。原生依赖仍为本机 OpenCV 5 自备运行包与动态 Tesseract 5.5.3。日志位于 `.deps/ocr-kind-{default,all,worker-real,project-real,clippy,doc}.log`。

本轮没有连接真实采集卡，也未执行 Windows／Linux 原生验收；不把 PNG 构造帧的验证视为采集卡硬件验收。
