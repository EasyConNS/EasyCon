# EasyCon dev 维护边界与 Maa v2 设计大纲

> **状态：部分实施（dev 两项近期修正已实施；Maa v2 未实施）**
> 本文是对 `docs/`、当前源码和既有 Maa 方案的合并核对。它规定 dev 继续维护的边界，并把新的 Maa 自动化设计放入 v2；不会把本文中的 v2 目标误当成当前代码已经完成。第 3 节的两个 dev 修正已在 2026-10-24 落地，下面的验收矩阵仍作为回归约束。
>
> 基线：EasyCon dev 当前结构，桌面端为 PC，单片机端以 C VM 为实际执行内核。PC 上运行 C VM 只是 MCU 内核的模拟和对拍工具，不算第三个正式执行端。

## 1. 结论和优先级

开发者的取舍可以归纳成三句话：

1. **dev 继续维护现在的 ECS、模块编译、能力端口、Flow、GUI/CLI 和 Maa 适配层。** 不做现有 ECS 到 Maa Pipeline 的整体改写，也不以新 Pipeline 取代现有 Flow。
2. **Maa 的新设计进入 v2。** v2 可以使用 Maa 的 Tasker/Pipeline 骨架，也可以把完整 ECS 暴露为一个 `EasyCon.RunEcs` CustomAction；它们都是编排层能力，不改变已有 ECS 文件。
3. **本轮 dev 已闭合两个明确缺口：** 编译出现错误时形成硬闸门，阻止模块链接和后续产物；GUI 的“编译固件”复用 Distributable 编译链生成 ECX，再交给现有烧录/固件封装流程。

因此，之前方案中“立即重建完整 PC 高层执行器、同步重建手柄和采集模块、把全部 Flow 迁到 Maa Pipeline”的内容，统一降级为 v2 设计和研究项。它们不能再作为 dev 的前置条件，也不能阻塞现有脚本、GUI 和 CLI 的维护。

## 2. 当前实现核对

### 2.1 已经成立的基础能力

| 能力 | 当前落点 | 结论 |
|---|---|---|
| ECS 语言 | `EasyCon.Script`、`docs/Script.md`、`docs/Functions.md` | 已是产品主入口。按键、`WAIT`、`@标签`、FRAME/ROI/OCR、函数返回值、数组、结构体和 FFI 语义继续保留。 |
| 统一编译链 | `Compilation` → `ProjectCompiler` → `ModuleCompilePipeline` → `EcxPipeline.Link` | 当前 ECX1/v3 平铺产物是 PC 与 MCU 共用的字节码格式；不要另起一套 ECS 编译器。 |
| PC/MCU 关系 | `EasyCon.Core.Runner.EcxVm`、`src/EasyCon.Vm/native/ecs-vm` | 正式对拍对象是 PC 桌面执行和 MCU C VM。PC 直接执行高层 IR 属于 v2 的优化方向；MCU 仍以 C VM 执行 ECX。 |
| 手柄输入 | `EasyCon.Device`、`PadSession`、`MaaControllerBinding` | 手柄模块拥有串口会话和端口选择。Maa CustomController 只做适配，不拥有串口生命周期。 |
| 视频采集 | `EasyCon.Capture`、`FrameHub`、`FrameStore` | 采集源拥有来源选择和单生产者。识别、脚本 FRAME/ROI、OCR 取图、监视器和预览应从同一帧源取数据。 |
| 音频采集和标签 | `WasapiAudioCapture`、`AudioMatchMonitor`、`AudioLabelService`、`AUDIO_LOAD/SCORE/UNLOAD` | “未来扩展音频基础能力”已经部分落地。v2 的工作是把音频纳入统一信息节点和来源契约，不应重新规划一遍采集和标签基础功能。 |
| Flow | `FlowNodeCatalog`、`FlowNodeRuntime`、`FlowExecutor`、`FlowServiceState` | 现有 Flow 已有控制流/数据边、看门狗、停止、慢感知和 `script.run`。它是可维护的现状，不是待删除的 v1。 |
| Maa 标签试点 | `MaaLabelConverter`、Maa OCR/视觉适配和资源预检 | 现有 ECS 标签仍是源格式；转换产物是后端资源，不要求用户改标签或改脚本。 |

