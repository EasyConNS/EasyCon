# 编排 IR 与组合子

状态：2026-10-24，已实现 [IR v0.1 规范](../../docs/NextGenIR.md) 的全部核心组合子与流程引用。JSON 文档经静态校验后才能执行，18 项原语均已有 IR 参数／输出适配；本层复用原语宿主和 RunScope，不重新实现设备协议或识别算法。

## 使用

在 nextgen 目录执行：

```sh
cargo run --locked --bin easycon-ir -- validate examples/composition.ir.json
cargo run --locked --bin easycon-ir -- run examples/composition.ir.json
cargo run --locked --bin easycon-ir -- run examples/combinators.ir.json
```

[示例文档](../examples/composition.ir.json) 顺序绑定 count=40、条件选择、Pipe 加二，结果为 Int(42)。CLI 装配 Sleep、ClockNow 和控制台 Log，只执行无参入口；不会自动打开硬件。Ctrl+C 请求取消后继续等待执行与清理完成。

[完整组合示例](../examples/combinators.ir.json) 也返回 Int(42)：外层 Catch／250ms Timeout 包住 Par Any。快分支等 5ms，先演示零时长 Timeout；Fallback 接着通过 Retry 调用 `sum(limit=3)`，LoopUntil 用 `$state + $iteration` 得到 1+2+3=6，再由 Pipe 乘以 7。慢分支等待 10 秒；快分支成功后它被取消并收拢，`slow-result` 不执行。Retry 此次首轮成功，因此不会实际退避；只有整个 body 可恢复失败时 Catch 才返回 -1。

Rust 宿主使用 `Document::from_json(...).validate()?` 得到不可修改的 ValidatedDocument，再调用 `execute(bindings, params, &parent).await`。可编译 API 示例见 [ir 模块](../src/ir.rs)。宿主需要区分停止来源时，使用 `RunControl` 和 `execute_controlled`／`execute_controlled_with_limits`，见 [停止控制示例](../src/ir/cancellation.rs)。绑定与订阅的装配方式见 [运行作用域](../run/README.md)。运行参数通过 BTreeMap<String, Value> 显式传入，入口参数名称、类型和集合形状在资源加入 RunScope 之前检查。

```text
JSON → Document → validate → ValidatedDocument
                                  ↓ execute
                  Expr / 组合子 / 自动 trace
                                  ↓
                   RunScope → PrimitiveHost
                                  ↓
                        原有原子能力提供者
```

## 已实现的节点

| flow.kind | 行为 |
|---|---|
| eval | 返回 Expr 的值 |
| primitive | 参数从左到右求值，按精确名称和版本调用现有原语 |
| seq | 创建顺序作用域；失败／取消时停止；空列表返回 Unit，其他返回最后一项输出 |
| bind | 在当前作用域绑定不可变名字，再执行 next；名字对当前顺序作用域后续兄弟可见 |
| choose | 只执行选定分支，分支具有子作用域；两分支输出同型，缺少 else_ 等同返回 Unit |
| pipe | source 输出在 sink 子作用域中绑定为 $prev；嵌套时按词法关系遮蔽 |
| subflow_call | target 为 Expr 求得的 FlowRef；args 是有序 NamedExpr；参数独立绑定，不捕获调用者局部名字 |
| par | All 返回按声明顺序的异型 Tuple；Any 选首个成功；Race 选首个完成；选定后取消并收拢其他分支 |
| retry | max 是总次数；retry_on 显式匹配顶层错误 kind；每轮局部作用域／清理隔离，支持 None／Fixed／Exponential 退避 |
| timeout | 非负时长只求值一次，0 不启动 body；父取消优先于本地截止，截止后取消、等待退出并清理 |
| loop_until | 至少执行一次；$state／$iteration 在 body 中可见，$result 只供 cond／state_next；最后允许一轮满足条件仍成功 |
| fallback | 仅可恢复失败进入 secondary；输出同型，secondary 失败保留 primary 原因 |
| catch | 仅可恢复失败进入 handler；可绑定类型化 ErrorRecord；取消和 RunFatal 绕过恢复 |

