# EasyCon Rust 基础核心

状态：基础原子能力与运行作用域实现中，2026-10-24。独立 Cargo 工作区，库与无界面诊断 CLI 共用核心。

当前实现采集卡／摄像头视频输入（官方 ccap-rs 1.6.0，唯一视频后端）、音频输入设备（CPAL 0.18.2），以及可选的 [图像感知原子能力](vision/README.md)：区域 RGB 统计、显式双图差异、模板匹配／OCR 接口、Tesseract OCR、OpenCV 5 原生 DNN 的 PP-OCR 单行识别。模板匹配和 PP-OCR 复用自有动态桥接，OpenCV 最低主版本为 5，允许升级；PP-OCR 文字检测仍暂缓。桌面／窗口采集、输出设备回环采集不在当前实现内。与现有 .NET 项目没有调用或构建依赖。

新增 [Vue 3 设备控制台](web/README.md)：选择并预检工作流，显示真实本地路径；独立枚举／连接／断开手柄串口、视频源和音频输入，显示实时视频预览及彩色音频强度柱。`web-host/` 是回环地址上的独立 Rust 宿主，复用原生资源托管，不修改核心原语或 IR 语义。

动作侧提供独立的 [通用设备原子模块](device/README.md)：上层只依赖 `easycon-device`，统一定义枚举、连接、手柄状态／报告和断开语义；EasyCon 和 LDN Pokémon 作为内置后端，自动探查也由 device 提供。原有 gamepad／pokeldn 独立 crate 已合并。`easycon-pad` 保留离线编码和诊断入口，长按／业务时序仍由编排层表达。

原子调用已汇总为 [18 项类型化原语](primitives/README.md)：版本化目录、效应、调用／输出类型、资源所有权、完成阶段与错误分类均可查询；能力由宿主显式借用，支持 mock，并校验媒体和识别提供者返回的结果。`TypeText` 仅提供可注入端口，无默认文本输入后端。运行宿主可使用 [RunScope](run/README.md) 管理专用订阅、手柄复位、清理报告和致命错误阻断。

已加入 [编排 IR 与组合子](ir/README.md)：版本化 JSON、类型化 Value／Expr、静态校验、全部核心组合子、流程引用、18 项原语适配和自动节点 trace，并通过 RunScope 收尾。可运行 `cargo run --locked --bin easycon-ir -- run examples/composition.ir.json` 验证。另有 `examples/combinators.ir.json` 演示子流程／循环／恢复／超时与并行，结果同为 Int(42)。新增宿主 RunControl 停止原因、组合子取消传播，以及 Expr 样本时间／格式／来源投影。前端来源映射仍待扩展。模型／模板由上层工作流选择并经宿主装配，核心不要求资产注册表。

新增 [原生工作托管](native-work/README.md)：模板匹配与 OCR 可在专用线程中初始化、复用和销毁；异步调用让出调度线程，取消后等待实际工作返回，宿主显式 `shutdown().await` 收尾。同步接口继续可用，不增加依赖。

新增 [项目运行宿主](project/README.md)：`easycon-run [workflow.json]` 自动读取同路径 `config.json`，设备配置仅需 `{"video":"index:0","gamepad":"COM3"}`。OpenCV 随应用分发并自动加载，串口自动探查 EasyCon／Pokeldn；识图流程扫描 `imglabel/*.il`，保持旧 EasyCon 标签的 Base64 图片、ROI 和搜索方法字段，其他独立资源和配置放在 `resources/`。模型与配套字典放在工作流项目的 `models/`，工作流分别声明 Tesseract／PP-OCR 实例并记录相对路径，统一由 ocr 原语按 kind 调用，两者可同时使用；发布工具可携带完整项目目录。提供采集→匹配→按 A 和双 OCR 示例，以及 `--check` 离线预检。

## 结构与边界

```text
诊断 CLI / 后续宿主
         │
         ├── video::VideoSession → 独立 VideoReader → Arc<VideoFrame>
         │          └── 专用线程 → VideoSource → ccap
         │
         └── audio::AudioSession → 独立 AudioReader → Arc<AudioWindow>
                    └── CPAL 输入回调 → 有界环形历史

共享契约：CaptureClock / SampleId / SampleTime / CaptureError
```