### 2.2 当前 Flow 已经覆盖的范围

现有节点目录与运行时已经形成信息、决策、执行三层的雏形：

- 信息：`capture.frame`、`image.load`、`ocr.text`、`vision.changed`。
- 决策：`compare`、`text.contains`、`state.step`。
- 执行：`wait`、`pad.key`、`pad.sequence`、`script.run`。

`vision.label`、完整的 `ocr.screen`、动态 `switch`、`ai.agent` 和子图仍是挂账项。它们应在 v2 设计接口后逐个补齐，不应在 dev 中通过复制另一套 Pipeline 运行时来解决。

### 2.3 手柄和采集源的独立性

这两个对象必须在模型、状态和界面上保持平级：

```text
PadModule
  ├─ 枚举/选择串口或其他控制端口
  ├─ 建立 PadSession
  └─ 对外提供 IPadInput

CaptureModule
  ├─ 枚举/选择摄像头、采集卡、OBS/OpenCV 来源
  ├─ 建立 CaptureSession / FrameHub
  └─ 对外提供 ICaptureSource

MaaAdapter
  ├─ CustomController → IPadInput
  └─ 截图/识别回调 → ICaptureSource / FrameHub
```

选择手柄端口不应改变采集源，选择采集源也不应重置手柄。连接状态、断开、重连、错误和释放各自归属本模块。Maa 适配器只取得运行期借用的端口，不能把“连接设备”做成一个同时控制两者的总开关。

同样的规则适用于音频：音频端点独立选择，不能因为切换视频源而重新选择麦克风或 WASAPI 端点。

### 2.4 1080p 是现有标签的几何事实

现有标签的内部坐标按 **1920×1080** 解释。历史调研中出现的 720p 表述已被纠正；将这些 ROI 当成 1280×720 会改变标签含义。转换器、`FrameHub` 的规范化和 v2 Pipeline 的 ROI 数据契约都必须以 1920×1080 为基准，再由采集后端统一缩放或拒绝不兼容的来源。

## 3. dev 近期只做的两个修正（已实施）

2026-10-24 已完成以下闭环：编译器在有错误级诊断时不进入链接并清空可运行镜像；链接异常转换为结构化诊断并稳定去重；CLI 与 GUI 共用 `EcxExportService` 生成 Distributable ECX；GUI 编译失败时不返回产物，固件流程继续使用临时文件原子替换。新增的脚本编译、ECX 导出和 GUI 构建测试覆盖这些不变量。

### 3.1 编译错误形成硬闸门

实施前源码已经有一部分短路逻辑：

- `ProjectCompiler.CompileCore` 在主树语法错误或模块图错误时返回；
- `ModuleCompilePipeline.CompileAll` 在模块失败时返回 `false`；
- `ProjectCompiler` 的 `EcxPipeline.Link` 位于 `CompileAll` 成功之后。

本轮已把“不链接”做成可测试的不变量，并按以下规则处理诊断重复：

1. **进入链接前只检查错误级诊断。** `ProjectCompiler` 在链接前再次检查 `Diagnostics.Any(d => d.IsError)`；存在错误时保持 `Image = null`、`Success = false`，不调用 `EcxPipeline.Link`，不写 ECX，不替换上一次有效输出。
2. **链接异常转换成唯一的链接诊断。** 结构体布局不一致、导入索引异常等真正的链接错误不能逃逸成未分类异常；转换为 `phase=link` 的结构化诊断，结果仍为失败。
3. **区分根错误与级联错误。** 解析、绑定、SSA、编码、缓存错误是根诊断；依赖失败只产生一条“依赖模块失败，已跳过”的摘要，并带 `relatedTo` 根诊断。不能让一个语法错误继续触发多轮未定义符号、入口缺失和链接错误。
4. **在诊断袋中去重，而不是只在 GUI 端截断。** 去重键至少包括 `phase + code + file + span + message`。CLI、GUI、LSP 和测试读取同一份去重后的诊断。现有 GUI 的 `DistinctBy(...).Take(8)`只能算显示兜底，不能代替编译器的错误模型。
5. **警告继续允许链接。** `MD_AMBIGUOUS_EXPORT` 等警告不应被误判为错误；有警告但无错误时仍生成正常 ECX。

