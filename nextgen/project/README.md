# 工作流运行宿主

`easycon-run` 装配视频采集、OpenCV 模板匹配、串口手柄和 Meta，执行项目提供的 IR。识别阈值、循环、按下／等待／抬起都放在工作流中。Tesseract 和 PP-OCR 通过两个独立节点装配；音频和文本端口目前仍通过 Rust `RunBindings` 装配。

## 项目约定

```text
项目目录/
    workflow.json           # 主入口，普通 IR Document
    config.json             # 与 workflow 同目录，仅设备选择；无设备流程可省略
    imglabel/
        target.IL           # 旧 EasyCon 标签，内嵌 ImgBase64
    models/                 # 随此工作流项目发布的模型和 OCR 字典
        ppocr.onnx
        ppocr.txt
        eng.traineddata
    resources/
        ...                 # 其他独立资源和配置；.il 内容不拆分
```

`config.json`：

```json
{"video":"index:0","gamepad":"COM3"}
```

视频仅使用 ccap；ID 来自其枚举，优先使用 `id:<完整设备名称>`，也兼容当前枚举中的 `index:N`。宿主打开设备默认流，从首张实帧确定尺寸和编码并输出 RGB8；`frames_per_second=0` 表示未知。开始采集时打印最终格式，会话内格式变化会报错。串口只填端口名。两项都可省略；工作流入口声明了相应来源时必须配置。配置不包含模板后端、阈值、模型或普通工作流参数。

从 `nextgen` 执行：

```sh
# 只预检工作流、相邻 config 和标签资源；不加载原生库或打开设备。
cargo run --locked --bin easycon-run -- examples/capture-match/workflow.json --check

# 修改 config 的设备选择和目标标签后运行。
cargo run --locked --bin easycon-run -- examples/capture-match/workflow.json

# 部署后在项目目录运行；默认读取 ./workflow.json。
easycon-run
# 也可传项目目录，或任意命名的工作流文件。
easycon-run /path/to/project --check
```

项目目录始终为工作流文件所在目录。入口的 `Source(Video)`、`Source(Gamepad)`、`Source(TemplateMatcher)` 参数按类型自动绑定为 `video`、`gamepad`、`opencv`，参数名可以不同。识图标签以入口 Record 参数名查找 `.il` 文件主体；阈值、时长等普通值直接用 IR 表达式定义。通用 Rust 执行 API 的参数能力不受此 CLI 约定限制。

## 自动加载 OpenCV

运行配置无需原生库路径。部署包结构为：

```text
easycon-run.exe
native/win-x64/
    easycon_opencv.dll
    opencv_world*.dll
    licenses/...
```

其他平台使用相应 RID 和动态库文件名。宿主调用 `NativeMatcher::open_opencv(None)`，沿用 [原生库加载规则](../native/README.md)：显式环境覆盖、可执行文件旁 `native/<rid>/`、开发目录 `native/runtimes/<rid>/`。普通部署直接使用随应用打包的库；保留 `EASYCON_OPENCV_DIR` 作为用户替换入口，指定错误的覆盖路径会明确失败。

准备完整原生运行包后可一并打包宿主：

```sh
cargo build --locked --release --all-features --bin easycon-run
python3 native/package.py --executable target/release/easycon-run --output dist/easycon-run
```

Windows 将可执行文件改为 `target/release/easycon-run.exe`。脚本复制整个依赖目录及许可证，拒绝覆盖现有输出；它不下载或编译 OpenCV。Windows x64 CI 从官方 SDK 准备原生库后生成此宿主包。macOS／Linux 目前仍使用自备 OpenCV >= 5 SDK；不能将本地测试 SDK 视为项目官方分发资产。

## 串口自动探查

配置只接收串口名，例如 `COM3`、`/dev/cu.usbmodem...` 或 `/dev/ttyACM0`。按 EasyCon 115200 → EasyCon 9600 → Pokeldn 921600 顺序握手，每次失败关闭连接再打开；成功握手并完成中立报告后才向 IR 提供手柄。取消、已建立会话的写入错误、释放未确认均不允许继续探查。全部失败保留两个协议及其各波特率的类型化错误。