| 模块 | 职责 |
|---|---|
| `src/sample.rs` | 来源身份、进程内会话代次、序号和时钟 |
| `src/video.rs` | 设备／格式枚举、视频来源适配、后台采集与最新帧订阅 |
| `src/audio.rs` | 输入设备／格式枚举、样本转换、连续窗口订阅与纯 RMS 测量 |
| `src/error.rs` | 参数、格式、来源变化、缺口、取消和后端错误契约 |
| `src/vision.rs`、`src/vision/` | 不可变 RGB／ROI、可替换匹配接口与原生 OpenCV >= 5 模板匹配、Tesseract／PP-OCR 单行后端 |
| `src/vision/measure.rs` | 只读区域 RGB 统计、显式双图差异与来源／原图坐标保留 |
| `src/primitives.rs`、`src/primitives/` | 内建原语目录、类型化单次派发、可 mock 端口与跨后端错误分类 |
| `src/ir.rs`、`src/ir/` | JSON IR、表达式、类型／作用域校验、效应合并、组合子、流程引用、结构化调度和节点 trace |
| `src/bin/easycon-ir.rs` | 无硬件 IR 校验与无参执行诊断入口 |
| `src/project.rs`、`src/project/`、`src/bin/easycon-run.rs` | 项目资源解析、入口预检、视频／匹配／手柄装配、执行与物理资源清理 |
| `device/` | 通用手柄契约、串口会话、内置 EasyCon／Pokeldn 后端和自动探查 |
| `web/`、`web-host/` | Vue 3 设备控制台与独立 Axum 本地宿主，工作流文件选择、设备连接、视频预览和音频电平 |
| `src/run.rs`、`src/run/` | 运行专用订阅、独占手柄状态租约、清理报告、取消与致命结果裁决 |
| `src/native_work.rs`、`src/vision/worker.rs` | 有界原生工作线程、取消／join、匹配与 OCR 异步适配 |
| `src/meta.rs` | 可取消等待、运行内单调时钟、注入式结构化用户日志 |
| `opencv-runtime/`、`native/` | 受限 FFI 边界、自有 C ABI 桥接、原生 OpenCV 依赖与版本检查 |
| `tesseract-runtime/` | 受限 C API FFI 边界、显式加载 Tesseract 5 动态库与原生资源释放 |
| `src/main.rs` | 用户选源、超时等待、Ctrl+C、诊断输出与收尾 |
| `src/bin/easycon-vision.rs` | 离线区域统计、双图差异、图片匹配和显式 OCR 后端选择，不打开硬件 |

库不决定静音／画面命中阈值、业务循环、重试或音视频配对。识别直接消费显式图像；RMS 只读样本。内部持续采集循环属于设备会话生命周期。OCR 原生依赖按 feature 启用；OpenCV 原生依赖动态加载，具体契约与诊断命令见 [视觉模块文档](vision/README.md)。

## 构建与验证

需要 stable Rust、系统 SDK、C++17 编译工具链和 libclang（ccap 的 bindgen 构建依赖），`Cargo.lock` 固定依赖解析。命令均在本目录执行：

```sh
cargo build --locked
cargo fmt --all --check
cargo clippy --locked --workspace --all-targets -- -D warnings
cargo test --locked --workspace --all-targets
cargo run --locked -- --help
cargo run --locked -- primitives
```

- Windows：使用 MSVC Rust 工具链，安装 Visual Studio C++ Build Tools 与 Windows SDK。
- macOS：安装 Xcode Command Line Tools。CLI 链接时嵌入 `Info.plist` 中的相机／麦克风用途说明；其他宿主须提供自己的权限元数据。首次打开音频输入可能触发系统麦克风权限提示。
- Linux：V4L2 视频、ALSA 音频；Debian／Ubuntu 构建依赖如下，还需要当前用户具备相应设备访问权限：

```sh
sudo apt-get update
sudo apt-get install -y build-essential pkg-config libasound2-dev libudev-dev libclang-dev clang
```

ccap 使用官方发布包自带的原生源码构建，未打补丁；Rust 依赖树已移除 nokhwa 及其 `block 0.1.6` 传递依赖。Windows 还需安装 LLVM/libclang，必要时设置 `LIBCLANG_PATH` 指向 LLVM 的库目录。