期望结果是：一行语法错误显示一条根错误，必要时附一条依赖跳过摘要；不会因为同一根因再打印四五条“链接失败”。独立文件中的独立错误仍应保留，不能为了安静而只保留第一条。

建议的最小测试矩阵：

| 场景 | 期望 |
|---|---|
| 主脚本一处语法错误 | `Image = null`，链接器未调用，无 ECX 输出；诊断按文件/行/列稳定且无重复。 |
| `lib/a.ecs` 绑定错误，`main.ecs` 依赖它 | 根错误一条，main 只显示一条依赖跳过摘要；不出现入口缺失风暴。 |
| 两个无关模块各有错误 | 两个根错误都保留，各自只出现一次。 |
| 只有警告 | 正常链接并生成 ECX，警告仍可见。 |
| 链接阶段真实错误 | 只生成一条链接错误，不产生部分 ECX，不污染上一次有效文件。 |
| 修复后再次编译 | 新的成功结果完全替换失败状态，缓存错误侧车不被当成成功产物。 |

### 3.2 GUI 的“编译固件”连接到生成 ECX

实施前调用链已经指出缺口：

```text
FlashFirmwareViewModel
  ├─ CompileFlashAsync / GenerateFirmwareAsync
  ├─ IScriptService.CompileAsync(editorText, scriptPath)
  └─ IScriptService.BuildAsync(...)
       └─ 当前抛出 NotImplementedException
```

这不是要恢复一个旧的 v1 `Assemble`；正确做法是复用当前 dev 的统一编译链：

1. `CompileAsync` 保存本次编辑器源码快照、入口路径和标签外部变量集合。未保存编辑器内容也要按当前文本编译；入口路径仅用于 `IMPORT`、`lib/` 和标签目录解析。
2. 新增 Core 侧的 `EcxExportService`（名称可以调整），唯一职责是以 `ScriptCompileProfiles.Distributable` 编译项目并调用 `EcsContainer.WriteImage`。CLI `compile` 和 GUI 都调用它，不让 GUI 复制模块编译逻辑。
3. `BuildAsync` 改为调用该导出服务，删除“v1 Assemble 已移除”的用户可见错误。已有 `CompileFlashAsync` 和 `GenerateFirmwareAsync` 可以继续保留，只替换其字节码来源。
4. 生成失败时不生成、不覆盖 ECX，也不进入 HEX 封装或串口烧录。错误显示编译诊断或导出诊断，不再显示已经删除的架构名。
5. 成功时先写临时文件再原子替换；生成的 ECX 字节必须与 CLI `compile` 对同一源码、同一标签集合生成的字节一致。`stripDebug` 应成为明确选项，不再用难以理解的 `autoRun` 布尔值承载两个语义。
6. “生成固件”仍可把同一份 ECX 作为脚本段写入现有固件模板；“编译并烧录”仍调用现有 `Flash` 协议。该修正不等于本次就实现 MCU 能力查询、容量预检或新的烧录协议，那些仍属于 `McuBytecodeDelivery.md` 的后续阶段。

GUI 验收应至少检查：

- 编辑器内容有未保存修改时，生成内容来自编辑器而不是磁盘旧文件；
- 同一文件由 CLI 和 GUI 生成的 ECX 容器可逐字节比较（若调试段策略不同，先固定同一 `stripDebug` 选项）；
- 编译错误时不会留下新 ECX、HEX 或烧录动作；
- 已连接/未连接手柄只影响烧录动作，不影响离线生成 ECX；
- 日志使用“编译失败：文件:行:列: 原因”或“ECX 导出失败：原因”，不再出现“v1 已删除”。

## 4. Maa v2 的设计边界

### 4.1 v2 目标

v2 提供 EasyCon 特化的自动化工作流：用通用节点组合“信息采集 → 决策 → 执行”，同时允许现有 ECS 作为一个完整动作节点。Maa 负责成熟的任务编排、资源和控制器扩展点；EasyCon 负责 ECS 语义、设备协议、来源选择、标签兼容和脚本运行上下文。

v2 不要求用户把现有脚本改写成 Pipeline。推荐的三种入口是：

```text
现有 ECS 文件 ───────────────► EasyCon ECS 宿主
Maa Pipeline + EasyCon.RunEcs ► Maa Tasker / Pipeline 执行
EasyCon Flow ────────────────► 现有 FlowExecutor（兼容入口）
```

