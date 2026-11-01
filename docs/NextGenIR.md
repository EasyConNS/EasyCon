# EasyCon 新项目：编排 IR v0.1 设计规范

状态：IR 规范与核心组合子实现；原始感知、手柄与视觉原子能力已在 [nextgen](../nextgen/README.md) 实现。设计日期：2026-10-23；实现核对：2026-10-24，见 [原语完成度](NextGenPrimitivesStatus.md)。

本文面向另起的新项目，定义跨前端的执行契约。它不描述当前 .NET 产品的实现，也不修改现有 ECS、ECX 或 MCU 语义。本文保留设计契约；已授权落地的基础能力在独立 Rust 工作区实现。

## 1. 已确定的范围与开发栈

| 项目 | 决策 |
|---|---|
| 支持目标 | Windows、macOS、Linux |
| 视频输入 | 通过系统标准视频设备访问的采集卡，唯一 ccap 后端 |
| 音频输入 | 通过系统音频输入端点访问的采集卡音频，CPAL 后端 |
| 感知与执行核心 | Rust |
| 异步执行与取消 | Tokio、tokio-util CancellationToken |
| 桌面宿主与界面 | Tauri 2、TypeScript、React、Vite |
| 表达与执行 | ECS 是编译器前端之一；各前端生成同一套类型化编排 IR |

桌面、窗口、系统回环音频采集不在范围内。采集卡的音视频输入分别选择并建立配对关系，不依据设备名称或枚举序号自动认定配对。

识别与动作原语在本文中规定契约。当前已实现区域 RGB 统计、显式双图差异测量及可替换的模板匹配／OCR 接口；模板匹配由项目自有桥接动态调用原生 OpenCV，不依赖 OpenCvSharp。提供的二进制仅来自官方 Release，先接入 Windows 原生 SDK；macOS／Linux 等待自备 SDK。最低主版本为 5，兼容升级允许替换原生库，跨主版本需对应 SDK 重建桥接。Tesseract 动态 C API 与 OpenCV 5 原生 DNN 的 PP-OCR 单行识别已实现，PP-OCR 文字检测暂缓；Sleep、ClockNow、Log 作为独立 Meta 能力已实现。具体见 [视觉原子模块](../nextgen/vision/README.md)、[基础核心](../nextgen/README.md) 与 [通用手柄模块](../nextgen/device/README.md)。已实现 [18 项内建原语目录与类型化派发](../nextgen/primitives/README.md)，支持能力注入、mock 和统一错误分类；TypeText 只有独立端口，没有默认后端。运行作用域 [RunScope](../nextgen/run/README.md) 已实现专用订阅释放、独占手柄复位、清理报告、取消隔离与致命错误锁存；[ir 模块](../nextgen/ir/README.md) 已实现 Value／Expr、静态类型／作用域／原语版本校验、调用图效应固定点、流程引用、全部核心组合子、共享预算、子作用域清理、原语适配与节点 trace。Observe 降低为已有组合子；匹配／OCR 已提供专用线程托管，初始化、调用和销毁在同一线程完成。前端来源映射与完整 trace schema 仍待实现；详细边界以实现说明为准。厂商专用 SDK 设备、旧字节码兼容、自动缓存和确定性重放均不属于 v0.1 的交付范围。

```text
ECS 前端 / 其他表达前端
             |
      类型检查、链接、效应分析
             |
        类型化编排 IR
             |
         Rust 执行引擎
    组合控制 / 取消 / trace / 资源
             |
         类型化原子能力
             |
    ccap / CPAL / 其他能力后端
```

## 2. 设计原则

1. 原语提供能力，组合子负责业务控制流。原语不得隐藏业务分支、业务循环或重复尝试；参数检查、算法内部遍历、协议处理和必要的资源清理属于能力实现。
2. 所有外部输入与控制参数通过 Expr 表达。所有流程节点的成功输出都可绑定，绑定由引擎完成。
3. 流程是可传递的类型化值。组合子接受子流程，产生新的流程；前端语法不定义另一套运行语义。
4. 效应显式声明并由引擎验证。Pure、Effectful、Meta 的分类用于合法性分析，不等价于缓存或重放许可。
5. 节点 trace 由引擎产生。原语无需手工报告节点开始、结束或重试。
6. 原语保留结构化结果。前端通过 Expr 提取字段或执行显式转换，不迫使底层只返回旧语法能表达的标量。
7. 每个运行具有结构化任务与资源作用域。节点返回前必须收拢其启动的工作并处理其拥有的资源。

## 3. IR 文档与节点模型

一个核心 IR 文档包含版本、入口、流程定义及其类型和原语契约版本。运行参数与运行期间取得的样本不属于静态 IR。工作流的模型、字典、模板和后端配置由上层编排描述并交给宿主装配；运行通过类型化绑定引用能力，不要求统一资产注册表或固定项目资产。

| 元素 | 契约 |
|---|---|
| ir_version | v0.1 固定为字符串 0.1，执行器只接受完全匹配的版本 |
| entry | 指向一个具有显式参数和输出类型的流程定义 |
| definitions | 具名流程定义集合，每个定义具有唯一 id、参数、输出类型和 body |
| node_id | 文档内唯一且稳定的静态节点标识 |
| source_span | 可选的前端文件、起止位置；用于诊断，不影响执行 |
| result_bind | 所有节点共有的可选输出绑定目标 |
| annotations | 仅允许放置在独立的扩展命名空间中，不得改变执行语义 |

模型／模板的可选摘要可放在上层运行包，用于核验、缓存或重放；不要求核心 Document.assets。路径仅作诊断身份，不是内容摘要。source_span 与 annotations 是待补的前端／诊断扩展，当前执行 schema 尚未接受这些字段。

子流程位置可使用内联流程，也可引用 Expr 求值得到的零参数 FlowRef。具有参数的流程通过 SubflowCall 调用。普通数据表达式用 Eval 进入流程，因此流程无需借助日志或空操作返回计算值。

