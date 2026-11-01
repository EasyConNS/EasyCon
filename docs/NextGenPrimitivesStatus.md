# 新项目原语实现完成度

状态：代码现状核对，2026-10-24。范围仅为独立 Rust 工作区 `nextgen/`，对照 [编排 IR v0.1 设计规范](NextGenIR.md)，不统计现有 .NET／Flow 功能。本文的“已实现”表示代码和对应测试存在；原生后端、硬件和平台交付另行标注。

## 1. 当前结论与统计口径

原语能力层主体已建立，可以通过 Rust API 独立调用。18 项都有类型化入口、版本化契约和派发路径；17 项具有能力执行逻辑，TypeText 只有可注入接口与 mock。Log 的消息校验、时间戳与单次交付已实现，输出 sink 由宿主提供。

| 维度 | 当前完成度 | 如何理解 |
|---|---|---|
| §6.2 原语目录／类型化派发 | 18 / 18 | 名称、版本、参数、输出、Kind、能力、错误、取消、所有权和完成阶段可查询 |
| 原语能力执行逻辑 | 17 / 18 | TypeText 尚无实际文本输入后端；识别后端有部署依赖 |
| 原语层测试 | 21 项通过 | 覆盖成功、能力缺失、取消、输出校验、错误分类和两种 OCR kind 的同时使用／误绑定拒绝和帧像素共享 |
| 运行作用域基础 | 11 项通过 | 专用订阅释放、独占手柄复位、清理报告、取消隔离和首次致命错误锁存 |
| 原生后端／真实设备交付 | 部分完成 | macOS arm64 真实 PP-OCR v6 与动态 Tesseract 已验收；目标 CI 结果和真实硬件仍待验证 |
| 编排 IR 与组合子 | 62 项测试通过 | Value／Expr、流程引用、全部核心组合子、调用图效应、预算、节点 trace、子作用域清理和原语适配 |
| 原生工作托管 | 12 项测试通过 | 8 项线程生命周期／队列测试及 4 项原语／IR 集成测试，另有 1 项真实模型验收 |
| 项目宿主与设备探查 | 18 项测试通过 | device 内 3 项协议探查、14 项项目／标签／模型／工作流／清理测试、1 项 CLI 入口测试；另有真实双 OCR 工作流验收与 ccap 解码／格式约束测试 |
| 完整编排产品契约 | 部分完成 | 前端来源映射、验证失败 trace 与持久化 schema 等仍待实现；资产由上层工作流选择 |

18 / 18 是入口覆盖率，17 / 18 是能力实现项数，均不能换算成整套设计或产品交付的百分比。单项原语也可能同时具有“代码完成、目标平台尚未验收”两种状态。

## 2. 已实现原语清单

| 原语 | Kind | 结构化输出 | 已实现行为与剩余边界 |
|---|---|---|---|
| Capture | Effectful | Frame | ccap 唯一视频后端；最新帧或 after 后的新帧；保留身份、时间和实帧格式，FPS=0 表示未知；OBS 替身已通过，实体卡待验收 |
| ReadAudio | Effectful | AudioWindow | CPAL 输入、独立订阅游标、精确帧数、缺口显式报错；待真实采集卡验收 |
| RegionStatistics | Pure | RegionStatistics | RGB 通道最小／最大／均值、像素数、原图区域与来源；已实现纯 Rust 算法 |
| ImageDifference | Pure | ImageDifference | 显式两图／ROI，MAE、RMSE、最大通道差、变化像素数、双方来源；已实现纯 Rust 算法 |
| MatchTemplate | Pure | Match | 原生 OpenCV >= 5 动态桥接、六种度量、原始评分与模式对应的可选 confidence；原生运行库与平台验收仍有缺口 |
| Ocr | Pure | OcrResult | 单一原语，kind 选择 Tesseract／PpOcr；直接接受 Frame 或 Image，输出 text／confidence／boxes 与来源；分别绑定 models/ 下的固定模型；PP-OCR 当前只提供单行 ROI，检测暂缓 |
| AudioRms | Pure | Float | 显式窗口的所有通道总体 RMS；拒绝非有限样本，无业务静音阈值，分批检查取消 |
| PadPress | Effectful | InputReceipt | 一份报告设置非空按钮集合，保留其余状态；不等待、不自动抬起 |
| PadRelease | Effectful | InputReceipt | 一份报告清除非空按钮集合，保留其余状态；重复按钮不引入引用计数 |
| PadSetHat | Effectful | InputReceipt | 更新八方向／中立方向帽，保留其他状态 |
| PadSetStick | Effectful | InputReceipt | 更新指定左右摇杆，原始坐标 0..255，保留其他状态 |
| PadCenterStick | Effectful | InputReceipt | 指定摇杆回到 128,128 |
| PadApplyReport | Effectful | InputReceipt | 校验并一次替换完整报告 |
| PadReset | Effectful | InputReceipt | 一次发送全中立报告 |
| TypeText | Effectful | InputReceipt | 只有独立 TextInput 端口、派发与 mock；没有 OS／Switch 文本输入实现，手柄不能自动满足此能力 |
| Sleep | Meta | Unit | 非负毫秒等待，取消可中断，零时长让出调度权 |
| ClockNow | Meta | Int | MetaContext 起点以来的单调毫秒数；由宿主每次运行创建上下文 |
| Log | Meta | Unit | 带运行时间戳的消息与平面结构化字段，同步提交一次给宿主注入的 LogSink；不是节点 trace |