三条入口共用能力端口、FrameHub、音频源、标签转换和停止/报告模型；不复制三套设备连接逻辑。

### 4.2 Maa 适配层

| v2 组件 | 责任 | 不负责的事情 |
|---|---|---|
| `EasyConCustomController` | 把 Maa 的按键、摇杆、截图请求转为 `IPadInput` 和 `ICaptureSource` 调用 | 不枚举端口、不打开采集卡、不拥有连接生命周期 |
| `EasyConCustomRecognition` | 对需要保持旧 OpenCV 语义的标签执行适配算法，并把结果换算为旧分值 | 不改变 ECS 阈值，不另建隐藏的图像源 |
| `EasyConMaaOcr` | 把标准 Maa OCR 资源、ROI 和结果映射为 EasyCon OCR 返回值 | 不把模板图像误当成 OCR，不改 `OCR_CONF` 语义 |
| `EasyConRunEcs` | 在当前运行会话里执行一段 ECS 或一个 ECS 文件，借用既有能力租约 | 不启动第二个设备连接，不另建独立 GUI/CLI 进程 |
| `EasyConResourceGeneration` | 把 `.IL`/标签、ROI、OCR 配置转换为版本化 Maa 资源并预检 | 不修改源标签，不把生成目录当成用户源文件 |

Maa 版本和 binding 版本必须固定。不能因为某个预览版本存在接口，就把它写成 EasyCon 的稳定依赖；缺 API 时先记录适配缺口，再决定升级或在 Maa 目录添加薄封装。

### 4.3 标签与图像

旧标签格式继续是源格式，转换结果是派生资源：

```text
Legacy .IL / 标签
   ├─ 模板图像 + ROI + 算法参数 → Maa TemplateMatch / CustomRecognition
   └─ 文字标签 + ROI + 语言/模型 → Maa OCR 资源
```

转换器需要保存源摘要、转换器版本、资源版本和 1920×1080 坐标。运行时只加载与当前资源代次一致的索引；转换失败要在运行前报告，不能悄悄回退到另一套图像后端。

模板识别、OCR、`FRAME`、`ROI`、监视器全部从同一个 `FrameHub` 读取最新帧或租约帧。Maa 的截图回调只是这个帧源的适配出口，不允许 Maa 自己再打开一个采集设备。

### 4.4 工作流节点

v2 节点目录按信息、决策、执行分组，但数据边和执行边继续分开：

| 层 | 首批节点 | 说明 |
|---|---|---|
| 信息 | `capture.frame`、`roi.crop`、`ocr.text`、`vision.label`、`audio.match`、`script.value` | 输出图像、文字、分数、音频匹配分数或 ECS 返回值。 |
| 决策 | `compare`、`text.contains`、`script.predicate`、`agent.analyze`、`jev.analyze` | `agent`/`jev` 必须有超时、取消、输入输出 schema 和只读/可执行权限；“jev”在实现前必须补齐其算法或 provider 定义，不能擅自当作某个既有标准。 |
| 执行 | `pad.key`、`pad.sequence`、鼠标键盘、模拟器、自定义执行器、`run.ecs`、`wait` | 执行节点接受显式能力租约和停止令牌。`run.ecs` 是 Maa CustomAction 的首个闭环。 |

`run.ecs` 的输入至少包括脚本来源、参数、入口目录、超时和错误策略；输出包括 `ok`、返回值、日志和结构化错误。它必须复用 `ScriptHostAssembler`/未来的高层执行桥，不通过 CLI 子进程运行。

### 4.5 PC 与 MCU 的执行策略

v2 允许 PC 直接从 AST/SSA/高层 IR 执行，以支持桌面专属的 OCR、OpenCV、音频、LLM 和调试能力。MCU 路径仍然是：

```text
ECS → typed IR/SSA → MCU lowering → ECX1/v3 → C VM
```

PC 和 MCU 的正式对拍只比较两端能表达的公共子集：值语义、控制流、按键/等待、返回值、错误和取消。桌面专属能力在 MCU 上通过特征位和预检拒绝，不能编译成功后再让设备无响应。PC 上的 C VM 模拟只用于验证 MCU 内核。