节点成功后，引擎在调用方当前作用域写入 result_bind，再发布该节点的成功终态。失败或取消不写入该绑定。静态检查必须拒绝同一作用域中的重复定义，运行期绑定失败不得被隐式忽略。

## 4. 值类型与 Expr

### 4.1 值类型

| 类型 | 规则 |
|---|---|
| Unit | 无业务结果，与 None、空 List、空 Tuple 不同 |
| Bool | 只接受布尔值，不进行真假值隐式转换 |
| Int | 有符号 64 位整数，普通运算溢出返回 NumericError |
| Float | 有限的 64 位浮点数，拒绝 NaN 和无穷值，负零规范化为零 |
| Str | Unicode 字符串，不隐式规范化或忽略大小写 |
| List<T> | 同型列表，空列表必须具有可推导或显式声明的元素类型 |
| Tuple<T1, T2, ...> | 固定长度、可异型，按位置访问；允许空 Tuple |
| Record | 具有固定字段名和字段类型的记录 |
| Option<T> | Some 或带有元素类型的 None |
| FlowRef<P, R> | 参数签名 P、结果类型 R，以及经验证的效应上界 |
| FrameRef、AudioWindowRef | 运行期只读样本引用，包含可查询的类型化元数据 |
| SourceRef | 具有种类和绑定名的能力引用，不暴露原生指针 |

v0.1 不提供 Any、隐式数值提升或宿主对象值。记录字段顺序不影响类型与相等性，Tuple 与 List 的顺序有意义。样本引用、来源引用和流程引用不支持通用相等比较；需要比较身份时显式访问相应 id。

### 4.2 Expr 构成与求值

Expr 包含字面量、变量引用、字段访问、索引、记录／列表／Tuple／Option 构造、一元运算、二元运算、显式数值转换、内建纯函数调用和流程引用构造。内建函数由注册名与契约版本标识，IR 不携带宿主闭包或可执行函数指针。

求值按语法顺序进行：函数参数、记录字段初始化项和集合元素均从左到右。and、or 只在需要时求值右侧。条件只接受 Bool；越界索引、访问不存在字段、解包 None 均报告带表达式位置的错误。

组合子的控制参数在进入节点时求值一次，并保存在本次调用中；Choose 的条件在选择前求值，LoopUntil 的条件和状态更新按每轮规则重新求值。子流程和原语的参数在各自实际进入时求值。纯表达式不会读取当前时钟、设备或可变宿主环境。

常量已能确定的类型、范围或引用错误在执行前报告 InvalidFlow。运行参数导致的求值错误在运行时报告 ExprError，保留具体 kind，例如 NumericError、IndexOutOfBounds 或 UnwrapNone。表达式成功求值后，若控制参数不满足范围约束，则由所属组合子报告 InvalidArgument。

### 4.3 内建纯函数

| 函数 | 签名与精确规则 |
|---|---|
| len | List<T> 或 Str → Int；Str 按 Unicode 标量值计数，不按字节或字素簇计数 |
| contains | Str、Str → Bool；区分大小写的精确子串检测 |
| starts_with、ends_with | Str、Str → Bool；区分大小写，不做 Unicode 规范化 |
| abs | Int → Int 或 Float → Float；Int 最小值取绝对值报 NumericError |
| min、max | 一个或多个同型 Int，或一个或多个同型 Float；零参数非法 |
| is_none、is_some | Option<T> → Bool |
| unwrap | Option<T> → T；None 报 UnwrapNone |

in 对 Str 使用子串检测，对 List<T> 使用 T 的类型化相等比较。可比较值包括标量、支持比较的集合、记录和 Option；不为任意宿主引用提供相等性。数值运算要求同型，除零和非有限结果报 NumericError。Int 除法向零截断，余数符号跟随被除数，最小 Int 除以负一按溢出处理。

数值转换不隐式发生。Int 转 Float 按 IEEE 754 最近值、同距取偶数舍入；Float 转 Int 向零截断，截断值超出 Int 范围报 NumericError。前端需要转换时必须生成显式转换表达式，不能依赖宿主语言的强制转换行为。

now_ms 不属于纯函数。读取时间使用 Meta 原语 ClockNow，输出在当前 run 起点之后的单调毫秒数，再绑定供 Expr 使用。运行起点、时钟频率和宿主实现不进入脚本的业务数值。

## 5. 流程值、作用域与循环状态

### 5.1 流程值

FlowRef 引用本 IR 文档中已经链接和校验的流程定义。它可以绑定、传参、从流程返回，也可以由 Choose 等组合子选出。v0.1 使用显式参数，不隐式捕获调用方局部变量，不允许运行期间从字符串或宿主闭包构造新流程。

可动态传入的 FlowRef 必须属于当前文档的已验证定义集合，且签名和效应不超过参数声明的上界。执行器重新计算定义的效应摘要，不信任前端或输入文件自报的分类。递归调用允许存在，但定义必须声明签名；效应按调用关系求并集直至稳定，并受运行调用深度约束。

### 5.2 作用域

绑定不可变，允许在子作用域遮蔽父绑定；同一作用域重复定义属于 InvalidFlow。普通变量名不得使用保留的美元符号前缀。

| 场景 | 可见性与退出规则 |
|---|---|
| Seq | 建立顺序作用域，前一子项的绑定对后续子项可见；退出后仅输出流向调用方 |
| Bind | 将 expr 绑定到当前顺序作用域；next 与后续兄弟可见；不会单独建立隔离作用域 |
| Par | 每个分支获得独立环境，不允许回写父作用域，通过结果汇合 |
| Choose、Catch、Fallback | 每个实际执行的分支使用子作用域；局部绑定不向外泄漏 |
| Retry | 每次尝试建立新的子作用域，失败尝试的绑定不保留 |
| Pipe | sink 的子作用域具有只读的 $prev；嵌套 Pipe 按词法关系遮蔽 |
| SubflowCall | 参数绑定在隔离作用域中，不能读取调用方未显式传入的局部变量 |
| LoopUntil | 每轮具有独立作用域，通过显式状态传递跨轮数据 |

