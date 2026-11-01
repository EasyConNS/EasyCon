# 类型化原语入口

`easycon_sense::primitives` 将设计规范 §6.2 的 18 项内建原语接到已有能力库。它提供版本化目录、Rust 类型化调用／输出、可注入能力端口和统一错误分类；原语不执行 Expr、不维护绑定或业务循环，也不生成节点 trace。[ir 模块](../ir/README.md) 已实现全部核心组合子、JSON 序列化和调用适配；[RunScope](../run/README.md) 提供原语调用外部的运行资源边界。

实现完成度与设计差距见 [2026-10-24 核对表](../../docs/NextGenPrimitivesStatus.md)：18 项入口齐全，17 项有能力执行逻辑；运行与子作用域清理、核心 IR 均已实现，完整产品边界另见核对表。

## 目录与效应

`PrimitiveId::ALL` 是内建目录；`id.contract()` 返回名称、版本、Kind、参数／输出类型、所需能力、调用线程要求、取消契约、提供者／输出所有权、操作完成阶段和可能错误。当前契约版本均为 `1`。`PrimitiveId::lookup(name, version)` 精确匹配，未知名称、大小写差异和未知版本返回 `None`，不选择替代版本；当前 IR 校验器据此在执行前拒绝非法引用。

| 原语名称 | Kind | 输出 | 能力绑定 |
|---|---|---|---|
| `capture` | Effectful | Frame | 视频订阅 |
| `read_audio` | Effectful | AudioWindow | 音频订阅 |
| `region_statistics` | Pure | RegionStatistics | 无 |
| `image_difference` | Pure | ImageDifference | 无 |
| `match_template` | Pure | Match | 固定配置／资产的匹配后端 |
| `ocr` | Pure | OcrResult | kind 选择 Tesseract／PpOcr，各自绑定固定模型实例 |
| `audio_rms` | Pure | Float | 无 |
| `pad_press`、`pad_release` | Effectful | InputReceipt | 手柄会话 |
| `pad_set_hat` | Effectful | InputReceipt | 手柄会话 |
| `pad_set_stick`、`pad_center_stick` | Effectful | InputReceipt | 手柄会话 |
| `pad_apply_report`、`pad_reset` | Effectful | InputReceipt | 手柄会话 |
| `type_text` | Effectful | InputReceipt | 独立文本目标 |
| `sleep` | Meta | Unit | 无 |
| `clock_now` | Meta | Int | 本次运行的 MetaContext |
| `log` | Meta | Unit | 本次运行的 MetaContext／LogSink |

目录可离线查看，不打开硬件、OCR 或 OpenCV：

```sh
cargo run --locked -- primitives
```

`PrimitiveCall` 保存已经求值的参数，Rust 枚举保证参数类型；`PrimitiveOutput` 保留具体结构，不压成字符串或单一分数。目录中的参数类型名称是契约词汇，由 ir 适配层映射到结构化 Type 并校验；PrimitiveHost 自身仍接收已经求值的参数。OCR 的 image 参数接受 `Frame` 或不可变 `Image`，可直接传入 Capture 的输出；Rust 调用使用 `ImageInput::Frame`／`ImageInput::Image`，帧适配共享 RGB 存储并保留来源。其他图像参数使用不可变 `Image`。

`ocr(kind, engine, image, region)` 使用一个原语契约。kind 为 `OcrBackendKind::Tesseract`／`PpOcr`，IR 中是 `Symbol(OcrKind)`；它与目录中的效应 Kind 含义不同。engine 绑定相应模型实例，误配在调用提供者前返回 InvalidArgument；两种 kind 可以在同一工作流并存。结果为 `{kind, source, region, text, confidence, boxes, profile}`，每个 box 保留 text、可选 confidence、原图坐标四点 polygon。顶层 confidence 在 Rust 由 `OcrResult::confidence()` 计算，在 IR 为字段：所有 box 分数的算术均值，空结果或任一未知分数为 None。它不是概率，也不能跨引擎比较。Tesseract 返回单词框；PP-OCR 当前返回单行 ROI，不提供文字检测框。

## 装配与调用

`PrimitiveHost` 按名称借用已创建的提供者。视频、音频、匹配、OCR、手柄、文本各有独立名称空间，不按相同名称推断它们是同一设备，也不自动枚举、连接或切换后端。缺失绑定在实际调用时返回 `CapabilityUnavailable`。

`VideoInput`／`AudioInput` 是可 mock 端口，已有 `VideoReader`／`AudioReader` 实现适配。视频 `after` 游标原样交付；音频帧数要求正整数并能表示为 `usize`，不包含采集设备连接和窗口重试。订阅的游标、缺口与取消行为保持原有契约。返回帧必须有合法 RGB 形状；有 after 时，来源／代次变化报 SourceChanged，未严格前进的序号报 InvalidData。返回音频必须具有非零采样率／通道数和精确的请求帧数，错误窗口报 InvalidData；不隐式补齐、截断或重读。

```rust
use easycon_sense::{
    primitives::{PrimitiveCall, PrimitiveHost, PrimitiveOutput},
    vision::Image,
};
use tokio_util::sync::CancellationToken;

async fn measure(image: &Image) -> easycon_sense::primitives::Result<()> {
    let mut host = PrimitiveHost::default();
    let result = host.execute(
        PrimitiveCall::RegionStatistics { image, region: None },
        &CancellationToken::new(),
    ).await?;
    if let PrimitiveOutput::RegionStatistics(stats) = result {
        println!("{:?}", stats.rgb_mean);
    }
    Ok(())
}
```