`.github/workflows/nextgen.yml` 在 Windows、macOS、Linux 执行默认特性的格式、Clippy、无硬件测试及 CLI 冒烟，并检查全部 OCR feature 无模型构建。Linux OCR 任务安装 Tesseract 运行库与英／中文 traineddata 后执行真实回归。项目分发的原生 OpenCV 仅采用官方 Release 二进制，Windows x64 任务从 SDK 构建自有桥接、运行原生匹配测试并上传依赖包；该任务的目标平台执行结果以 CI 为准。macOS／Linux 使用自备 SDK，不随项目提供其他来源的包。OpenCV 和 Tesseract 均在运行时加载，Rust 构建不需这些后端的原生 SDK；PP-OCR 已移除 ONNX Runtime 依赖。本地自备 OpenCV 5 SDK、PP-OCR v6 与 Tesseract 验收见 [记录](vision/ACCEPTANCE.md)，其他平台的实际兼容性以目标 CI 和设备验收为准。

## 手动诊断

先枚举设备，使用输出中的完整 ID。视频与音频分别选源，不按名称推断属于同一采集卡。

```sh
cargo run --locked -- video-devices
cargo run --locked -- audio-devices
```

ccap 在打开设备时处理系统相机权限；其安全枚举 API 也会短暂打开设备，首次枚举／连接可能触发授权。macOS 宿主包含相机用途说明。拒绝后应在系统设置中修改权限。

查询格式，设备 ID 替换为实际枚举值：

```sh
cargo run --locked -- video-formats --device 'index:0'
cargo run --locked -- audio-formats --device '<audio-device-id>'
```

视频默认使用设备的实际默认流，不依靠属性报告值猜测尺寸；第一帧用于确认尺寸和编码并保留给订阅者。优先使用枚举返回的 `id:<完整设备名称>`；`index:N` 继续可用，按当前枚举顺序解析。按名称打开后核对实际设备，拒绝 ccap 的默认相机回退。

```sh
cargo run --locked -- video --device 'id:OBS Virtual Camera' --frames 10 --output frame.ppm
```

`video-formats` 短暂启动设备，只返回实帧确认的默认流格式，不构造未验证的分辨率／帧率组合。`frames_per_second=0` 表示未知（也包含当前整数元数据无法精确表示的分数帧率），不会用估算交付速率冒充设备帧率。

可同时指定 `--width`、`--height`、`--fps`、`--encoding` 约束默认流；`--fps 0` 不约束帧率。这些约束只校验默认流，不承诺切换设备模式；不符或请求了无法确认的 FPS 即报错，不静默降级。例如本机 OBS 可用 `--width 1920 --height 1080 --fps 0 --encoding BGRA32`。

视频命令输出帧序号、会话、主机接收时间、尺寸和字节数；可将最后一帧保存为 PPM P6 RGB 图片。已有文件不会被覆盖。

音频默认使用所选输入设备公布的默认格式，打印实际采样率、声道和原始样本格式。窗口按“采样帧”计数，4800 帧在 48 kHz 下为 100 ms；双声道窗口含 9600 个交错样本：

```sh
cargo run --locked -- audio --device '<audio-device-id>' --window-frames 4800 --windows 10 --history-frames 96000
```

也可按格式枚举结果同时指定三个格式参数：

```sh
cargo run --locked -- audio --device '<audio-device-id>' --sample-rate 48000 --channels 2 --sample-format i16
```

音频命令只报告窗口起点、时间戳、RMS 和回调丢帧计数，不写录音文件。只选择 CPAL 的输入端点，不将输出端点作为回环来源；系统安装的虚拟输入设备仍可能出现在枚举中。

两个采集命令的 `--timeout-ms` 默认 5000，约束每次等待样本的时间。超时、断连和 Ctrl+C 都进入资源收尾；它不限制设备打开或原生停止耗时。

## 样本与订阅契约