result_bind 总是写入节点的调用方作用域，而非节点内部的分支作用域。静态检查按实际可见性进行确定赋值分析；不能仅因某分支可能执行，就认为其局部名字在外部存在。

### 5.3 循环状态

LoopUntil 可指定 state_init 和 state_next。省略两者时状态类型为 Unit；声明 state_next 时必须同时声明 state_init，且更新结果与初始状态同型。

- $state 是本轮输入状态，body、cond、state_next 均可读，不可原地修改。
- $iteration 是本 LoopUntil 的本轮编号，从 1 开始。
- body 成功后，它的输出通过 $result 提供给 cond 和 state_next。
- cond 为 true 时立即返回 $result，不再计算 state_next。
- cond 为 false 且允许继续时，计算 state_next 作为下一轮状态；未指定则沿用原状态。

body 内部的局部绑定不泄漏到 cond。条件需要的数据应放在 body 的结构化输出中。这样循环状态、观察结果与编译器临时变量各有明确归属。

## 6. 原子能力与效应

### 6.1 分类

| Kind | 定义 | 典型能力 |
|---|---|---|
| Pure | 结果仅由显式的不可变输入、固定配置与固定资产决定 | 对指定帧匹配模板、OCR、对指定窗口计算 RMS |
| Effectful | 观测外部可变状态，或改变外部状态 | capture、取得实时音频、输入设备动作 |
| Meta | 引擎提供的运行控制和记录能力 | ClockNow、Sleep、Log |

Pure 原语必须明确其实现契约；模型与配置在单个已加载提供者实例内固定，不隐式读取当前帧、切换默认模型或读取可变配置。不同工作流可以装配不同实例，模型选择权在上层编排。核心不强制内容摘要；需要核验、缓存或重放时由上层提供。某后端不能满足 Pure 契约时，应另按 Effectful 注册。分类不保证不同硬件实现具有逐位相同的浮点结果。

每个原语契约包含：名称与版本、参数类型、输出类型、Kind、所需能力、可返回的错误种类、资源所有权、取消行为和外部操作完成阶段。能力可用性在实际调用时检查，使 Fallback 能处理某个运行环境缺少能力的情况。

Par v0.1 只接受 Pure 与 Meta。效应检查递归覆盖子组合子、所有可选分支、具名调用和动态 FlowRef 的效应上界。不可到达的分支也参与静态检查；未知效应不放行。包含 Effectful 的子流程即使放在 Retry、Timeout 或 Catch 内，也不能借此进入 Par。

此规则允许对同一帧并发编排多个识别，但不保证 CPU 并行；当前 IR 运行资源锁使提供者调用串行。不允许在 Par 中直接同时采集实时视频与音频。未来若需要并行实时观测，应显式修订效应准入规则。媒体设备后台生产样本不等同于在 IR 中并发执行采集原语。

### 6.2 原语结果契约

| 能力 | 输入与输出 | 语义 |
|---|---|---|
| Capture | VideoSourceRef、可选 after 游标 → FrameRef | 无游标时取得最新可用帧，无帧则等待首帧；有游标时等待同一来源代次中的更新帧 |
| ReadAudio | AudioSourceRef、正整数采样帧数 → AudioWindowRef | 从本运行的订阅游标取得连续窗口；不足则可取消地等待，数据缺口报 AudioGap |
| RegionStatistics | FrameRef、可选 ROI → RegionStatistics | 返回区域 RGB 通道最小／最大／均值、像素数、原图坐标与来源，Pure |
| ImageDifference | 左右 FrameRef、各自可选 ROI → ImageDifference | 等尺寸区域逐像素比较，返回 MAE／RMSE、最大通道差、变化像素数与双方来源；无隐式基线，Pure |
| MatchTemplate | FrameRef、不可变模板与匹配配置 → Match | 返回最佳候选的区域、原始评分、度量标识、可用的归一化 confidence 和帧身份 |
| Ocr | kind、OcrRef、FrameRef 或 Image、可选区域 → OcrResult | 单一原语，kind 选择 Tesseract／PpOcr；绑定各自固定模型实例，可在同一流程同时使用；无文字是正常空结果 |
| AudioRms | AudioWindowRef → Float | 返回所有通道样本归一化幅度的总体均方根，非负；不内置静音业务阈值 |
| PadPress、PadRelease | 手柄会话、一个或多个按钮 → InputReceipt | 一次报告内设置／清除指定按钮位，保留其他状态；不包含等待和自动释放 |
| PadSetHat | 手柄会话、方向帽 → InputReceipt | 替换八方向／中立状态，其他状态保留 |
| PadSetStick、PadCenterStick | 手柄会话、左／右选择、坐标（设置时） → InputReceipt | 更新指定摇杆；原始坐标 0..255，128 居中 |
| PadApplyReport、PadReset | 手柄会话、完整报告（设置时） → InputReceipt | 一次报告替换完整状态；Reset 为全中立状态 |
| TypeText | 文本目标、文本 → InputReceipt | 需要独立的文本输入能力，不默认由 Switch 手柄后端提供 |
| Sleep | 非负 duration_ms → Unit | 可取消等待；零时长仍让出一次调度机会 |
| ClockNow | 无参数 → Int | 当前运行起点以来的单调毫秒数 |
| Log | 消息与结构化字段 → Unit | 产生用户消息事件，独立于自动节点 trace |

Match.confidence 为 `Option<Float>`：归一化模式提供 0..1 的相似度，非归一化模式为 None，不为原始评分伪造标尺。默认 SqDiffNormed 提供 confidence；CCoeffNormed 将 [-1,1] 映射到 [0,1]。它们不承诺统计概率或跨度量可比性。命中阈值由 Expr 与 Choose 等组合子判断。模板不存在或搜索区域非法属于错误；低评分不属于错误。帧元数据、原图坐标系和原始评分均予以保留，ECS 前端可显式提取可用的 confidence。