### 4.6 cleanroom 模块重建

“cleanroom”在本方案中指独立按公开协议和行为契约重建，不指法律认证：

- 手柄模块只依赖串口协议、报文格式、握手、节流、重连和输入行为测试向量；
- 采集模块只依赖来源枚举、OpenCV/OBS 后端选择、帧尺寸、时间戳、断开和重连契约；
- 两者都不引用对方的私有类型，也不把 Maa 原生对象泄漏到模块公共接口；
- 通过录制报文、固定图像帧和故障注入做差分测试；通过后再挂入 `MaaControllerBinding`。

重建是可选的内部替换手段，不是 v2 的用户可见功能，也不是 dev 继续维护的前置条件。

## 5. v2 阶段大纲

| 阶段 | 交付 | 进入条件 |
|---|---|---|
| V2-0 契约冻结 | Pad/Capture/Audio 独立端口、FrameHub、资源代次、停止/错误模型、1920×1080 坐标 | dev 两项近期修正完成 |
| V2-1 适配器 | CustomController、Maa OCR、模板 CustomRecognition、标签转换器 | 旧 ECS 回放基线固定 |
| V2-2 `run.ecs` | Maa CustomAction 在同一 RunScope 执行 ECS，输出结构化结果 | 能力租约和取消可对拍 |
| V2-3 Pipeline/Flow 互操作 | Maa Pipeline 可调用 `run.ecs`；现有 Flow 继续运行并可调用同一节点语义 | 不改变已有 `.flow.json` |
| V2-4 决策节点 | script predicate、agent provider、jev provider、权限和超时 | provider 契约明确，默认只读 |
| V2-5 MCU 公共子集 | 高层 IR 到 ECX、C VM 执行、PC/MCU 对拍、容量和能力预检 | 先有有效 ECX 导出，再接硬件下发 |

任何阶段都必须保留一个可以运行的 ECS 直达入口；v2 不能以“全部节点迁移完成”作为可用条件。

## 6. 不纳入本轮的内容

- 不把现有 ECS 全量转换为 Pipeline。
- 不删除或改名现有 Flow，不把 `script.run` 宣布为“v1 已删除”。
- 不因为接 Maa 而合并手柄和采集源；端口与来源仍可分别选择。
- 不以 720p 重解释现有标签。
- 不把 PC 高层执行、MCU C VM 模拟和旧 C# 解释器描述成三个需要同时长期维护的正式产品端。
- 不在本轮实现 LLM/jev 的具体算法；先冻结 provider、schema、权限、超时和取消契约。
- 不把 MCU 烧录能力查询、容量预检和固件协议升级混入 GUI ECX 导出修正。

## 7. 验收定义

dev 维护完成的最低验收是：

1. 编译错误时无链接、无新 ECX、无固件封装、无烧录；重复诊断按根因去重。
2. 无错误（允许警告）时，CLI 与 GUI 对相同编辑器源码生成相同 ECX。
3. GUI 生成使用当前编辑器文本，并保留入口路径对 `IMPORT`、`lib/`、标签和音频目录的解析语义。
4. 手柄端口和采集来源可独立选择、独立断开，互不触发重连。
5. 识别、FRAME/ROI、OCR、监视器取同源帧；音频端点独立于视频和手柄。
6. Maa 适配层只作为宿主能力和动作/识别适配，不改变 ECS 源文件。

v2 验收再增加：Maa `run.ecs` 的执行/停止/返回值、标签转换回放、PC/MCU 公共子集对拍，以及节点目录和运行时的一致性。

## 8. 与旧方案的关系

`Maa接入详细方案-20261014.md`、`Maa接入实施任务书-20261015.md` 和 `PC高层执行与设备模块重建方案-20261018.md` 中关于协议兼容、独立端口/来源、统一帧源、旧分值、PC/MCU 两端对拍和 Maa 适配器的内容继续有效；其中要求立即改造整个执行架构的部分，按本文移入 v2。

本文不覆盖 `0000-cvm.patch` 的应用细节。该补丁先按已授权范围完成 C VM 内存布局/构建预算的处理；它与 GUI ECX 导出属于不同层，补丁通过后仍须单独完成上面的 ECX 编译闸门和 GUI 调用链验收。