七项手柄原语共用 `Gamepad`，已有 EasyCon 7 字节报告内容编码与 LDN Pokémon 两种串口实现。通用串口枚举、连接、握手、断开、协议应答和资源释放由基础库提供，不额外计入 §6.2 的 18 项执行原语。长按、点击时序、业务组合键与重试仍应由编排层组合。

## 3. 对照设计规范的语义完成度

| 设计要求 | 状态 | 代码现状／差距 |
|---|---|---|
| §2 原语能力与业务控制分离 | 原语层已实现 | 单次调用，无业务循环、隐式重采集或动作重试；内部算法遍历、I/O 就绪等待和连接协议流程保留 |
| §2／§4 全输入 Expr、所有结果可绑定 | 核心已实现 | 全部组合子使用 Expr 输入及公共 result_bind；类型化 FlowRef、ErrorRecord、不可变绑定与词法作用域；已开放帧／音频时间、视频格式和图像来源／ROI；written_at 仍为 Rust Instant |
| §6.1 效应分类 | 调用图与 Par 准入已实现 | 覆盖所有分支、递归调用固定点及动态 FlowRef 上界；运行参数递归检查文档身份与签名，Par 只准入 Pure／Meta |
| §6.1 Pure 的实例配置与实现身份 | 基本契约已实现 | 不可变显式图像、实例内固定模型／配置、实现／profile 字符串；Tesseract 禁用自适应学习。模型、字典、模板由上层工作流选择；核心不要求资产注册表或内容摘要，可选摘要由上层校验／缓存策略负责 |
| §6.2 样本身份、坐标与测量结果 | 原语层已实现 | Arc 样本、来源／代次／序号／时间；ROI 保留原图偏移，低评分／空 OCR 是正常结果；不承诺硬件同步，三平台设备行为待验收 |
| §6.2 提供者边界校验 | 已实现主要检查 | 校验视频 RGB 形状和游标前进、音频格式与窗口长度、识别来源／区域／模式／有限评分／confidence。AudioInput 注入端仍需保证样本有限和连续；派发层不逐样本重验，也不维护其连续性历史 |
| §6.2 动作结果与完成阶段 | 部分完成 | 成功回执保留会话、序号、Written／Accepted 和 written_at；提交后不以晚到取消覆盖结果。文本传输错误有 NotStarted／Unknown；所有手柄失败尚未统一为可查询的交付阶段记录，不能从一般 Io 错误推断“尚未发送” |
| §7 错误分类与恢复 | 已实现主要契约 | Catch／Fallback／Retry 只处理 Recoverable；保留 typed 错误、节点／调用身份、origin、cause、suppressed 和候选值；RunFatal 锁存并绕过恢复 |
| §7 资源清理 | 运行与子作用域已实现 | 运行释放专用订阅并全中立复位手柄；尝试／迭代／调用／恢复／超时子作用域释放所触碰的按键状态，清理后才发布终态和继续恢复。子节点借用运行订阅，不自动重建游标；外部连接和共享物理会话继续由宿主持有 |
| §9 取消与调度 | 核心裁决已实现 | 本地中央队列避免递归 poll 栈；Par 并发异步等待、同批索引裁决、取消并收拢；共享循环／调用预算、父取消／局部截止裁决。匹配／OCR 可用专用线程托管，异步等待时定时器和取消能推进，取消后收拢实际工作；IR 运行资源锁仍使能力调用串行，原生调用可延长停止时间。RunControl 支持首个 UserStop／Shutdown／Aborted 原因；兼容原 token 入口，提供者取消单列 ProviderCancelled，清理失败仍优先 |
| §10 内建可观测性 | 执行 trace 已实现 | 节点开始／唯一终态、嵌套 Branch／Attempt／Iteration、RetryScheduled／BranchAccepted／ScopeCancellation、身份／接受顺序／清理报告；source_span、验证失败事件和持久化格式待实现 |
| §11 序列化与执行前校验 | 核心执行 schema 已实现 | 全部核心节点 tagged JSON、i64 字符串、有限 Float、往返、未知字段／版本拒绝、绑定与调用效应校验；annotations 等诊断扩展尚待加入；上层工作流资源包不作为核心 IR 必填 schema |
| §12 平台与真实来源验收 | 待验收 | 代码面向 Windows／macOS／Linux；未完成三平台采集卡持续采集、拔插、权限、串口和取消延迟的设备矩阵 |