OCR 参数 `kind` 使用 `Symbol(OcrKind)`（`Tesseract`／`PpOcr`），与效应分类的 `Kind::Pure` 不同。`engine` 绑定固定模型的实例，必须与所选 kind 一致。`image` 可直接消费 Capture 返回的 Frame，也可接受 Image；不会隐式采集或切换模型。

`OcrResult` 保留 `{kind, source, region, text, confidence, boxes, profile}`。kind 使用同一个类型化符号；source 保存帧身份、时间与原尺寸，region 和每个 box 的四点 polygon 使用原图坐标。boxes 中每项包含 text、`Option<Float>` confidence 和 polygon。顶层 confidence 为所有 box 置信度的算术均值；空 boxes 或任一分数未知时为 None，不忽略未知值、不填零。分数要求有限且处于 0..1；汇总不表示概率，不承诺跨引擎或不同分段粒度可比。Tesseract 返回单词框；当前 PP-OCR 只有单行识别，box 是输入 ROI，文字检测／多行流水线仍待实现。原有 `tesseract_ocr`／`ppocr_ocr` 名称不再是原语入口。

采集原语只取得一次样本或一次指定大小的窗口，不隐藏业务重试。等待 I/O 就绪属于本次操作，不构成业务循环。识别原语显式消费样本，不在内部重新采集。

FrameRef 与 AudioWindowRef 只读且受引用生命周期管理。每个样本保留来源 id、会话代次、序号、宿主接收时间、可用的来源时间和格式信息；不同来源时钟不得不经映射直接比较。Expr 已开放 Frame.time／format、AudioWindow.time 和 Image.source／bounds；Frame.format.frames_per_second 为整数，0 表示后端未报告整数帧率，不推算或伪造设备 FPS；纳秒为十进制 Str，缺失源时间保留 Option。图像来源与识别结果共用同一结构，裁剪保留原图偏移；InputReceipt.written_at 仍仅为 Rust Instant。来源重连后旧游标报告 SourceChanged，不悄悄跨代次继续。

按后续用户决定，手柄动作拆成独立的按下、等待与抬起；长按或组合键时序由组合子表达，不属于原子动作。上述原语已通过内建目录和 `PrimitiveHost` 接入现有 `Gamepad` 方法；PadPress／PadRelease 在原语边界拒绝空按钮集合，重复按钮按位处理。基础 Gamepad API 保留允许空集合的独立契约。原语目录已声明提供者／输出所有权和操作完成阶段，派发层检查媒体形状、视频游标前进、识别来源／坐标及评分契约；IR 求值、校验与全部核心组合子执行已实现。

手柄会话由单一所有者持有，动作串行更新，没有后台动作队列或跨运行按键引用计数。按下状态可跨原语保留；RunScope 已提供成功、失败和取消退出时的全中立复位与清理报告，保留外部持有的连接。将手柄加入作用域即授予整个状态的独占控制，退出时即使未执行动作也会复位；不恢复进入时的旧状态。直接使用 PrimitiveHost 的调用方仍须显式 `reset` 或 `disconnect`；取消单次动作不撤销此前已交付的状态。发送第一字节前可取消而不改变状态；一旦开始传输则在事务预算内完成帧和所需应答，返回实际结果，失败则使会话失效。

基础库 `WriteReceipt` 记录连接代次、报告序号、写入时刻及 `Written`／`Accepted` 交付级别，后续映射到 IR 的 InputReceipt。交付级别只证明操作系统写完成或固件确认，不证明游戏已产生预期响应。业务成功仍需显式观测。资源枚举、连接与断开也由基础库提供，具体契约见 [通用手柄原子模块](../nextgen/device/README.md)。

## 7. 执行结果、错误与资源清理

### 7.1 三类结果

| 结果 | 内容 | 恢复行为 |
|---|---|---|
| Success | 类型化输出 | 可写入结果绑定并继续 |
| Failure | ErrorRecord | 仅 Recoverable 错误可被 Retry、Catch、Fallback 处理 |
| Cancelled | 取消原因及清理报告 | 直接传播，不进入普通错误恢复 |

ErrorRecord 至少包含 kind、message、node_id、invocation_id、origin、recovery、cause 和按顺序保留的 suppressed 错误。origin 区分 Expr、Primitive 和 Combinator。ExprError 的具体原因放在 cause 中。原语错误由引擎附加原语名与版本，不压平成字符串。

recovery 分为 Recoverable 和 RunFatal。清理失败、引擎内部不变量破坏属于 RunFatal，不能由用户流程吞掉。InvalidFlow 在运行前拒绝，不产生一条已经执行过的流程。

| 错误类别 | 本版约定 |
|---|---|
| ExprError | 保留 NumericError、IndexOutOfBounds、UnwrapNone 等具体原因 |
| CapabilityUnavailable、SourceUnavailable、SourceChanged | 运行环境与来源问题，默认 Recoverable |
| AudioGap、InvalidArgument、AssetUnavailable、RecognitionError、TransportError | 对应具体原语契约，默认 Recoverable |
| Timeout、MaxRetriesExceeded、MaxIterationsExceeded | 组合子产生的 Recoverable 错误，保留子错误上下文 |
| CleanupFailed、InternalError | RunFatal，禁止继续执行外部动作 |

错误恢复不回滚已经发生的外部效果。动作错误应保留已知的完成阶段；不确定是否已写入时必须明确表示未知，不能声称未执行。

### 7.2 清理