枚举不会打开端口。单独探查可以使用 `easycon-pad probe --port <串口名>`；诊断工具仍可显式指定 `--backend` 和 `--baud`。

## 工作流模型与通用 OCR 原语

模型属于每套工作流项目，与 `workflow.json` 一同发布；路径相对工作流所在目录，以 `models/` 开头，可以使用子目录。缺失、空文件、绝对路径、目录越界和逃出项目的符号链接在 `--check` 期间报错；预检不加载原生库或解释 ONNX。运行时不下载、转换或选择其他模型。

工作流顶层分别声明 `tesseract` 和 `ppocr` 模型实例，执行节点使用统一的 `ocr` 原语和显式 `kind`：

```json
{
  "ir_version": "0.1",
  "entry": "main",
  "tesseract": {
    "tess": {
      "models": ["models/eng.traineddata", "models/chi_sim.traineddata"],
      "page_segmentation": 7
    }
  },
  "ppocr": {
    "paddle": {
      "model": "models/ppocr.onnx",
      "dictionary": "models/ppocr.txt",
      "recognition_height": 48,
      "max_recognition_width": 2048,
      "append_space": true
    }
  },
  "definitions": {}
}
```

以上展示声明部分，完整可执行流程见 [双 OCR 示例](../examples/ocr/workflow.json)。入口参数 `tess`、`paddle` 都使用 `Source(Ocr)`，按名称绑定到独立实例；所有实例名在项目中唯一。两个节点的 name 都为 `ocr`，参数 `kind` 是 `Symbol(OcrKind)`，可取 `Tesseract` 或 `PpOcr`；engine 指定对应实例，kind 与实例不符会在原生调用前报错。两者可在同一流程中组合、重复使用，分别持有模型、状态和托管工作线程。

`ocr(kind, engine, image, region)` 的 image 接受 `capture` 的 Frame 输出，也接受已有 Image；region 是可选的局部 ROI。结果统一为 `{kind, source, region, text, confidence, boxes, profile}`。boxes 中每项包含 text、可选 confidence 和原图坐标四点 polygon；顶层 confidence 是全部 box 置信度的算术均值，空结果或任一 box 缺少分数时为 None。Tesseract 提供单词框，当前 PP-OCR 提供单行输入 ROI；没有文字检测时不能把它视为检测框。

PP-OCR 参数默认值如上，模型和配套字典必填；当前仍为单行识别。Tesseract 模型文件名决定语言代码，多个模型必须位于同一目录，按声明顺序形成 `eng+chi_sim`；PSM 默认 7，支持 3..=13。这些模型声明属于项目宿主格式，核心 IR Document 仍只包含版本、入口和定义；带声明的完整文件由 `easycon-run` 执行。

使用 OCR 的宿主需构建相应 feature，正式通用宿主使用 `cargo build --locked --release --all-features --bin easycon-run`。PP-OCR 自动加载应用旁 OpenCV DNN。Tesseract 使用 `native/<rid>/libtesseract-5.dll`（Windows）、`libtesseract.5.dylib`（macOS）或 `libtesseract.so.5`（Linux），并需配套原生依赖；开发目录为 `native/runtimes/<rid>/`，可通过 `EASYCON_TESSERACT_LIBRARY` 指定显式替换。模型目录与原生动态库目录分别承担数据和平台运行库职责。

完整发布可带上项目目录：

```sh
python3 native/package.py --executable target/release/easycon-run --project examples/ocr --output dist/easycon-ocr
# 输出的 project/ 包含 workflow.json、imglabel/、models/、resources/ 等项目内容。
dist/easycon-ocr/easycon-run dist/easycon-ocr/project
```

打包前把实际模型放进示例 `models/`，运行 `easycon-run examples/ocr --check`。打包复制文件及目录结构，不改写工作流内的相对路径，不依赖构建机器的模型绝对路径。示例的大模型不提交到源码库，发布时复制项目目录中的实际文件。

## 旧 EasyCon .IL 兼容