Completion 是契约上的成功完成阶段；InputReceipt 是单次动作的实际成功证据。两者不表示节点终态已发布，也不能替代失败交付进度或清理报告。ReadAudio 已推进的游标不会因调用方丢弃结果或派发层拒绝非法输出而回滚。

## 4. 原生后端与交付状态

| 后端 | 已有产物 | 尚不能认定完成的部分 |
|---|---|---|
| ccap／CPAL | 真实设备适配、枚举／格式查询、会话／订阅、诊断 CLI、合成来源与缓冲测试 | 三平台真实采集卡持续输入、断连、权限和原生释放验收 |
| EasyCon／LDN Pokémon | 编码、握手、增量状态、部分写入、取消、应答、故障失效、断开；仅凭串口名按协议／波特率顺序自动探查；假串口验证 | 真实单片机与三平台串口驱动验收 |
| OpenCV >= 5 | 自有 C ABI 桥接、动态加载、版本／ABI 校验、六种匹配与 DNN 新引擎；macOS 自备 5.0.0 SDK 真实匹配／OCR 通过；官方 Windows SDK 准备、打包与 CI 配置 | 本地未验证 Windows 桥接执行，仓库没有预构建好的 Windows 归档；macOS／Linux 自备 SDK；不能将 CI 配置存在视为远端执行已通过 |
| Tesseract | 可选 feature、运行时 C API 加载、模型／语言／PSM 配置、禁用学习、TSV 单词框；macOS 5.5.3 真实英文／中文／混合文本回归通过 | Windows／Linux 真实运行与其他语言数据仍待验收；同步计算只在调用前后响应取消 |
| PP-OCR | 可选 feature、OpenCV 5 DNN ENGINE_NEW 加载本地 ONNX／字典、预处理、CTC 解码；macOS 本地现成 PP-OCRv6_medium_rec 及游戏截图 ROI 回归通过 | 文字检测／多行完整流水线暂缓，当前不能称为完整 PaddleOCR 管线；同步推理只在调用前后响应取消，其他平台仍待验收 |
| NativeWorker | 专用线程初始化／复用／销毁非 Send 状态，有界队列、取消后等待、异步 shutdown／join、Rust panic 失效；匹配与两种 OCR 已接入 | 不强制抢占原生调用；Drop 兜底可阻塞，推荐显式 shutdown；IR 内能力调用仍串行 |
| TypeText | 独立端口、交付回执／错误约定、注入测试 | 目标设备与真实输入后端尚未确定并实现 |

自动缓存、确定性重放、旧 ECS／ECX 兼容、厂商专用采集 SDK 和桌面／回环采集不作为当前原语交付的欠项；其中前两项也不能仅凭 Pure 分类自动启用。

## 5. 本次验证与设计验收映射

2026-10-24 在当前 macOS 工作区执行核心验证；完整真实模型回归沿用同日原生验收记录，本轮更新默认入口、简化设备配置和旧 .IL 标签约定，再次验证项目宿主 OpenCV 默认加载：