当前 RunScope 是运行级资源边界：拥有专用视频／音频订阅，独占借用手柄，其他提供者只借用。finish 同步收尾并返回 RunCompletion，含最终结果、逐资源 CleanupReport 和被覆盖的候选结果 prior；清理失败优先，其次是已锁存的致命失败，再是取消和普通候选结果。每个手柄用独立且未取消的 token 复位，失败后继续处理其他资源；Drop 仅作忽略结果的尽力收尾。该 API 不创建或收拢外部任务；ir 执行器在其外部用中央调度队列收拢节点、维护取消树、释放子作用域所触碰的按键状态并生成 trace。节点借用运行级订阅，不隐式重建订阅或重置音频游标；完整动作序列应放在同一状态作用域内。调用方须先等待自己启动的工作结束。匹配／OCR 可借用宿主持有的 NativeMatcher／NativeOcr，运行结束不关闭其线程，宿主最终显式 shutdown().await。工作线程初始化、调用与销毁原生状态，取消后等待实际返回；Rust panic 使线程失效并映射为 InternalError／RunFatal。直接提供者须返回 typed 错误，不承诺从原生崩溃、进程中止或卡死驱动中恢复。

节点拥有的任务、订阅和租约在其终止前收拢。Retry 开始下一次尝试、Fallback 执行 secondary、Catch 执行 handler，都以先前作用域已经完成清理为前提。

清理报告逐资源记录 NotRequired、Released 或 ReleaseUnconfirmed，附带资源身份和错误。ReleaseUnconfirmed 不视为 Released；任何应释放但不能确认释放的资源使清理失败。输入资源的 Released 表示释放报告至少已经写入，并记录实际完成阶段；不能据此声称游戏已响应或设备提供了协议确认。报告本身由引擎附加到结果和 trace，不替代业务输出。

正常清理在外部取消后仍执行，不被同一个取消信号跳过。清理失败产生 CleanupFailed；原先的失败、取消原因或候选成功结果作为上下文保留。该 RunFatal 失败优先于普通成功、可恢复失败或取消终态，避免报告已安全停止。

应用长期拥有的共享采集设备由来源管理器释放；运行只释放自己的订阅、样本引用和请求。取消一个运行不关闭其他运行正在使用的来源。

## 8. 组合子精确语义

### 8.1 Eval、Seq 与 Bind

Eval 求值一个 Expr 并返回其值，不调用能力提供者，效应为 Pure。

Seq 按子项顺序执行，遇 Failure 或 Cancelled 即停止。全部成功返回最后一项输出，空 Seq 返回 Unit。Seq 不限制混合效应，各子项的效应进入其摘要。

Bind 求值 expr，在当前顺序作用域绑定 name，再执行 next。Bind 返回 next 的输出；它不额外返回一个 Unit。绑定对 next 和当前 Seq 后续兄弟可见，next 内部新建的局部作用域仍按一般规则退出。绑定必须是新名字。

### 8.2 Par

执行前对全部分支进行递归效应检查，违反 Pure／Meta 限制则拒绝整个 IR，不启动任何分支。

| 模式 | 成功与失败 | 输出类型 |
|---|---|---|
| All | 启动全部分支，等待全部终止；普通分支失败不取消其余分支。全部成功时按分支声明顺序返回结果；存在失败时，以声明顺序中最靠前的失败为主错误，其他失败保留为上下文 | 按分支顺序的 Tuple，允许异型 |
| Any | 第一个被引擎接受的成功成为候选；取消其余分支并等待清理，再返回候选。全部失败时以最后被接受的失败为主错误，按分支顺序保留全部失败上下文 | 所有分支必须具有相同输出类型 |
| Race | 第一个被引擎接受的 Success 或 Failure 成为候选；取消其余分支并等待清理，再返回候选 | 所有分支必须具有相同输出类型 |

空 All 返回空 Tuple；空 Any 和空 Race 属于 InvalidFlow。Any 或 Race 的失败分支清理若发生 RunFatal，覆盖候选结果。任一模式遇 RunFatal 都取消其他分支、等待清理并返回该致命失败。

父取消使全部分支取消，收拢后向上传播。被 Any／Race 主动取消的分支只产生本分支 Cancelled，不改变已选候选，也不触发外层 Fallback。选择规则的并发裁决见第 9 节。

### 8.3 Choose

cond 求值为 true 时执行 then，否则执行 else。实际分支的输出就是 Choose 输出。具有两个分支时两者输出类型必须相同；省略 else 时 then 必须输出 Unit，false 分支返回 Unit。需要可选值时显式让两个分支返回 Option<T>。

只执行一个分支，但两个分支都参与类型和效应检查。cond 求值失败直接向上传播，不尝试任何分支。

### 8.4 Retry

max 是总尝试次数，包含第一次执行，必须至少为 1。retry_on 是显式的可恢复错误 kind 允许集合，不使用隐含的全部错误匹配；只匹配当前 ErrorRecord 的顶层 kind，不递归匹配 cause。空集合合法，表示不重试。节点进入时求值并验证 max、retry_on 和 backoff。

每次尝试使用新局部作用域。成功返回结果；Cancelled 或 RunFatal 直接传播；Recoverable 错误不在 retry_on 中时原样返回。在允许重试且剩余次数大于零时，完成本轮清理，按退避等待，再启动下一次尝试。次数耗尽返回 MaxRetriesExceeded，cause 为最后一次错误，并保留先前尝试记录。

| Backoff | 等待规则 |
|---|---|
| None | 不等待，但向调度器让出执行权 |
| Fixed(ms) | 每次重试前等待相同的非负毫秒数 |
| Exponential | base_ms、factor、max_ms 满足 0 ≤ base_ms ≤ max_ms，factor 为有限数且 ≥ 1；第 k 次重试等待 base_ms × factor 的 k-1 次幂与 max_ms 的较小值，向下取整为毫秒 |

退避计算在达到 max_ms 时饱和，不因中间乘法溢出绕回。退避等待可取消。Retry 不复原外部状态，不自动认定动作可安全重试；前端应保留重复执行的显式意图，运行器不在原语内部再次重试。

### 8.5 Timeout

duration_ms 为非负 Int，进入节点并完成参数求值后，以单调时钟建立截止时间。零时长直接返回 Timeout，不启动 body。

body 在截止前完成执行与必要清理，返回其结果。截止先到时，取消子作用域，收拢任务与资源后返回 Timeout，保存子结果和取消上下文。局部超时只取消该子作用域，不直接取消整个 run。父取消优先向上传播 Cancelled。