当工作流包含 `match_template`、`ocr`、`region_statistics` 或 `image_difference` 时，宿主扫描 `imglabel/` 当前层所有以 `.il` 结尾的文件，扩展名大小写不敏感，文件主体精确匹配。也接受此前约定的 `ImgLabel/` 名称；在区分大小写的文件系统中，两者指向不同目录会报歧义。没有识图原语的流程不扫描标签。检测范围包括所有子流程定义，动态调用也能在运行前准备资产。

扫描解析全部标签 JSON，因此损坏 JSON 或重复标签名会预检失败；只对入口实际引用的标签解码内嵌图片和检查算法。未使用的 OCR／边缘检测标签可以共存。

旧格式可以直接使用，无需解包：

```json
{
  "searchMethod": 5,
  "ImgBase64": "<原有 PNG 图片的 Base64>",
  "RangeX": 100,
  "RangeY": 80,
  "RangeWidth": 640,
  "RangeHeight": 360,
  "TargetX": 0,
  "TargetY": 0,
  "TargetWidth": 100,
  "TargetHeight": 100,
  "UseGrayscale": false,
  "UseBinary": false,
  "UseGaussianBlur": false,
  "UseOther": false
}
```

`searchMethod` 缺省为旧实现的 `5`；支持 `0=SqDiff`、`1=SqDiffNormed`、`2=CCorr`、`3=CCorrNormed`、`4=CCoeff`、`5=CCoeffNormed`。其他旧算法在标签被引用时明确报错，不替换为普通模板匹配。`Target*` 和 `Use*` 接受为旧编辑／预处理元数据，与旧运行实现一致，不再次裁剪或处理已存图片。图片完整解码为 RGB8，不启用透明通道掩码。接受 UTF-8 BOM 和 Base64 中的 ASCII 空白。

`Range*` 是输入帧像素坐标；四项全零约定为整帧，否则必须构成非空区域且能容纳模板。实际帧上的区域越界由原语校验。旧 `.IL` 不包含阈值；阈值放在工作流中。

`.il` 始终保持上述旧格式，不支持 `{ "image": "...", "config": "..." }` 引用形式，也不要求额外图片或侧车 JSON。其他独立资源和配置统一放到项目 `resources/`，不改变 `.il` 的自包含内容。宿主只读取标签，不改写或解包。

标签转换为 `{ image: Image, config: { mode, region } }` 入口值。`match_template` 返回完整结构，工作流自己选择 `raw_score` 或 `confidence`。归一化模式的 confidence ∈ [0,1]：模式 1 为 `1 - raw_score`，模式 3 为裁剪后的 raw_score，模式 5 为裁剪后的 `(raw_score + 1) / 2`。非归一化模式 0／2／4 的 confidence 为 None，工作流应比较 raw_score。这些分数没有旧脚本的百分数取整，也不代表概率。

## 采集／匹配／按 A 示例

[workflow.json](../examples/capture-match/workflow.json) 每轮携带上一帧的 SampleId，读取严格更新的帧，匹配 `target`；低于 0.9 时按下 A → 等待 80ms → 抬起 A → 等待画面 300ms，达到阈值（含相等）结束。最多 1000 轮，总时限 60000ms。这些值都在工作流中。示例标签保持旧 `.IL` 格式并使用 SqDiffNormed，内嵌图片来自已有文字测试图，请替换为实际目标宝可梦模板。

宿主在外层运行时持有并等待阻塞工作线程；串口与视频会话的打开／关闭在该线程执行，OpenCV 初始化／计算／销毁由 NativeMatcher 的专用线程管理。Ctrl+C 在外层保持可响应，发出取消后等待 IR 中立复位、物理断开、视频停止和匹配线程回收。执行结果与宿主清理错误分别保留，任一清理失败均使 CLI 返回失败。

同步串口写入和 ccap 原生读帧不能被强制抢占；串口调用可能推迟宿主内部定时器调度，已开始的写入需等实际结果，视频驱动阻塞时关闭也可能延迟。不要把 IR Timeout 理解成强杀原生代码的硬时限。