| 命令（在 nextgen 执行） | 结果 |
|---|---|
| cargo test --locked --workspace --all-targets | 208 通过，0 失败，2 跳过 |
| cargo test --locked --workspace --all-targets --all-features | 209 通过，0 失败，9 跳过 |
| 显式提供真实模型／动态库，运行 vision::tests::native::real_（--ignored） | 5 通过，0 失败；本地 PP-OCR v6、Tesseract 5.5.3，中英混合文本与游戏 ROI |
| 显式提供 OpenCV 5 DNN 运行包，运行 real_opencv（--ignored） | 2 通过，0 失败；六种匹配模式、坐标与来源语义 |
| 显式提供相同原生库和模型，运行 --test native_work real_native_workers（--ignored） | 1 通过，0 失败；独立线程上的真实匹配／Tesseract／PP-OCR v6 重复调用和显式关闭 |
| cargo test --locked --workspace --doc --all-features | 4 项 API 示例编译通过 |
| cargo clippy --locked --workspace --all-targets --all-features -- -D warnings | 通过 |
| cargo fmt --all --check | 通过 |
| easycon-ir run examples/combinators.ir.json | 返回 Int(42)，CLI 使用新的 RunControl 入口 |
| easycon-run examples/capture-match/workflow.json --check | 无硬件／无原生库预检通过，旧格式 .IL 的内嵌图片、ROI 和搜索方法已加载，入口参数类型正确 |
| python3 -m unittest discover -s native -p 'test_*.py' | 10 项通过，覆盖宿主／项目模型打包及覆盖保护 |
| 打包后的 easycon-run，从 /tmp 启动且移除 EASYCON_OPENCV_DIR | 自动加载程序旁 native/osx-arm64 的真实 OpenCV 5，完成图片自身匹配及资源回收；使用本机自备 SDK |

两种配置均包含 21 项原语目录／派发／边界测试、11 项运行作用域测试和 62 项 IR 测试；它们是上述总数的子集，不能相加。8 项 worker 单元测试和 4 项托管集成测试也计入上述总数。本轮新增 10 项 IR 回归，覆盖停止原因竞争／嵌套恢复绕过／局部截止与预算隔离／提供者取消／清理终态优先级，以及样本时间、格式、来源与裁剪坐标投影。全 feature 默认跳过 9 项：两项真实 OpenCV、两项 Tesseract、三项 PP-OCR、一项托管真实回归和一项项目双 OCR 回归；新增项目回归本轮通过，其余 8 项沿用同日显式加载真实库与模型的验收记录，见 [验收记录](../nextgen/vision/ACCEPTANCE.md)。其余无硬件测试不能证明真实设备或其他平台已验收。本次没有执行远端 CI。Clippy 严格检查通过；视频后端已统一替换为 ccap；nokhwa 与 block 0.1.6 已从当前 Rust 依赖树移除。

项目宿主与设备探查的 18 项测试已计入总数；新增 3 项验证模型相对路径、两套独立 OCR 实例声明／绑定、缺失／空模型和越界路径拒绝。设备 crate 已合并通用手柄契约、EasyCon／Pokeldn 内置后端和自动探查。其余覆盖：旧 `.IL` 的 Base64／ROI／搜索方法与编辑元数据、六种匹配模式、拒绝资源引用扩展和非法区域、按识图需求扫描全部标签、仅解码已引用标签、相邻设备配置和缺失配置检查、默认 workflow／目录入口、协议探查顺序与错误保留、低分／低分／达阈值只产生两次 A 点击、每轮新帧游标、按键等待期间取消后复位，以及物理断开失败后继续清理。视频现验证 ccap packed RGB 转换、padding／方向、非法帧拒绝、时间戳与格式约束，不再采用旧的最高帧率模式排序。以真实旧格式标签从 `/tmp` 启动打包宿主，移除环境覆盖后自动加载应用旁 OpenCV 5、完成自身匹配并收尾；无需 config 或实际设备。此前真实 CLI 的 Sleep／SIGINT 验证保留：等待清理后退出码 130，trace 保留 UserStop。Windows 官方 SDK 宿主打包流程已加入 CI，尚未执行远端验收。

本轮新增完整双 OCR 项目验收：`workflow.json` 分别声明模型实例，两个调用节点统一为 `ocr`，kind 分别为 Tesseract／PpOcr，同时加载项目 `models/` 的 Tesseract 英文／中文 traineddata 和本地 PP-OCRv6_medium_rec ONNX／字典，两者结果去除空白均为 `自动化测试EasyCon123`。完整项目及四个模型／字典文件已复制进本机发布包，从 `/tmp` 启动并自动加载应用旁 OpenCV；Tesseract 原生库本次通过显式环境路径提供，未将本机 Homebrew 运行库认定为可分发资产。模型字节摘要在复制前后相同，大模型不提交到源码库。