超时后的清理失败按第 7 节升级为 RunFatal。嵌套 Timeout 继承更早的父截止时间；父截止引发的取消由父节点包装，不在子节点伪造成另一项局部超时。

截止时间控制何时发起取消，不承诺抢占任意原生调用。Sleep 应能在等待期间响应；不响应取消的原生工作会延长 Stopping 阶段，期间不能报告已经终止。

### 8.6 LoopUntil

至少执行一次 body，每轮开始先检查取消与预算、建立本轮状态，再执行 body。body 失败或取消立即传播，不继续求值 cond。body 成功后按第 5.3 节求值 cond，true 返回本轮输出。

cond 为 false 时才判断本节点 max_iters 是否耗尽，耗尽返回 MaxIterationsExceeded。因此最后一次允许的迭代仍可正常成功。max_iters 若提供必须至少为 1，省略时受运行级迭代预算约束。

可以继续时计算下一轮状态，并向调度器让出执行权。ShouldStop 只读取引擎的运行取消状态，不能在其中隐含执行另一段业务流程。

### 8.7 Pipe

执行 source；失败或取消直接传播。source 成功后建立 sink 子作用域，将输出绑定为 $prev，执行 sink 并返回其输出。两端类型按 sink 对 $prev 的使用进行检查。$prev 不写入调用方作用域。

Pipe 可规范化为词法绑定与顺序执行，但原始节点身份应保留用于 trace 和前端定位。

### 8.8 Fallback

执行 primary，成功直接返回。Recoverable 失败在完成清理后进入 secondary；Cancelled 或 RunFatal 直接传播。secondary 成功返回其值；失败时以 secondary 为主错误，保留 primary 作为上下文。两分支输出类型相同，primary 的局部绑定不进入 secondary。

### 8.9 SubflowCall

求值 target FlowRef 与 args，从左到右验证并绑定到 params，在隔离作用域执行 body。成功输出是调用结果，并可写入调用方的 result_bind。参数和返回值必须满足目标签名。

调用共享 run 的时钟、预算、trace 上下文及继承的取消关系，不共享调用方局部变量。调用前检查深度预算，递归不会建立新的 run 或重置预算。

### 8.10 Catch

body 成功时返回其输出。仅 Recoverable 失败会进入 handler；Cancelled 和 RunFatal 直接传播。body 清理完成后，在 handler 的子作用域绑定只读 ErrorRecord，兼容前端可把它映射成字段 Map。

handler 与 body 输出类型相同。handler 失败时保留 body 错误作为上下文，handler 取消则传播取消。Catch 不回滚 body 已产生的外部效果。

### 8.11 Observe 的定位

Observe 是标准组合流程的名称，v0.1 不新增独立引擎节点。前端或标准流程库将它展开为 LoopUntil、Choose、Sleep、Timeout 等已有节点，并保留来源映射。

其契约是：立即调用 probe，probe 返回 Option<T>；Some 时返回 T，None 时等待 interval_ms 后再次观察。probe 的失败或取消直接传播。interval_ms 非负，零间隔仍让出执行权。指定 duration_ms 时，整个观察过程受 Timeout 约束，循环计入同一运行预算。

Observe 是有终点的等待，不定义无限流算子。其效应继承 probe；包含实时采集时不能进入当前 Par。

## 9. 取消、并发裁决与运行预算

### 9.1 取消树

每次运行具有根取消作用域，组合子为并行分支、超时子流程等创建子作用域。父取消向下传播，子取消不影响父级。CancellationToken 的 clone 只用于同一作用域的多个持有者；作用域隔离使用 child_token。[R1]

| 来源 | 取消原因 | 对外结果 |
|---|---|---|
| 用户停止、进程关闭 | UserStop、Shutdown | 根 Cancelled |
| 外部熔断 | Aborted | 根 Cancelled |
| 运行预算耗尽 | BudgetExceeded | 根 Cancelled |
| 局部 Timeout | DeadlineExceeded | body Cancelled，Timeout 节点返回 Timeout 失败 |
| Any／Race 选出候选 | SiblingCompleted | 剩余分支 Cancelled，组合子按候选结束 |

已提供 RunControl 宿主入口：request_stop(UserStop／Shutdown／Aborted) 先锁存首个外部原因，再通知取消；clone 共享停止状态，每次新运行创建新控制。execute_controlled 与带预算的同名入口保留原因，原 CancellationToken 入口仍将宿主取消解释为 UserStop。引擎内部的 DeadlineExceeded、SiblingCompleted 和 BudgetExceeded 不改写宿主控制；提供者自行取消为 ProviderCancelled。清理期间收到宿主停止时，旧取消作为 cause 保留。CancellationRequested 携带 reason，后续不同来源的停止请求追加事件，不改写已发布记录。

检查点包括节点进入前、参数求值后、原语返回后、每次循环／重试边界，以及一切可等待的休眠、订阅和 I/O 等待期间。CPU 密集或原生调用必须声明实际取消能力，不能仅靠“调用后再检查”宣称快速取消。

取消通知不等于任务结束。Tokio Sleep 可通过丢弃 future 取消，但普通 timeout 不能抢占不让出执行权的工作，已开始的 spawn_blocking 也不能靠 abort 强制终止。丢弃 JoinHandle 会分离任务，因此不符合本规范的任务收拢要求。[R2][R3][R4][R5]

### 9.2 裁决顺序

引擎为接受的运行事件分配单调递增 event_seq，trace 记录该顺序；不把操作系统线程完成时间宣称为确定性执行顺序。

同一次仲裁中，先检查父取消，再检查本节点已到达的截止时间，再接受正常完成结果。接受 body 完成时单调时钟已经达到截止时间，则按超时处理，不直接依赖 Tokio timeout 的边界选择。RunFatal 清理失败按第 7 节优先报告。