媒体会话、模型引擎、手柄和日志端由宿主持有。运行独占自己的订阅；`PrimitiveHost` 的销毁只结束借用，不会关闭共享物理来源或复位外部手柄。直接使用 PrimitiveHost 时，宿主必须显式 `reset`／`disconnect` 并检查结果。使用 RunScope 可转入运行专用订阅并独占借用手柄；finish 返回清理报告，手柄全中立复位后仍保留外部连接。IR 执行器负责嵌套组合子的清理与任务收拢。

匹配／OCR 绑定是可信宿主配置，必须遵守实例内固定模型／配置、显式图像、无隐式重采集或自适应学习的 Pure 契约。模型、字典和模板由上层工作流选择；可选内容摘要属于上层校验／缓存策略，不要求核心统一注册资产。需要读取外部可变状态的实现不能挂到这些 Pure 入口，应另设 Effectful 契约。目录分类不等于已经实现资产摘要、结果缓存或确定性重放；模型路径与来源字符串不能当内容摘要。

## 结果校验与完成阶段

ROI 与模板尺寸在调用识别提供者前检查。匹配结果必须保留输入来源、搜索区域、模式和模板尺寸，候选区域不得越界；raw_score 必须有限，confidence 的有无及换算必须符合模式契约。`TemplateMode::confidence` 是 OpenCV 后端与派发层共用的换算实现。低评分仍为成功结果。

OCR 结果必须保留输入来源、所选原图区域和后端身份；分段置信度可为 None，有值时必须有限且在 0..1，分段坐标必须有限且处于所选区域内。文本原样保留，空识别结果为正常成功。非法识别输出报告 RecognitionError，并保留 VisionError::InvalidOutput；不会改选后端。

目录显式声明 ProviderOwnership（无提供者或借用）、OutputOwnership（普通值或共享只读样本）和 Completion。Capture 成功表示取得样本；ReadAudio 成功表示该订阅游标已前进；输入成功以回执交付级别为准；Log 成功表示 sink 接受提交，持久化保证由 sink 决定。计算、等待和读取时钟分别记录自己的完成阶段。

结果校验失败不会回滚提供者已经发生的操作。尤其是音频提供者若已消费窗口，派发层拒绝其非法结果后也不会恢复游标、重新订阅或重试。完成阶段描述操作事实，不等于后续执行引擎已经发布节点终态，也不表示已完成运行清理。

## 输入与交付

PadPress／PadRelease 在原语层要求非空按钮集合，空集合在调用提供者前报 InvalidArgument；重复按钮仍按位处理，不引入引用计数。基础 `Gamepad` 库保留其允许空集合的独立契约。七项手柄原语各调用一次现有 `Gamepad` 原子方法，保留原报告状态与串口事务语义。`InputReceipt` 保留连接代次、序号、实际 `written_at` 与交付级别：`Written` 表示写入完成，`Accepted` 表示后端已确认接收，均不证明游戏响应。原有 EasyCon／LDN Pokémon 后端不变。

`TypeText` 只调用显式注入的 `TextInput`。当前提供接口与 mock 验收，**没有默认 OS 或 Switch 文本输入后端**；只注册手柄不能满足文本能力。文本按 UTF-8 字符串原样交付，不转拼音、不逐字模拟按键。提供者应在发送前检查取消，开始发送后在自己的预算内返回实际回执或带 `InputProgress::NotStarted/Unknown` 的传输错误，不把可能已交付的输入改报成“未执行”。

按下、等待、抬起仍是三个独立调用；没有长按或组合键时序原语。

## 取消、线程与错误

所有调用在入口检查 token。纯计算成功返回后再次检查取消；RMS 每最多 4096 个交错样本检查一次，RGB 测量每最多 4096 像素检查一次。Sleep 与媒体订阅等待响应取消。OpenCV 模板匹配／DNN 和 Tesseract 的同步原生调用只能前后检查，取消会等待当前计算收尾。

动作／日志提交后的成功回执和真实失败不被晚到取消改写。原语返回后，引擎仍需按设计规范执行其自身的取消／截止时间裁决；本层不会提前替执行器发布节点终态。

`Scheduling::CallerThread` 可能同步执行 CPU 或阻塞操作；`Async` 使用可等待端口。匹配／OCR 使用 `Scheduling::Provider`，派发层等待其异步方法；直接注入同步后端时默认方法仍在调用线程执行。异步宿主可装配 [NativeMatcher／NativeOcr](../native-work/README.md)，在专用线程初始化、调用和销毁后端，并显式 shutdown。原语内部不创建 worker 或分离任务；host 借用提供者并串行派发。future 无 Send 要求，原生状态无须跨线程移动。取消不承诺抢占驱动、原生库或阻塞 LogSink。

`PrimitiveError` 保留原始 `CaptureError`／`VisionError`／`DeviceError`／`MetaError`／`TextInputError`；`kind()` 提供跨后端分类，`recovery()` 返回：

- `Cancelled`：各提供者的取消归一化到独立取消结果，不作为普通恢复错误。
- `Recoverable`：参数、能力缺失、音频缺口、识别、传输等错误。
- `RunFatal`：清理失败或内部不变量／工作线程故障，嵌套连接错误中的清理失败也不会降级。

派发层不重试，不做 fallback，不吞掉清理错误。RunScope 会锁存首次 RunFatal，拒绝所有后续调用，并在 finish 时执行清理；即使调用方传入候选成功结果也无法覆盖该致命错误。直接使用 PrimitiveHost 不提供这种运行保障。当前 ir 执行器附加节点身份、节点路径和表达式位置，保留 typed 原语原因；错误包装保留 cause／suppressed 和被覆盖的候选结果。