- `SampleId.source` 带后端前缀；每次打开获得新的进程内 `SessionId`。视频 `index:N` 仅在本次设备枚举中有效，拔插后重新枚举，不能当作稳定硬件身份。音频 ID 由 CPAL 提供，不保证拔插后不变。
- 视频序号从 1 开始。音频序号是窗口第一个采样帧的偏移，从 0 开始，与声道数无关。
- `CaptureClock` 可克隆给两个会话，共享单调主机时钟原点。`received_ns` 是主机接收／处理时间，视频在原生读取和解码之后记时；不是曝光时间。
- `source_ns` 属于本来源／会话的时钟。CPAL 使用首个输入捕获时间作原点；ccap 以首个非零源时间戳为原点，零时间戳视为不可用并返回 `None`；序号、有效时间戳回退或会话内格式变化会报错。音频窗口取首帧的源时间，主机时间仍是该帧所在回调的接收时间。不能据此宣称音视频硬件同步。
- 视频以紧密排列 RGB8 交付；`VideoFormat.encoding` 记录 ccap 交付的 RGB／BGR／RGBA／BGRA 编码，不代表采集卡原始传输编码；行对齐、方向和尾部存储在标准化时处理。音频转换为交错 `f32`，保留声道、不重采样、不裁剪浮点值；整数按 CPAL 规则归一化。非有限值和不完整声道帧报错。
- 视频只保留最新帧，允许跳过中间帧。`capture(None, token)` 返回当前最新帧，尚无帧则等待；传入 `after` 仅返回更新的帧。跨来源／会话游标返回 `SourceChanged`。
- 音频订阅从当前末尾开始，每个读者有独立游标。`read_window(n, token)` 等待精确 n 帧连续数据，成功后才推进游标；取消或丢弃等待 future 不消耗数据。
- 音频历史有界，默认 96,000 采样帧，超出历史的慢读者收到 `AudioGap`。该读者需要重新订阅，从当前末尾恢复，不会静默跳过缺口或补零。
- 取消一个读者只停止这次等待。物理来源由 Session 持有，Session 停止后全部读者收到终态；错误优先于缓存旧样本。没有自动重连，重新打开意味着新会话。

## 时间与日志原语

`meta::sleep(duration_ms, token)` 是可取消的单次等待，负数报错，零时长仍让出一次调度机会；不会创建后台任务。取消单次等待不自动释放手柄按键；使用 RunScope 时，运行退出负责全中立复位，运行内部的按下／等待／抬起仍由后续编排负责。

每次运行创建一个 `MetaContext::new(Arc<dyn LogSink>)`，克隆共享时钟原点与输出端；新建上下文获得新原点。`clock_now(token)` 返回从运行起点开始的单调毫秒数，属于 Meta，不是纯表达式函数。

`log(level, message, fields, token)` 交付带运行内时间戳的 `LogRecord`；字段支持 Null、Bool、Int、有限 Float、Str。`LogSink: Send + Sync` 由宿主注入，同步交付一次，失败向上传播，不自动重试。取消在交付前检查；交付成功后取消不会掩盖已完成的输出。Sink 应快速返回，本层不能抢占阻塞 Sink。

这三项均属于 Meta 能力，也可通过 `PrimitiveHost` 调用。用户日志独立于节点 trace；使用 ir 执行器时自动产生节点事件，不要求原语实现报告节点状态。

## 生命周期与当前限制

`ccap-rs` 的独立硬件试验见 [验收工具与记录](tools/ccap-probe/README.md)。该工具拥有独立锁文件；真实回归现通过生产 `VideoSession::open_default` 运行。CLI、工作流宿主和 Web 均仅使用 ccap，没有后端选择开关。

显式调用 `stop()` 收尾；`Drop` 也会停止来源。视频工作线程被 join，音频流被释放，不留下由本库分离的工作。视频支持通过 `VideoSource` 工厂注入合成来源；测试在进程内模拟视频和音频输入。

视频 `open`、`stop` 和 `Drop` 是阻塞操作，异步宿主应放到自己管理并等待结束的阻塞任务中。ccap 单次原生取帧超时为 1000 ms，超时按错误传播；取消在读帧前后检查。打开、停止及驱动阻塞仍不能强制抢占，不承诺硬停止期限，Rust panic 捕获也不等于原生崩溃隔离。macOS 尚未授权时，ccap 的权限请求依赖主线程事件循环；首次授权路径仍需单独验收。

音频回调预分配历史缓冲，成功路径不分配样本内存、只尝试加锁。读者复制窗口期间发生锁争用会丢弃整次回调，增加丢帧统计并让现有读者收到缺口错误；即使旧历史尚在，也采用保守失效策略。大窗口／高并发会增加争用，后续若真实硬件测量显示需要，应将入口替换为预分配 SPSC 队列。当前不做音频拼接修复、跨设备同步和延迟校准。

当前测试验证数据与生命周期契约，尚未完成真实采集卡的视频／音频连续采集、拔插和各平台权限验收。原语目录与类型化派发已实现；运行作用域清理已提供独立 API；IR、Expr／类型／作用域校验、流程引用、全部核心组合子及子作用域清理已实现；后续仍需完成设备验收、前端映射与 trace 持久化契约。