Par Any／Race 的第一个结果指引擎接受顺序；同一批已就绪分支按声明索引从小到大处理。事件在不同调度批次到达时按 event_seq 裁决。候选选定后进入清理阶段；父取消若在节点终态前被接受，仍使该节点取消。

已发布的节点终态不被后到取消重写；其父节点仍可在自己的完成检查点观察取消。绑定只存在于所属执行作用域，不代表整个运行已经成功。

### 9.3 预算与公平性

运行级循环迭代预算默认为 1,000,000，所有 LoopUntil 实例和子流程共享，每次开始 body 前计数。超过预算请求根取消 BudgetExceeded，不能由 Catch、Retry 或重新调用子流程绕过。

活动 SubflowCall 深度默认最多 128，入口深度为 0，第一次调用深度为 1；尝试超过上限触发同类预算取消。宿主可以在启动前设置更严格的预算，实际值写入 run trace，运行内不得提高。

每次循环与无等待的重试边界都让出执行权，避免纯计算流程长期占据调度线程。每个原语入口仍需检查取消。

## 10. 内建可观测性

| 身份字段 | 含义 |
|---|---|
| run_id | 一次运行 |
| node_id、node_path | 静态节点与可读结构位置 |
| invocation_id、parent_invocation_id | 一次动态节点执行及其父调用 |
| branch_index | 并行分支声明位置 |
| attempt、iteration | 所属重试和循环上下文，可嵌套记录 |
| event_seq、elapsed_ms | 引擎接受顺序和运行单调时间 |
| source_span | 原始前端位置，若存在 |

所有真正开始的节点产生 NodeStarted，并最终产生且只产生一个 NodeSucceeded、NodeFailed 或 NodeCancelled。未执行的 Choose 分支不伪造开始／终态事件。静态校验失败产生 ValidationFailed，因尚未启动而没有节点执行事件。

引擎还产生 RetryScheduled、CancellationRequested、CleanupCompleted 等语义事件。Expr 不单独构成流程调用，但求值错误必须记录所属节点和表达式路径。Log 原语产生 UserLog，不替代自动 trace。

样本默认记录引用身份、时间、格式与尺寸，不自动复制完整图像或音频。节点和错误事件应保留结构化值的类型及可读摘要。界面预览可以跳帧，不应把预览丢弃解释为流程 trace 丢失。

## 11. 序列化、版本与验证

v0.1 采用带显式类型标签的 JSON IR。Unit、None、空 List、空 Tuple 使用不同标签；Int 用带类型标签的十进制字符串保存，避免 TS 数字往返损失 64 位整数精度。Float 使用有限 JSON 数值，负零规范化为零。

记录值的字段、定义集合等无序项在规范化输出中按名称排序；流程子项、Tuple、List、错误上下文等有序项保留顺序。记录构造表达式的初始化项本身是有序序列，不能按字段名重排，以免改变第一个求值错误。规范化不重排有副作用节点，也不求值时钟或设备能力。

静态 IR 不序列化活动 FrameRef、AudioWindowRef、设备句柄、取消 token 或闭包。FlowRef 仅保存已验证定义身份和签名引用；SourceRef 保存类型与绑定名，实际能力在调用时解析，缺失可由 Fallback 处理。模型由上层经配置装配，模板可以作为显式图像运行参数传入，不序列化活动原生对象。

版本不匹配、未知执行节点／函数／原语契约、非法保留字段均拒绝执行，不静默跳过。扩展注解只允许位于 annotations 下。校验阶段验证 schema、引用、类型、作用域、控制参数常量约束、结果绑定和递归效应，然后才能启动任何能力操作。

序列化往返的等价指执行结构、类型、字面量、节点身份、来源映射和顺序相同，不要求原始 JSON 空白或字段排列相同。

## 12. 原始感知的资源与验证边界

来源管理器拥有物理采集会话，IR 运行取得逻辑订阅；视频保留最新样本供订阅读取，音频采用有界缓冲和独立订阅游标。慢订阅不得导致无限积压，视频允许覆盖旧帧，音频缺口必须显式报告。

实时采样数据由 Rust 持有，TS 只接收预览和状态。关闭预览不应取消仍被运行使用的采集来源；退出应用则取消运行、释放订阅，再关闭物理采集会话。

三平台目标不等于已经验证每个采集卡和格式。实际设备枚举、输入格式、重连、取消延迟和原生资源释放必须按平台验收。v0.1 不承诺跨设备硬件同步，也不把宿主接收时间当作硬件采样时间。

## 13. 语义验收清单