所有节点均有文档内唯一 node_id 和可选 result_bind。节点成功后将结果写入调用方作用域；失败／取消不绑定。Seq 的内部绑定不向外泄漏；Choose 的分支绑定不泄漏。Pipe 的 source 按调用方当前作用域执行，sink 的绑定隔离，$prev 不泄漏。普通绑定不能使用美元符号前缀。

Observe 由 `ir::observe(prefix, probe, element, interval_ms, duration_ms)` 降低到上述已有节点；没有独立 Observe 执行节点。首次立即 probe，Some 直接解包，None 等待后重试；零间隔让出执行权，负间隔在 probe 前拒绝。循环共享运行预算，probe 失败直接传播。

`retry_on` 为 `List<Str>`，例如 `CapabilityUnavailable`、`SourceUnavailable`、`ExprError`、`Timeout`，只允许已登记的可恢复种类；`Cancelled`、`BudgetExceeded`、`CleanupFailed` 和 `InternalError` 不能列入。控制参数、退避的 ms／base_ms／factor／max_ms 都是 Expr，factor 要求 Float，其他时长／次数要求 Int。

## 值与表达式

支持 Unit、Bool、Int、Float、Str、同型 List、异型 Tuple、Record、带元素类型的 Option、FlowRef、ErrorRecord，以及 Source／Symbol 领域类型。Frame、AudioWindow、Image、SampleId 和 InputReceipt 只作为运行期类型化值存在，不序列化到静态文档。帧和窗口共享不可变样本；动作回执保留 Written／Accepted、会话、序号和真实 written_at。

Int 是 i64，JSON 必须使用规范十进制字符串；拒绝数值编码、前导零和越界值。Float 必须有限，负零规范化为零；运算溢出、除零、解包 None、越界索引均保留错误种类及表达式路径。Int 与 Float 之间只允许显式 Convert；Bool 不做真假值转换。Str 长度按 Unicode 标量值计算。

Expr 支持字面量、变量、字段、索引、有序记录初始化、List／Tuple／Some／None 构造、一元与二元运算、转换及版本化纯函数调用。and／or 短路；in 对字符串做子串检测，对同型可比较 List 做成员检测。Tuple 索引当前要求 Int 字面量；List 允许运行期 Int 索引。

纯函数版本均为 1：len、contains、starts_with、ends_with、abs、min、max、is_none、is_some、unwrap。另提供显式领域转换 `image_from_frame(Frame) -> Image`，保留帧身份并共享 RGB 缓冲。now_ms 不在纯函数表内，读取时钟必须使用 ClockNow 原语。

Expr 的样本字段如下；全部只是已交付数据的投影，不再次访问设备或读取当前时间：

| 类型 | 字段 |
|---|---|
| Frame | id、width、height、time、format |
| AudioWindow | id、frames、channels、sample_rate、time |
| Image | width、height、bounds、source |
| SampleId | source、session、sequence |
| InputReceipt | session、sequence、delivery |

time 是 `{ received_ns: Str, source_ns: Option<Str> }`；u64 纳秒与身份分量使用十进制 Str，保留完整范围。None 与 Some("0") 不混淆。format 是 `{ width: Int, height: Int, frames_per_second: Int, encoding: Str }`，encoding 保留输入格式，帧像素始终为 RGB8。AudioWindow 已归一化为交错 f32，原始设备 sample_format 仍只在 AudioSession 上，不伪造到窗口。

Image.bounds 是原图坐标 Rect，嵌套 crop 保留绝对偏移；source 为 `Option<{ id: SampleId, width: Int, height: Int, received_ns: Str, source_ns: Option<Str> }>`，与识别／统计结果中的 source 使用相同形状。文件／合成图像返回 None。图像来源记录的尺寸是原帧尺寸，可能不同于当前 ROI。InputReceipt.written_at 仍仅保留 Rust Instant，不投影成不存在的通用时间戳。样本时钟与运行 ClockNow 原点不同，不可直接混算。