通用 OCR 契约回归新增 4 项：采集帧通过 Pipe 直接送入两种 kind、kind 和输入像素的静态类型拒绝、帧适配不复制 RGB 且在调用前拒绝错误形状、空／未知置信度与汇总均值。现有同帧识别适配、托管取消／复用和项目示例均迁移到 ocr；boxes 保留原图多边形与分段置信度。真实 worker 验收已改为通过统一 PrimitiveCall::Ocr 读取帧数据，并检查 kind、来源、boxes 和汇总置信度。

设计 §13 的主要执行语义已有对应测试：V01–V05 的顺序／分支／空并行、V06–V11 的重试／并发／流程引用、V12–V24 的类型／截止／取消／共享预算／作用域、V28–V30 的子作用域按键清理／调用 trace／JSON，以及 V32 的 Observe。V15 增加真实工作线程上的非 Send mock，覆盖 Timeout／Race 取消后等待，调度线程保持响应；真实引擎另验收复用与关闭，不代表原生取消时延基准；V06 的订阅由运行持有而非每轮新建，节点请求会先收拢，音频游标不回滚。

V25–V27 的同帧输入、低评分／空文本／静音、来源代次／音频缺口已有基础与适配测试；V31 的版本／类型／绑定已覆盖；资源摘要由上层按需核验，不作为核心必填项；V33 的共享来源隔离有基础测试，应用退出与三平台硬件生命周期还需验收。因此不能将完整 V01–V33 或产品交付统一标为全部通过。

## 6. 建议后续顺序

1. 原生匹配／OCR 工作托管已落地；按真实使用需要继续托管设备打开／停止和同步动作，保持明确的交付与收尾语义。
2. 补 source_span／annotations、验证失败事件与稳定 trace schema；上层编排定义每套工作流资源配置和可选摘要，再接 ECS 前端。
3. 原生库／真实硬件验收独立推进，统一动作失败的已知／未知交付阶段；如需单次 IR 运行中的识别并行，再缩小资源锁范围，不预设线程池。
4. TypeText 先明确目标与后端需求，再补真实实现。PP-OCR 检测按既定暂缓范围另行安排。

## 代码与文档入口

- [原语调用与派发](../nextgen/src/primitives.rs)、[目录与元数据](../nextgen/src/primitives/catalog.rs)、[输出校验](../nextgen/src/primitives/validation.rs)、[端口／回执](../nextgen/src/primitives/ports.rs)、[错误分类](../nextgen/src/primitives/error.rs)。
- [IR 使用说明](../nextgen/ir/README.md)、[模型与 API](../nextgen/src/ir.rs)、[IR 测试](../nextgen/src/ir/tests.rs)、[基础 JSON](../nextgen/examples/composition.ir.json)、[组合子 JSON](../nextgen/examples/combinators.ir.json)、[组合子测试](../nextgen/src/ir/tests/combinators.rs)、[停止／元数据测试](../nextgen/src/ir/tests/runtime_contract.rs)、[宿主停止控制](../nextgen/src/ir/cancellation.rs)。
- [原生工作托管](../nextgen/native-work/README.md)、[线程实现](../nextgen/src/native_work.rs)、[识别适配](../nextgen/src/vision/worker.rs)、[集成测试](../nextgen/tests/native_work.rs)。
- [运行作用域](../nextgen/run/README.md)、[实现](../nextgen/src/run.rs) 和 [作用域测试](../nextgen/src/run/tests.rs)。
- [原语测试](../nextgen/src/primitives/tests.rs)、[语义边界测试](../nextgen/src/primitives/tests/semantics.rs)、[手柄集成测试](../nextgen/tests/gamepad.rs)、[原生识别测试入口](../nextgen/src/vision/tests.rs)。
- [原语使用说明](../nextgen/primitives/README.md)、[基础核心](../nextgen/README.md)、[视觉能力](../nextgen/vision/README.md)、[通用手柄](../nextgen/device/README.md)、[OpenCV 原生部署](../nextgen/native/README.md)。