以下为完整 IR 实现必须覆盖的验收场景。核心组合子、流程引用和子作用域已有实现及 60 项 IR 测试；仍未完成真实硬件、三平台及前端／trace schema 等完整验收，覆盖范围见 [原语完成度 §5](NextGenPrimitivesStatus.md#5-本次验证与设计验收映射)，应区分已覆盖的执行语义与未验收的平台／前端边界。

| 编号 | 场景 | 预期 |
|---|---|---|
| V01 | 空 Seq、空 Par All | 分别返回 Unit、空 Tuple，类型不同 |
| V02 | 空 Par Any／Race | 执行前 InvalidFlow，无能力调用 |
| V03 | Seq 中某项失败 | 后续项未开始，错误来源保留 |
| V04 | Bind 与后续兄弟、内层同名遮蔽 | 顺序可见性正确，退出子作用域后恢复父绑定 |
| V05 | 分支局部名字被外部引用 | 执行前拒绝，不依赖实际分支选择 |
| V06 | Retry 第二次成功 | 第一轮局部绑定和订阅不保留，输出来自第二轮 |
| V07 | Par All 中一项先失败、一项稍后成功 | 等待全部；按声明顺序选择主错误 |
| V08 | Par Any／Race 选出候选 | 取消其他分支，收拢后终止，没有分离任务 |
| V09 | 两个不同类型的 Par All 分支 | 得到按位置类型化的 Tuple |
| V10 | Par 间接调用含 Capture 的子流程 | 递归效应检查拒绝，即使藏在 Catch 或未选分支中 |
| V11 | 动态 FlowRef 违反签名／效应上界 | 启动前拒绝，不信任前端摘要 |
| V12 | Choose 无 else 且 then 返回非 Unit | 类型检查拒绝；Option 返回必须显式构造 |
| V13 | Retry max=1、错误不在 retry_on、退避溢出 | 总尝试次数精确；非匹配错误透传；退避在上限饱和 |
| V14 | Timeout 包裹长时间 Sleep | 等待期间取消，耗时接近截止时间加调度开销，不等待完整 Sleep |
| V15 | Timeout 包裹不可快速取消的原生工作 | 显示停止中；清理结束前不报告终态，不承诺硬截止 |
| V16 | 父取消与局部超时／正常完成同时就绪 | 按仲裁规则选出唯一终态，trace 可追踪 |
| V17 | Catch／Fallback／Retry 收到根取消 | 不进入 handler、secondary 或下一次尝试 |
| V18 | 最后一次允许的循环满足条件 | 成功优先，不误报 MaxIterationsExceeded |
| V19 | 循环状态累积与条件求值 | $state、$result、$iteration 作用域和更新顺序正确 |
| V20 | 嵌套循环、递归调用 | 共享迭代预算；调用深度受限；不能通过恢复组合子绕过 |
| V21 | 嵌套 Pipe | $prev 词法遮蔽，退出后不泄漏 |
| V22 | 子流程调用后访问其局部变量 | 执行前拒绝，仅显式输出可绑定 |
| V23 | 短路右侧含 unwrap(None) | 右侧未求值时不报错，真正求值时可由 Catch 处理 |
| V24 | 多字节字符、组合字符、Int 极值、混合数值 | len 按标量值；溢出报错；无隐式提升 |
| V25 | 同一帧做模板匹配和 OCR | 明确消费同一帧身份，不再次隐式采集 |
| V26 | 模板低评分、OCR 空文本、音频静音 | 正常结果；分别区别于资产缺失、来源断开或音频缺口 |
| V27 | 视频重连、音频慢消费者 | 代次变化显式报错；音频缺口可见，不冒充连续样本 |
| V28 | 动作过程中取消、清理失败 | 释放本次持有状态；失败升级 RunFatal，不能被 Catch 吞掉 |
| V29 | 相同静态节点被循环／重试／递归执行 | invocation_id 不同，节点开始与终态一一对应 |
| V30 | IR JSON 往返、超出 JS 安全整数范围 | 保留完整 Int、类型、节点与顺序信息 |
| V31 | 未知版本、节点、重复绑定或错误类型的能力引用 | 校验阶段拒绝，没有外部副作用 |
| V32 | Observe 首次成功、持续 None、probe 失败 | 分别立即返回、等待并受预算／超时控制、透传错误 |
| V33 | 应用关闭、预览关闭、取消一个订阅运行 | 资源所有权正确，共享来源不被无关订阅释放 |

## 14. 设计依据与实现约束

上述 IR 与组合子语义是本项目的设计决定。下面的官方资料只约束 Tokio 实现行为，不意味着 Tokio 自动提供了本规范的作用域、清理、trace 或截止裁决。

- [R1 CancellationToken](https://docs.rs/tokio-util/latest/tokio_util/sync/struct.CancellationToken.html)：父子取消、clone 与 child_token 的传播关系。
- [R2 Tokio Sleep](https://docs.rs/tokio/latest/tokio/time/fn.sleep.html)：异步等待、取消与定时分辨率限制。
- [R3 Tokio timeout](https://docs.rs/tokio/latest/tokio/time/fn.timeout.html)：非让出工作不能被强制抢占，边界选择需按 IR 规则自行仲裁。
- [R4 spawn_blocking](https://docs.rs/tokio/latest/tokio/task/fn.spawn_blocking.html)：已经开始的阻塞任务不能通过 abort 强制取消。
- [R5 JoinHandle](https://docs.rs/tokio/latest/tokio/task/struct.JoinHandle.html)：丢弃句柄会分离任务，执行器必须显式收拢其工作。

## 15. 后续设计顺序

先以本文定义的类型、作用域、结果和效应契约作为 IR 设计基线，继续细化视频／音频来源描述、支持格式和会话状态机。当前已有内建原语目录、类型化单次派发、mock 端口与原始感知诊断入口；运行作用域、Value／Expr、流程引用、调用图效应、全部核心组合子与子作用域清理已实现；后续补齐前端来源映射和 trace schema，再将 ECS 语法映射到统一 IR。工作流资源描述与可选摘要由上层负责。

首批可独立使用的成果是 [nextgen](../nextgen/README.md) 无界面 Rust 核心与诊断入口，具备来源枚举、视频帧和音频窗口交付、来源错误报告与资源释放、通用串口手柄动作，区域 RGB 统计与显式双图差异、可替换的模板匹配接口与原生 OpenCV >= 5 动态后端、Tesseract 与 PP-OCR 单行后端，以及 Sleep／ClockNow／Log。原语入口已统一，待完成真实设备和三平台验收；IR 核心已实现，ECS 前端和 Tauri 界面仍未实施。后续界面消费同一核心，无须先重写旧编译器或全部识别能力。

当前另有 [easycon-run 项目宿主](../nextgen/project/README.md)，装配视频／OpenCV 模板匹配／串口手柄／Meta 并执行项目 IR。原生库从应用旁加载，串口按名称自动探查；`workflow.json` 自动读取同路径的简化 `config.json`；识图流程扫描 `imglabel/*.il`，保持旧 EasyCon `.IL` 的 Base64 图片、ROI 坐标和搜索方法内容，标签解析为 `{ image, config }` 入口参数，其他独立资源和配置归 `resources/`，阈值直接在工作流中表达。项目预检复用引擎入口参数校验，运行清理后再关闭物理资源；Tesseract／PP-OCR 分别声明并绑定 models/ 下的工作流模型，音频暂未暴露到此 CLI 配置。