`Expr::FlowRef { definition }` 只引用本文件已验证的定义。`Type::FlowRef(FlowSignature { params, output, effects })` 声明参数／结果签名和效应上界；签名必须一致，实际效应只能比上界更小。`ValidatedDocument::flow_ref(id)` 铸造带本次校验身份的运行值；入口集合内部的外来文档引用也会被递归拒绝。可绑定、传参和返回 FlowRef，任意子流程位置均可放置零参数 SubflowCall；不能通过字符串或闭包构造运行流程。

ErrorRecord 可投影 kind／message／node_id／invocation_id／origin／recovery／cause／suppressed／cancel_reason，以及 expression_kind／expression_path／primitive_name／primitive_version。可选身份与细节为 Option；cause 为 Option<ErrorRecord>，suppressed 为 List<ErrorRecord>。Rust FlowError 另外保留 typed ExprError、Arc<PrimitiveError> 和已交付的候选值。

## 原语适配

Primitive 节点使用 name、version、args；args 是有序的 NamedExpr 列表。参数名称与现有目录精确匹配；必须显式给出全部参数，包括构造 None 的 after／region。参数类型由原语目录词汇映射，新增或未知词汇不会默认为任意类型。

SourceRef 包含 kind 与 binding：video、audio、gamepad、text_input、template_matcher、ocr 各自独立。静态检查种类与非空名字；实际提供者在调用时解析，缺少能力为运行错误。SourceRef 是符号绑定，不是稳定硬件 ID。

按钮、方向帽、摇杆编号、匹配模式、OCR 种类及日志级别使用带 SymbolKind 的 Symbol；未知名字或错误种类拒绝。Rect／Stick／Report／MatchConfig 使用固定字段记录；Log fields 使用平面标量记录，Unit 映射日志 Null。

匹配、OCR、区域统计和图像差异返回完整记录，可通过 Field／Index 消费坐标、confidence、文本、boxes 及来源，保留 None 与低评分的区别。模型、字典、模板与后端由上层工作流选择并经可信宿主装配；每个提供者实例内配置固定，核心 IR 不要求 assets 字段或强制内容摘要。TypeText 的 IR 入口不代表已经提供真实后端。

## 静态检查与运行结果

执行前检查版本、所有定义、全局 node_id 唯一性、参数签名、函数／原语版本、同型约束、字段／索引类型、确定赋值、重复绑定、分支作用域和声明输出类型。即使分支不会被选中也必须合法。对可求值的常量、Bind／result_bind 和 Pipe 常量传递做检查，已知负时长、空按钮等在启动前拒绝；运行参数导致的同类错误在运行时报告。

效应摘要递归合并全部分支，通过调用图有限格固定点覆盖相互递归的定义；不会按常量条件删除未选分支。动态 FlowRef 按声明的效应上界检查，Par 拒绝任何直接或间接的 Effectful。空 All 返回空 Tuple，空 Any／Race 拒绝；Any／Race 输出同型。All 普通失败等待其余分支，按声明索引选择主错误；Any 全失败使用最后接受的错误。同批就绪按声明索引裁决。

结构深度上限为 128（节点、Expr、类型分别检查），文档节点上限 100,000。运行默认共享 1,000,000 次 LoopUntil 预算与 128 层活动调用深度（入口为 0）；`execute_with_limits` 允许更严格限制，超过默认值会收紧到默认上限。超限触发根 BudgetExceeded，恢复组合子不能绕过，实际预算写入 trace。

execute 返回 Execution，包含 FlowOutcome、逐资源 cleanup、按发生顺序保存的 child_cleanup、被覆盖的候选结果 prior 和 trace。表达式错误保留节点身份、路径和 ExprError；原语错误保留契约名／版本和 Arc<PrimitiveError>；包装失败保留 cause 和有序 suppressed。取消检查覆盖节点进入、参数求值后和节点完成；每个已开始节点还让出一次调度权。取消后仍执行运行级清理，清理失败优先，已提交动作的候选回执不会被丢弃。

自动 trace 记录 run_id、node_id、node_path、invocation_id、parent_invocation_id、event_seq、elapsed_ms 和可嵌套的 Branch／Attempt／Iteration 上下文。RetryScheduled／BranchAccepted／ScopeCancellation 记录调度裁决与取消原因。`RunControl::request_stop(UserStop | Shutdown | Aborted)` 的首个请求锁存原因，clone 共享控制；新的运行应创建新的控制。CancellationRequested 带 reason，ScopeCancellation 和终态错误保留实际原因。原有 CancellationToken 执行入口继续按 UserStop 解释宿主取消；提供者自身取消单独标为 ProviderCancelled。每个已开始节点只有一次终态；未选分支不产生事件。入口节点的终态在 CleanupCompleted 之后发布，使清理失败反映为入口失败。错误 trace 只保留类型、错误元数据和候选类型，不保存媒体载荷。实际候选值仍可从 Execution／FlowError 检查。日志保留原有 sink 交付，另生成 UserLog 消息事件。

## 资源与调度边界

执行器采用本地中央队列轮询节点，子节点通过结果通道返回，不递归轮询整条调用栈。Par 分支真正并发推进异步等待，不创建分离任务。RunScope 由 Mutex 保护以满足现有可变借用端口，提供者调用期间持锁；Sleep 不占该锁。匹配／OCR 可装配 [托管提供者](../native-work/README.md)，原生工作在专用线程执行，定时器和取消仍能推进；同一次 IR 运行中的能力调用仍串行。直接装配同步后端仍会阻塞调用线程。原生调用不被强制抢占。

Retry 每次尝试、LoopUntil 每次迭代、子流程调用、恢复分支和 Timeout body 建立独立词法／取消／按键状态作用域。子作用域触碰的手柄在其终态前全中立复位，成功也释放；清理失败锁存为 RunFatal，禁止后续恢复。需要持续按住的 press／sleep／release 应放在同一个作用域内部。并不回滚此前产生的外部效果，也不恢复进入时的按键状态。

节点的感知请求在返回前收拢；视频／音频订阅在 RunBindings 中由整个运行持有，子节点借用该订阅，不隐式新建订阅或重置音频游标。运行结束才销毁订阅，外部共享物理会话和手柄连接继续由宿主持有。每次 child_cleanup 可与有节点／invocation 身份的 CleanupCompleted 事件按发生顺序对应；运行收尾另记 cleanup。清理使用未取消 token。

Timeout 到期先取消并等待 body，必要清理完成后才发布终态；父取消优先，本地截止不传播到根。内部截止、兄弟完成和预算耗尽不会取消宿主持有的 RunControl，也不写入其外部停止原因。清理期间新到的宿主停止仍参与终态裁决；若覆盖此前的提供者取消，原取消保留为 cause，trace 保留两次请求的实际顺序。嵌套 Timeout 继承更早截止，父截止不被重复包装。RunFatal 优先于普通候选，节点终态后到的取消只影响尚未结束的祖先。原生调用或提供者忽略取消时，停止耗时可以超过 duration_ms。

## 尚未实现的规范部分

核心组合子已齐；ECS 前端、动作失败交付进度、source_span、annotations、验证失败 trace 与稳定的 trace 持久化 schema 仍待实现。静态 JSON 尚未加入 annotations，未知字段继续拒绝。工作流资源描述与可选摘要由上层运行包负责，不作为核心 IR 必填 schema。

物理来源和模型在执行之前由可信宿主装配；TypeText 仍无真实后端。已用真实模型验证托管 OCR／匹配的复用与关闭；真实硬件和远端三平台 CI 尚未验收。提供者应返回 typed 错误；托管线程捕获 Rust panic 并升级为 RunFatal，不保证从原生崩溃、进程中止或卡死驱动中恢复。

已有 60 项 IR 测试，见 [基础测试](../src/ir/tests.rs) 与 [组合子测试](../src/ir/tests/combinators.rs) 及 [停止与元数据测试](../src/ir/tests/runtime_contract.rs)；全量结果与设计差距见 [完成度核对](../../docs/NextGenPrimitivesStatus.md)。
